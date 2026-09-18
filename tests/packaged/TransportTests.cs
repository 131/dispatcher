using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml;
using Utils;

class TransportTests {
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static int Main(string[] args) {
        try {
            if (args.Length == 3 && args[0] == "--dispatcher-package-helper")
                return PackagedApplication.Helper(args[1], args[2]);
            string directory = Path.GetFullPath(args[0]);
            string probe = Path.Combine(directory, "probe.exe");
            string result = Path.Combine(directory, "probe.xml");
            string log = Path.Combine(directory, "probe.log");
            string cwd = Path.Combine(directory, "working directory é"); Directory.CreateDirectory(cwd);
            Environment.SetEnvironmentVariable("DISPATCHER_INHERITED", "inherited-é-漢字");
            Environment.SetEnvironmentVariable("DISPATCHER_OVERRIDE", "override=\"quote\"\r\nsecond line");
            Environment.SetEnvironmentVariable("DISPATCHER_HELPER_ONLY", null);
            string[] values = { "", "plain", "two words", "a\"b", "quoted \"words\"", "trailing space slash \\", "é漢字", "a&b|c%PATH%", "line1\nline2" };
            StringBuilder command = new StringBuilder(Dispatcher.Program.EncodeParameterArgument(result));
            foreach (string value in values) command.Append(' ').Append(Dispatcher.Program.EncodeParameterArgument(value));
            ProcessStartInfo holdingStart = new ProcessStartInfo(probe, "--hold");
            holdingStart.UseShellExecute = false;
            Process holding = Process.Start(holdingStart);
            uint exit;
            try {
                exit = PackagedApplication.RunCore(command.ToString(), cwd, false, true, false, log,
                delegate(string pipe) {
                    ProcessStartInfo start = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName);
                    start.Arguments = "--dispatcher-package-helper " + pipe + " " + Dispatcher.Program.EncodeParameterArgument(probe);
                    start.UseShellExecute = false;
                    start.EnvironmentVariables["DISPATCHER_HELPER_ONLY"] = "must-not-leak";
                    if (args.Length > 1) {
                        Require(File.Exists(PackagedApplication.ResolveExecutable(args[1])), "Native package resolution failed.");
                        return PackagedApplication.StartInPackage(args[1], start.FileName, start.Arguments);
                    }
                    return Process.Start(start);
                });
            } finally {
                if (!holding.HasExited) holding.Kill();
                holding.WaitForExit(); holding.Dispose();
            }
            Require(exit == 37, "Real target exit code was not preserved.");
            XmlDocument document = new XmlDocument(); document.Load(result);
            Require(document.DocumentElement.GetAttribute("cwd") == cwd, "CWD mismatch.");
            XmlNodeList actual = document.SelectNodes("/probe/arg");
            Require(actual.Count == values.Length, "Argument count mismatch.");
            for (int i = 0; i < values.Length; i++) Require(actual[i].InnerText == values[i], "Argument mismatch at " + i);
            foreach (string name in new string[] { "DISPATCHER_INHERITED", "DISPATCHER_OVERRIDE" })
                Require(document.SelectSingleNode("/probe/env[@name='" + name + "']").InnerText == Environment.GetEnvironmentVariable(name), "Environment mismatch: " + name);
            Require(((XmlElement)document.SelectSingleNode("/probe/env[@name='DISPATCHER_HELPER_ONLY']")).GetAttribute("present") == "false", "The helper environment leaked into the target.");
            string output = File.ReadAllText(log);
            Require(output.Contains("probe stdout") && output.Contains("probe stderr"), "OUTPUT redirection failed.");
            Console.WriteLine("PASS {0}-bit ({1}): ENV, ARGV, CWD, target exit code, OUTPUT, concurrent same-executable launch", IntPtr.Size * 8, args.Length > 1 ? "package context" : "ordinary context");
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
