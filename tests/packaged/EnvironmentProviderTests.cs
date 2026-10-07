using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Utils;

class EnvironmentProviderTests {
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static int Main(string[] args) {
        try {
            string fixture = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), "environment-provider-fixture.exe");
            string command = "\"" + fixture + "\"";
            Dictionary<string, string> values = EnvironmentProvider.Run(command + " json", 5000);
            Require(values["ALPHA"] == "one" && values["MIXED"] == "provider", "JSON values or case-insensitive lookup failed.");

            bool invalid = false;
            try { EnvironmentProvider.Run(command + " invalid", 5000); }
            catch (InvalidOperationException error) { invalid = error.Message.Contains("valid JSON"); }
            Require(invalid, "Invalid JSON was accepted.");

            bool exit = false;
            try { EnvironmentProvider.Run(command + " fail", 5000); }
            catch (InvalidOperationException error) { exit = error.Message.Contains("code 7"); }
            Require(exit, "Provider failure was accepted.");

            bool timeout = false;
            Stopwatch watch = Stopwatch.StartNew();
            try { EnvironmentProvider.Run(command + " wait", 200); }
            catch (TimeoutException) { timeout = true; }
            Require(timeout && watch.ElapsedMilliseconds < 3000, "Provider timeout failed.");

            Console.WriteLine("PASS ENV_PROVIDER JSON, case-insensitive values, invalid JSON, exit code and timeout");
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
