using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using System.Net.Sockets;
using Utils;

namespace Dispatcher
{
  class Program
  {
    static void Main()
    {
      //ConsoleEx.AttachConsole(Process.GetCurrentProcess().Id);

      ConsoleEx.AllocConsole();


      string dispatched_cmd = Environment.GetCommandLineArgs()[0];
      var argsStart = Environment.CommandLine.IndexOf(" ", dispatched_cmd.Length);
      var initargs = argsStart != -1 ? Environment.CommandLine.Substring(argsStart + 1) : "";

      dispatched_cmd = "php";
      string dispatcher = Assembly.GetExecutingAssembly().Location;
      string dispatcher_dir = Path.GetDirectoryName(dispatcher);
      string text = System.IO.File.ReadAllText(dispatcher_dir + "\\dispatch");

      var cmd = new string[0];
      Dictionary<string, string> replaces = new Dictionary<string, string>
      {
        {"%dwd%", dispatcher_dir},
      };

      foreach (var rawline in text.Split(new string[] { "\r\n" }, StringSplitOptions.None))
      {
        var line = Replace(rawline, replaces);
        var entry = line.Split(new string[] { " " }, 3, StringSplitOptions.None);

        if (entry.Length < 2) continue;
        if (dispatched_cmd == entry[0])
        {
          cmd = entry;
          break;
        }
      }
      if (cmd.Length == 0)
        return;

      string exepath = Path.GetFullPath(Path.Combine(dispatcher_dir, cmd[1]));
      string exeDir = Path.GetDirectoryName(exepath);
      if (!File.Exists(exepath))
        return;

      string pargs = (cmd.Length > 2 ? cmd[2] + " " : "") + initargs;

      Process proc = new Process();
      proc.StartInfo.FileName = exepath;

      // Si l'on veut rediriger le sortie du process il faut que l'on utilse UseShellExecute = false
      proc.StartInfo.UseShellExecute = false;

      // Attention : pour rediriger StandardOutput et StandardError en meme temps
      // il faut faire 2 threads pour ne pas avoir de dead lock.
      proc.StartInfo.CreateNoWindow = true;

      proc.StartInfo.Arguments = pargs;
      proc.StartInfo.RedirectStandardOutput = true;
      proc.StartInfo.RedirectStandardInput = true;
      proc.StartInfo.RedirectStandardError = true;

        //some likes to be in the path (aka rsync)
      proc.StartInfo.EnvironmentVariables["PATH"] = Environment.GetEnvironmentVariable("PATH") + ";" + exeDir;


      //proc.StartInfo.StandardErrorEncoding = Encoding.

      //proc.StartInfo.WorkingDirectory = workingDir;


 
      proc.Start();

      ConsoleEx.AttachConsole(proc.Id);


      Stream instrm = Console.OpenStandardInput();
      Stream outstrm = Console.OpenStandardOutput();


      var job = new Job();
      job.AddProcess(Process.GetCurrentProcess().Handle);
      job.AddProcess(proc.Handle);

      Chat.Start(proc.StandardOutput.BaseStream, outstrm, ChatMethod.CopyTo, "pout > cout");
      Chat.Start(proc.StandardError.BaseStream, outstrm, ChatMethod.CopyTo, "perr > cout");
      Chat.Start(instrm, proc.StandardInput.BaseStream, ChatMethod.Async, "cin > pin");

      proc.WaitForExit();
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

    public static string Replace(string str, Dictionary<string, string> dict)
    {
      foreach (KeyValuePair<string, string> replacement in dict)
        str = str.Replace(replacement.Key, replacement.Value);
      return str;
    }
  }











}
