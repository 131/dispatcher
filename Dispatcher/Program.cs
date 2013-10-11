using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using System.Net.Sockets;
using Utils;
using System.Text.RegularExpressions;

namespace Dispatcher
{
  class Program
  {
    static void Main()
    {
      string exePath;
      string args;

      Dictionary<string, string> envs = new Dictionary<string, string>();

      if (!ExtractCommandLine(out exePath, out args, envs))
        Environment.Exit(1);

      string exeDir = Path.GetDirectoryName(exePath);
      envs["Path"] =  Environment.GetEnvironmentVariable("PATH") + ";" + exeDir;
      foreach(KeyValuePair<string, string> env in envs)
        Environment.SetEnvironmentVariable(env.Key,  env.Value);

      RunC(exePath, args);
      //RunProcess(exePath, args);
    }



    private static void RunC(string exePath, string args)
    {
      var pInfo = new PROCESS_INFORMATION();
      var sInfoEx = new STARTUPINFOEX();
      sInfoEx.StartupInfo = new STARTUPINFO();


      sInfoEx.StartupInfo.dwFlags = Kernel32.STARTF_USESTDHANDLES | Kernel32.STARTF_USESHOWWINDOW;
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


    private static bool ExtractCommandLine(out string exePath, out string args, Dictionary<string, string> envs)
    {
      args = exePath = String.Empty;

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
            envs[e.Groups[1].Value] = e.Groups[2].Value;
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

    /**
     * Fails as OpenStandardInput might be a "char" (console)
     * And RedirectStandardInput force Process.StandartInput as "pipe"
     * Include chat.cs to get this to work
     * 
    private static void RunProcess(string exepath, string pargs)
    {

      string exeDir = Path.GetDirectoryName(exepath);
      if (!File.Exists(exepath))
        return;

      Process proc = new Process();
      proc.StartInfo.FileName = exepath;

      proc.StartInfo.UseShellExecute = false;

      proc.StartInfo.CreateNoWindow = true;

      proc.StartInfo.Arguments = pargs;
      proc.StartInfo.RedirectStandardOutput = true;
      proc.StartInfo.RedirectStandardInput = true;
      proc.StartInfo.RedirectStandardError = true;

      //some likes to be in the path (aka rsync)
      proc.StartInfo.EnvironmentVariables["PATH"] = Environment.GetEnvironmentVariable("PATH") + ";" + exeDir;


      proc.Start();
      ConsoleEx.AttachConsole(proc.Id);


      Stream instrm = Console.OpenStandardInput();
      Stream outstrm = Console.OpenStandardOutput();
      Stream errstrm = Console.OpenStandardError();


      var job = new Job();
      job.AddProcess(Process.GetCurrentProcess().Handle);
      job.AddProcess(proc.Handle);

      Chat error = Chat.Start(proc.StandardOutput.BaseStream, outstrm, ChatMethod.Sync, "pout > cout");
      Chat output = Chat.Start(proc.StandardError.BaseStream, errstrm, ChatMethod.Sync, "perr > cout");
      Chat io = Chat.Start(instrm, proc.StandardInput.BaseStream, ChatMethod.Async, "cin > pin");


      if (ConsoleEx.IsInputRedirected && ConsoleEx.IsInputDisk)
      {
        while (!io.EOF) ;
        io.Close();
      }


      proc.WaitForExit();

      error.End();
      output.End();
      io.End();

      while (error.Alive || output.Alive) ;


      Environment.Exit(proc.ExitCode);
    }

    static void Main_tcp()
    {

      TcpClient client = new TcpClient("127.0.0.1", 8000);
      NetworkStream stream = client.GetStream();

      Stream instrm = Console.OpenStandardInput();
      Stream outstrm = Console.OpenStandardOutput();


      Chat.Start(stream, outstrm, ChatMethod.CopyTo, "perr > cout");
      Chat.Start(instrm, stream, ChatMethod.CopyTo,  "cin > pin");
    }

    */

  }











}
