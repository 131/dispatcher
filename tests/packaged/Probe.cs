using System;
using System.IO;
using System.Xml;

class PackageProbe {
    static int Main(string[] args) {
        if (args.Length > 0 && args[0] == "--hold") {
            if (args.Length > 1) File.WriteAllText(args[1], System.Diagnostics.Process.GetCurrentProcess().Id.ToString());
            System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite); return 0;
        }
        XmlDocument document = new XmlDocument();
        XmlElement root = document.CreateElement("probe"); document.AppendChild(root);
        root.SetAttribute("cwd", Environment.CurrentDirectory);
        foreach (string name in new string[] { "DISPATCHER_INHERITED", "DISPATCHER_OVERRIDE", "DISPATCHER_HELPER_ONLY" }) {
            XmlElement variable = document.CreateElement("env");
            variable.SetAttribute("name", name);
            variable.SetAttribute("present", Environment.GetEnvironmentVariable(name) != null ? "true" : "false");
            variable.InnerText = Environment.GetEnvironmentVariable(name) ?? "";
            root.AppendChild(variable);
        }
        for (int i = 1; i < args.Length; i++) { XmlElement argument = document.CreateElement("arg"); argument.InnerText = args[i]; root.AppendChild(argument); }
        document.Save(args[0]);
        Console.WriteLine("probe stdout"); Console.Error.WriteLine("probe stderr");
        return 37;
    }
}
