using System;
using System.Threading;

class EnvironmentProviderFixture {
    static int Main(string[] args) {
        string mode = args.Length == 0 ? "json" : args[0];
        if (mode == "json") {
            if (Environment.GetEnvironmentVariable("PROVIDER_INPUT") != "input" || !Environment.CurrentDirectory.EndsWith("provider cwd")) return 9;
            Console.WriteLine("{\"ALPHA\":\"one\",\"mixed\":\"provider\"}"); return 0;
        }
        if (mode == "invalid") { Console.WriteLine("nope"); return 0; }
        if (mode == "fail") return 7;
        if (mode == "wait") { Thread.Sleep(5000); return 0; }
        return 2;
    }
}
