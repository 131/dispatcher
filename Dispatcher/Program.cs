using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Configuration;
using System.Diagnostics;
using System.Threading;
using System.Text.RegularExpressions;
using Utils;
using System.ServiceProcess;
using System.Runtime.InteropServices;
using murrayju.ProcessExtensions;

namespace Dispatcher {



    public class Program
    {



      //Console CTRL+C is delegated to subprocess, we have nothing to do here
      private static bool ConsoleCtrlCheck(Kernel32.CtrlTypes ctrlType)
      {
          return true;
      }
  
        const string FLAG_USE_SHOWWINDOW = "USE_SHOWWINDOW";
        static string exePath;
        static string args;
        static string cwd;
        static string execPreCmd;

        static string logsPath = "";
        static bool use_showwindow;
        static bool as_service;
        static bool as_desktop_user;
        static bool use_job;
        static uint exitCode;
        static Dictionary<string, string> envs;

        internal  static PROCESS_INFORMATION pInfo;


        static void Main()
        {

            Kernel32.SetConsoleCtrlHandler(new Kernel32.HandlerRoutine(ConsoleCtrlCheck), true);

            envs = new Dictionary<string, string>();
            envs["PATH"] = Environment.GetEnvironmentVariable("PATH");

            if (!ExtractCommandLine())
                Environment.Exit(1);

            string exeDir = Path.GetDirectoryName(exePath);
            envs["PATH"] = envs["PATH"] + ";" + exeDir;

            foreach (KeyValuePair<string, string> env in envs)
                Environment.SetEnvironmentVariable(env.Key, env.Value);

            if (as_service) {
                ServiceBase[] ServicesToRun;
                ServicesToRun = new ServiceBase[]
                {
                    new Service1()
                };
                ServiceBase.Run(ServicesToRun);
            } else {
                Run();
                Environment.Exit((int)exitCode);
            }
        }

        public static void Run() {

            if (as_desktop_user)
            {
                if(execPreCmd != null) {
                    pInfo = ProcessExtensions.StartProcessAsCurrentUser(execPreCmd, envs, "", cwd, !use_showwindow);
                    Kernel32.WaitForSingleObject(pInfo.hProcess, Kernel32.INFINITE);
                    Kernel32.CloseHandle(pInfo.hThread);
                    Kernel32.CloseHandle(pInfo.hProcess);
                }



                pInfo = ProcessExtensions.StartProcessAsCurrentUser(exePath, envs, args, cwd, !use_showwindow);

                if (use_job)
                {
                    var job2 = new Job();
                    job2.AddProcess(pInfo.hProcess);
                }

                Kernel32.WaitForSingleObject(pInfo.hProcess, Kernel32.INFINITE);
                Kernel32.CloseHandle(pInfo.hThread);
                Kernel32.CloseHandle(pInfo.hProcess);
                Kernel32.GetExitCodeProcess(pInfo.hProcess, out exitCode);
                return;
            }


            //make sure dispatcher kill its child process when killed
            if (use_job)
            {
              var job = new Job();
              job.AddProcess(Process.GetCurrentProcess().Handle);
            }


          if(execPreCmd != null) {
            var startInfo = new ProcessStartInfo(execPreCmd);
            startInfo.WorkingDirectory = cwd;

            using (Process exeProcess = Process.Start(startInfo))
             {
                  exeProcess.WaitForExit();
              }
          }


            pInfo = new PROCESS_INFORMATION();
            var sInfoEx = new STARTUPINFOEX();
            sInfoEx.StartupInfo = new STARTUPINFO();

            sInfoEx.StartupInfo.dwFlags = Kernel32.STARTF_USESTDHANDLES;

            if (use_showwindow)
                sInfoEx.StartupInfo.dwFlags |= Kernel32.STARTF_USESHOWWINDOW;

            IntPtr iStdOut = Kernel32.GetStdHandle(Kernel32.STD_OUTPUT_HANDLE);
            IntPtr iStdErr = Kernel32.GetStdHandle(Kernel32.STD_ERROR_HANDLE);
            IntPtr iStdIn = Kernel32.GetStdHandle(Kernel32.STD_INPUT_HANDLE);
            IntPtr hLogs = IntPtr.Zero;


            sInfoEx.StartupInfo.wShowWindow = Kernel32.SW_HIDE;
            sInfoEx.StartupInfo.hStdInput = iStdIn;
            sInfoEx.StartupInfo.hStdOutput = iStdOut;
            sInfoEx.StartupInfo.hStdError = iStdErr;

            if (!String.IsNullOrEmpty(logsPath)) {
                SECURITY_ATTRIBUTES lpSecurityAttributes = new SECURITY_ATTRIBUTES();
                lpSecurityAttributes.bInheritHandle = 1;
                lpSecurityAttributes.nLength = Marshal.SizeOf(lpSecurityAttributes);
                hLogs = Kernel32.CreateFile(logsPath, Kernel32.DesiredAccess.FILE_APPEND_DATA, 0x00000003 //share read& w
                , lpSecurityAttributes, Kernel32.CreationDisposition.OPEN_ALWAYS, 0, IntPtr.Zero);

                sInfoEx.StartupInfo.hStdOutput = hLogs;
                sInfoEx.StartupInfo.hStdError = hLogs;
            }

            uint dwCreationFlags = Kernel32.CREATE_UNICODE_ENVIRONMENT;
            if (!use_job)
                dwCreationFlags |= Kernel32.CREATE_BREAKAWAY_FROM_JOB;

            Kernel32.CreateProcess(
                null, exePath + " " + args, IntPtr.Zero, IntPtr.Zero, true,
                dwCreationFlags,
                IntPtr.Zero, cwd, ref sInfoEx, out pInfo);

            Kernel32.CloseHandle(pInfo.hThread);
            Kernel32.WaitForSingleObject(pInfo.hProcess, Kernel32.INFINITE);
            Kernel32.GetExitCodeProcess(pInfo.hProcess, out exitCode);

            //clean up
            if(hLogs != IntPtr.Zero)
                Kernel32.CloseHandle(hLogs);
            Kernel32.CloseHandle(pInfo.hProcess);
            Kernel32.CloseHandle(iStdOut);
            Kernel32.CloseHandle(iStdErr);
            Kernel32.CloseHandle(iStdIn);
        }

