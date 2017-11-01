using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Configuration;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Utils;

namespace Dispatcher
{
  class Program
  {
    private static string exePath;
    private static string args;
    private static bool use_showwindow;

    const string FLAG_USE_SHOWWINDOW = "USE_SHOWWINDOW";

    static void Main()
    {

      Dictionary<string, string> envs = new Dictionary<string, string>();
      if (!ExtractCommandLine(out exePath, out args, out use_showwindow, envs))
        Environment.Exit(1);

      string exeDir = Path.GetDirectoryName(exePath);
      envs["Path"] =  Environment.GetEnvironmentVariable("PATH") + ";" + exeDir;
      foreach(KeyValuePair<string, string> env in envs)
        Environment.SetEnvironmentVariable(env.Key,  env.Value);

      var pInfo = new PROCESS_INFORMATION();
      var sInfoEx = new STARTUPINFOEX();
      sInfoEx.StartupInfo = new STARTUPINFO();

      sInfoEx.StartupInfo.dwFlags = Kernel32.STARTF_USESTDHANDLES;

      if (use_showwindow)
        sInfoEx.StartupInfo.dwFlags |= Kernel32.STARTF_USESHOWWINDOW;

      IntPtr iStdOut = Kernel32.GetStdHandle(Kernel32.STD_OUTPUT_HANDLE);
      IntPtr iStdErr = Kernel32.GetStdHandle(Kernel32.STD_ERROR_HANDLE);
      IntPtr iStdIn = Kernel32.GetStdHandle(Kernel32.STD_INPUT_HANDLE);

      sInfoEx.StartupInfo.wShowWindow = Kernel32.SW_HIDE;
      sInfoEx.StartupInfo.hStdInput = iStdIn;
      sInfoEx.StartupInfo.hStdOutput = iStdOut;
      sInfoEx.StartupInfo.hStdError = iStdErr;

      Kernel32.CreateProcess(
          null, exePath + " " + args, IntPtr.Zero, IntPtr.Zero, true,
          Kernel32.STARTF_USESTDHANDLES,
          IntPtr.Zero, null, ref sInfoEx, out pInfo);

      Kernel32.CloseHandle(pInfo.hThread);

      //make sure dispatcher kill its child process when killed
      var job = new Job();
      job.AddProcess(Process.GetCurrentProcess().Handle);
      job.AddProcess(pInfo.hProcess);

      Kernel32.WaitForSingleObject(pInfo.hProcess, Kernel32.INFINITE);

      uint exitCode;
      Kernel32.GetExitCodeProcess(pInfo.hProcess, out exitCode);

      //clean up
      Kernel32.CloseHandle(pInfo.hProcess);
      Kernel32.CloseHandle(iStdOut);
      Kernel32.CloseHandle(iStdErr);
      Kernel32.CloseHandle(iStdIn);

      Environment.Exit((int) exitCode);
    }

        private static bool ExtractCommandLine(out string exePath, out string args, out bool use_showwindow, Dictionary<string, string> envs)
        {
            args = exePath = String.Empty;
#if DISPACHER_WIN
      use_showwindow = false;
#else
      use_showwindow = true;
#endif
            exePath = ConfigurationManager.AppSettings["PATH"];

            if (ConfigurationManager.AppSettings[FLAG_USE_SHOWWINDOW] != null)
                use_showwindow = !(ConfigurationManager.AppSettings[FLAG_USE_SHOWWINDOW] == "0" || ConfigurationManager.AppSettings[FLAG_USE_SHOWWINDOW] == "");

            if (String.IsNullOrEmpty(exePath))
            {
                Console.Error.WriteLine("Cannot resolve cmd");
                return false;
            }
            
            string dispatched_cmd = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]);

            string dispatcher = Assembly.GetExecutingAssembly().Location;
            string dispatcher_dir = Path.GetDirectoryName(dispatcher);

            exePath = Path.GetFullPath(Path.Combine(dispatcher_dir, exePath));

            Dictionary<string, string> replaces = new Dictionary<string, string> {
                {"%dwd%", dispatcher_dir},
            };

            List<string> values = new List<string>();
            foreach (string key in ConfigurationManager.AppSettings) {
                string value = ConfigurationManager.AppSettings[key];
                if (key.StartsWith("ARGV"))
                    args += (Regex.IsMatch(value, "^[a-zA-Z0-9_./:^-]+$") ? value :  "\"" + value + "\"") + " ";
                if (key.StartsWith("ENV_"))
                    envs[key.Remove(0,4)] = Replace(value, replaces);
            }

            var argsStart = Environment.CommandLine.IndexOf(" ", dispatched_cmd.Length);
            if(argsStart != -1)
                args += Environment.CommandLine.Substring(argsStart + 1);
            
            return true;
        }
    public static string Replace(string str, Dictionary<string, string> dict)
    {
      foreach (KeyValuePair<string, string> replacement in dict)
        str = str.Replace(replacement.Key, replacement.Value);
      return str;
    }

  }

}
