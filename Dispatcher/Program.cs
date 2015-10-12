using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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

    const string FLAG_DISPATCHER_GUI = "DISPATCHER_GUI"; //toggle !use_showwindow

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


      string dispatched_cmd = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]);

      var argsStart = Environment.CommandLine.IndexOf(" ", dispatched_cmd.Length);
      args = argsStart != -1 ? Environment.CommandLine.Substring(argsStart + 1) : "";

      string dispatcher = Assembly.GetExecutingAssembly().Location;
      string dispatcher_dir = Path.GetDirectoryName(dispatcher);
      string text = System.IO.File.ReadAllText(dispatcher_dir + "\\dispatch");

      var cmd = new string[0];
      Dictionary<string, string> replaces = new Dictionary<string, string>
      {
        {"%dwd%", dispatcher_dir},
      };

      string[] lines = text.Split(new string[] { "\r\n" }, StringSplitOptions.None);
      for(var i=0;i<lines.Length; i++)
      {
        var line = Replace(lines[i], replaces);
        var entry = line.Split(new string[] { " " }, 3, StringSplitOptions.None);

        if (entry.Length < 2) continue;
        if (dispatched_cmd == entry[0])
        {
          cmd = entry;
          for (int j = i+1; j < lines.Length; j++)
          {
            Match e = Regex.Match(lines[j], @"^\s*([a-z_0-9-]+)\s*=\s*(.+)?", RegexOptions.IgnoreCase);
            if(!e.Success)break;

            if(e.Groups[1].Value == FLAG_DISPATCHER_GUI)
              use_showwindow = false;
            else envs[e.Groups[1].Value] = Replace(e.Groups[2].Value, replaces);
          }

          break;
        }
      }

      if (cmd.Length == 0)
      {
        Console.Error.WriteLine("Cannot resolve cmd");
        return false;
      }

      args = (cmd.Length > 2 ? cmd[2] + " " : "") + args;
      exePath = Path.GetFullPath(Path.Combine(dispatcher_dir, cmd[1]));
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