        private static bool ExtractCommandLine() {
            string dispatcher = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
            string dispatcher_dir = Path.GetDirectoryName(dispatcher);
            cwd = null; //inherit
            as_service = false;
            as_desktop_user = false;
            use_job = true;

            //ConfigurationManager.AppSettings do not expand exe short name (e.g. search for a missing C:\windows\system32\somelo~1.config file)

            KeyValueConfigurationCollection config = new KeyValueConfigurationCollection();

            if (ConfigurationManager.AppSettings["PATH"] != null) {
                foreach (string key in ConfigurationManager.AppSettings)
                    config.Add(key, ConfigurationManager.AppSettings[key]);
            } else {
                config = ConfigurationManager.OpenExeConfiguration(dispatcher).AppSettings.Settings;
            }

            args = String.Empty;

#if DISPACHER_WIN
      use_showwindow = false;
#else
            use_showwindow = true;
#endif

            exePath = config["PATH"].Value;

            if (config[FLAG_USE_SHOWWINDOW] != null)
                use_showwindow = !(config[FLAG_USE_SHOWWINDOW].Value == "0" || config[FLAG_USE_SHOWWINDOW].Value == "");

            if (String.IsNullOrEmpty(exePath)) {
                Console.Error.WriteLine("Cannot resolve cmd");
                return false;
            }

            string dispatched_cmd = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]);

            string exefoo = Path.GetFullPath(Path.Combine(dispatcher_dir, exePath));
            if (File.Exists(exefoo))
                exePath = exefoo; //let windows resolve it
            
            Dictionary<string, string> replaces = new Dictionary<string, string> {
                {"%dwd%", dispatcher_dir},
                {"%Y%",  DateTime.Now.ToString("yyyy")},
                {"%m%", DateTime.Now.ToString("MM")},
                {"%d%", DateTime.Now.ToString("dd")},
                {"%H%", DateTime.Now.ToString("HH")},
                {"%i%", DateTime.Now.ToString("mm")},
                {"%s%", DateTime.Now.ToString("ss")},
            };

            
            foreach (string key in config.AllKeys) {
                string value = config[key].Value;
                value = Replace(value, replaces);
                value = Environment.ExpandEnvironmentVariables(value);
                if (key.StartsWith("ARGV"))
                    args += (Regex.IsMatch(value, "^[a-zA-Z0-9_./:^,=-]+$") ? value : "\"" + value + "\"") + " ";
                if (key.StartsWith("ENV_"))
                    envs[key.Remove(0, 4)] = value;
                if (key == "PRESTART_CMD")
                    execPreCmd = value;
                if (key == "CWD")
                    cwd = value;
                if (key == "AS_SERVICE")
                    as_service = true;
                if (key == "AS_DESKTOP_USER") {
                    as_desktop_user = true;
                } if (key == "OUTPUT")
                    logsPath = value;
                if(key == "USE_JOB")
                    use_job = ! isFalse(value);
            }
            var argv = System.Environment.GetCommandLineArgs();

            for(var i = 1; i < argv.Length; i++) {
                string value = argv[i];
                args += (Regex.IsMatch(value, "^[a-zA-Z0-9_./:^,=-]+$") ? value : "\"" + value + "\"") + " ";
            }

            return true;
        }
        public static string Replace(string str, Dictionary<string, string> dict) {
            foreach (KeyValuePair<string, string> replacement in dict)
                str = str.Replace(replacement.Key, replacement.Value);
            return str;
        }

        public static bool isFalse(string str) {
          return String.IsNullOrEmpty(str) || str == "false" || str == "0";
        }
        
        
        




    }


    public class Service1 : ServiceBase {

        protected Thread t;
        protected override void OnStart(string[] args)
        {

            t = new Thread(() => Run());
            t.Start();
        }
        protected void Run()
        {
          int delay = 1000;
          while(true) {
            Program.Run();
            Thread.Sleep(delay);
            delay = delay * 2;
          }
        }

        protected override void OnStop()
        {

          t.Abort();
          try {
            Process remote= Process.GetProcessById(Program.pInfo.dwProcessId);
            remote.Kill();
          } catch(Exception err) { }
        }

    }
}


