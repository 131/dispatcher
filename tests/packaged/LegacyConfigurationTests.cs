using System;
using System.IO;
using System.Reflection;
using Dispatcher;

class LegacyConfigurationTests {
    static string Document(string settings) {
        return "<?xml version=\"1.0\"?><configuration><appSettings>" + settings + "</appSettings></configuration>";
    }

    static void Require(bool condition, string message) {
        if (!condition) throw new Exception(message);
    }

    static void Main() {
        foreach (string value in new[] { "", "false", "False", "FALSE", "0" })
            Require(!Program.toBool(value), "Wrong false boolean.");
        foreach (string value in new[] { "true", "True", "TRUE", "1" })
            Require(Program.toBool(value), "Wrong true boolean.");
        foreach (string value in new[] { "yes", "no", "2", " true " }) {
            bool rejected = false;
            try { Program.toBool(value); } catch (ArgumentException) { rejected = true; }
            Require(rejected, "Arbitrary boolean accepted.");
        }
        string executable = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
        string name = Path.Combine(Path.GetDirectoryName(executable), Path.GetFileNameWithoutExtension(executable));
        string settings = "<add key=\"PATH\" value=\"cmd.exe\"/><add key=\"SERVICE_RESTART_ON_NETWORK_CHANGE\" value=\"False\"/>";
        File.WriteAllText(name + ".config", Document(settings));
        var extract = typeof(Program).GetMethod("ExtractCommandLine", BindingFlags.Static | BindingFlags.NonPublic);
        Require((bool)extract.Invoke(null, null), "Configuration extraction failed.");
        Require(!Program.restart_on_network_change, "False restart option was enabled.");
        File.WriteAllText(name + ".config", Document("<add key=\"ORDER\" value=\"base\"/>"));
        using (var locked = new FileStream(name + ".config", FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
            bool unreadable = false;
            try { ConfigurationParser.loadConfig(); } catch (InvalidOperationException) { unreadable = true; }
            Require(unreadable, "Unreadable existing configuration was ignored.");
        }
        Directory.CreateDirectory(name + ".config.d");
        File.WriteAllText(Path.Combine(name + ".config.d", "z.config"), Document("<add key=\"ORDER\" value=\"last\"/>"));
        File.WriteAllText(Path.Combine(name + ".config.d", "a.config"), Document("<add key=\"ORDER\" value=\"earlier\"/>"));
        Require(ConfigurationParser.loadConfig()["ORDER"] == "last", "Fragments are not ordered.");
        File.WriteAllText(name + ".config", "<?xml broken");
        bool invalid = false;
        try { ConfigurationParser.loadConfig(); } catch (InvalidOperationException) { invalid = true; }
        Require(invalid, "Invalid existing configuration was ignored.");
        File.WriteAllText(name + ".config", Document(""));
        Console.WriteLine("PASS explicit booleans, false restart, sorted fragments and invalid XML rejection");
    }
}
