using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Utils;

class TransportTests {
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static Process StartHelper(string probe, string pipe, string appName) {
        var start = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName);
        start.Arguments = "--dispatcher-package-helper " + pipe + " " + Dispatcher.Program.EncodeParameterArgument(probe);
        start.UseShellExecute = false;
        start.EnvironmentVariables["DISPATCHER_HELPER_ONLY"] = "must-not-leak";
        if (appName != null) {
            Require(File.Exists(PackagedApplication.ResolveExecutable(appName)), "Native package resolution failed.");
            return PackagedApplication.StartInPackage(appName, start.FileName, start.Arguments);
        }
        return Process.Start(start);
    }

    static async Task CancelLaunch(string directory, string probe, string appName, bool disconnect) {
        string name = "dispatcher-cancel-test-" + Guid.NewGuid().ToString("N");
        string marker = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".xml");
        using (PackagePipe pipe = PackagePipe.Server(name))
        using (Process helper = StartHelper(probe, name, appName)) {
            await pipe.AcceptAsync(helper);
            Require(await pipe.ReadInt32Async() == 1, "Wrong helper protocol.");
            Require(await pipe.ReadInt32Async() == helper.Id, "Wrong helper PID.");
            await pipe.ReadStringAsync();
            await pipe.WriteAsync(writer => {
                writer.Write(Dispatcher.Program.EncodeParameterArgument(marker));
                writer.Write(directory); writer.Write(true); writer.Write(false); writer.Write("");
                writer.Write(Process.GetCurrentProcess().Id);
                for (int i = 0; i < 3; i++) writer.Write(0L);
                writer.Write(0); // Explicitly empty target environment.
            });
            Require(await pipe.ReadBooleanAsync(), "The helper could not create the suspended probe.");
            using (Process target = Process.GetProcessById(await pipe.ReadInt32Async())) {
                IntPtr handle = target.Handle; // Retain the process before requesting cancellation.
                try {
                    if (disconnect) pipe.Dispose();
                    else {
                        await pipe.WriteAsync(writer => writer.Write(false));
                        Require(!await pipe.ReadBooleanAsync(), "Cancellation was acknowledged as a successful launch.");
                        Require((await pipe.ReadStringAsync()).Contains("cancelled"), "Cancellation error was lost.");
                    }
                    Require(target.WaitForExit(5000), "Cancelled target was left suspended.");
                    Require(helper.WaitForExit(5000) && helper.ExitCode == 1, "Cancelled helper did not exit.");
                    Require(!File.Exists(marker), "The target ran before the parent authorized it.");
                } finally {
                    if (!target.HasExited) target.Kill();
                    if (!helper.HasExited) helper.Kill();
                }
            }
        }
    }

    static void DetachedLaunch(string directory, string probe, string appName) {
        string marker = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".pid");
        uint exit = PackagedApplication.RunCore("--hold " + Dispatcher.Program.EncodeParameterArgument(marker),
            directory, true, true, true, "", pipe => StartHelper(probe, pipe, appName));
        Require(exit == 0, "Detached launch did not return success.");
        var watch = Stopwatch.StartNew();
        while ((!File.Exists(marker) || new FileInfo(marker).Length == 0) && watch.ElapsedMilliseconds < 5000) Thread.Sleep(20);
        Require(File.Exists(marker), "Detached target never started.");
        using (Process target = Process.GetProcessById(int.Parse(File.ReadAllText(marker)))) {
            try { Require(!target.HasExited, "Detached target died when the dispatcher returned."); }
            finally { if (!target.HasExited) target.Kill(); target.WaitForExit(); }
        }
    }

    static void RejectUnexpectedClient(string directory, string probe) {
        using (Process holding = Process.Start(new ProcessStartInfo(probe, "--hold") { UseShellExecute = false })) {
            int holdingId = holding.Id;
            PackagePipe impostor = null;
            try {
                try {
                    PackagedApplication.RunCore("", directory, true, false, false, "", pipe => {
                        impostor = PackagePipe.ClientAsync(pipe).GetAwaiter().GetResult();
                        return holding;
                    });
                    throw new Exception("The dispatcher accepted a pipe client with the wrong PID.");
                } catch (IOException error) { Require(error.Message.Contains("Unexpected package helper"), "Wrong peer rejection error."); }
                try {
                    impostor.ReadBooleanAsync().GetAwaiter().GetResult();
                    throw new Exception("The dispatcher sent data to an unauthenticated peer.");
                } catch (EndOfStreamException) { }
            } finally {
                if (impostor != null) impostor.Dispose();
                // RunCore disposes its bootstrap Process object; reacquire for test cleanup.
                using (Process child = Process.GetProcessById(holdingId)) { if (!child.HasExited) child.Kill(); child.WaitForExit(); }
            }
        }
    }
    static int Main(string[] args) {
        try {
            if (args.Length == 3 && args[0] == "--dispatcher-package-helper")
                return PackagedApplication.Helper(args[1], args[2]);
            string directory = Path.GetFullPath(args[0]);
            string appName = args.Length > 1 ? args[1] : null;
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
                pipe => StartHelper(probe, pipe, appName));
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
            CancelLaunch(directory, probe, appName, false).GetAwaiter().GetResult();
            CancelLaunch(directory, probe, appName, true).GetAwaiter().GetResult();
            DetachedLaunch(directory, probe, appName);
            if (appName == null) RejectUnexpectedClient(directory, probe);
            Console.WriteLine("PASS cancelled/disconnected launch cleanup, detached target lifetime and peer identity checks");
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
