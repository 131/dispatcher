using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Text.RegularExpressions;
using Utils;
using System.ServiceProcess;
using System.Runtime.InteropServices;
using murrayju.ProcessExtensions;
using System.Xml;
using System.Net.NetworkInformation;
using System.Net;

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
        public static bool uwf_servicing_disabled;
        static bool use_job;
        static bool detached;
#if DISPACHER_WIN
        static bool DISPACHER_WIN = true;
#else
        static bool DISPACHER_WIN = false;
#endif

        public static bool restart_on_network_change;

        static uint exitCode;
        static Dictionary<string, string> envs;


        internal  static PROCESS_INFORMATION pInfo;



        static void Main()
        {

            Kernel32.SetConsoleCtrlHandler(new Kernel32.HandlerRoutine(ConsoleCtrlCheck), true);

            envs = new Dictionary<string, string>();
            envs["Path"] = Environment.GetEnvironmentVariable("PATH");

            if (!ExtractCommandLine())
                Environment.Exit(1);

            string exeDir = Path.GetDirectoryName(exePath);
            envs["Path"] = envs["Path"] + ";" + exeDir;

            Environment.SetEnvironmentVariable("PATH", null);
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
                    pInfo = ProcessExtensions.StartProcessAsCurrentUser(execPreCmd, envs, "", cwd, !use_showwindow, logsPath);
                    Kernel32.WaitForSingleObject(pInfo.hProcess, Kernel32.INFINITE);
                    Kernel32.CloseHandle(pInfo.hThread);
                    Kernel32.CloseHandle(pInfo.hProcess);
                }



                pInfo = ProcessExtensions.StartProcessAsCurrentUser(exePath, envs, args, cwd, !use_showwindow, logsPath);

                if (use_job)
                {
                    var job2 = new Job();
                    job2.AddProcess(pInfo.hProcess);
                }

                Kernel32.WaitForSingleObject(pInfo.hProcess, Kernel32.INFINITE);
                Kernel32.GetExitCodeProcess(pInfo.hProcess, out exitCode);
                Kernel32.CloseHandle(pInfo.hThread);
                Kernel32.CloseHandle(pInfo.hProcess);
                return;
            }


            //make sure dispatcher kill its child process when killed
            if (use_job && !detached)
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
            sInfoEx.StartupInfo.cb = Marshal.SizeOf(sInfoEx);

            IntPtr lpValue = IntPtr.Zero;


            sInfoEx.StartupInfo.dwFlags = Kernel32.STARTF_USESTDHANDLES;

            if (use_showwindow)
                sInfoEx.StartupInfo.dwFlags |= Kernel32.STARTF_USESHOWWINDOW;

            IntPtr iStdOut = Kernel32.GetStdHandle(Kernel32.STD_OUTPUT_HANDLE);
            IntPtr iStdErr = Kernel32.GetStdHandle(Kernel32.STD_ERROR_HANDLE);
            IntPtr iStdIn = Kernel32.GetStdHandle(Kernel32.STD_INPUT_HANDLE);
            IntPtr hLogs = IntPtr.Zero;


            sInfoEx.StartupInfo.wShowWindow = Kernel32.SW_HIDE; //? Kernel32.SW_NORMAL
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
            dwCreationFlags |= Kernel32.EXTENDED_STARTUPINFO_PRESENT;

            if(detached)
              dwCreationFlags |= Kernel32.DETACHED_PROCESS;

            if (!use_job)
                dwCreationFlags |= Kernel32.CREATE_BREAKAWAY_FROM_JOB;


            if(detached) {
              IntPtr parentHandle = ParentProcessUtilities.GetParentProcess().Handle;
              SetParent(parentHandle, ref sInfoEx, ref lpValue);
            }
 

            Kernel32.CreateProcess(
                null,                 // No module name (use command line)
                exePath + " " + args, // command line
                IntPtr.Zero,          // Process handle not inheritable
                IntPtr.Zero,          // Thread handle not inheritable
                true,                // Set handle inheritance

                dwCreationFlags,     // creation flags
                IntPtr.Zero,         // Use parent's environment block
                cwd,                 // Use parent's starting directory 
                ref sInfoEx,         // Pointer to STARTUPINFO structure
                out pInfo            // Pointer to PROCESS_INFORMATION structure
            );


            if (sInfoEx.lpAttributeList != IntPtr.Zero)
            {
                Kernel32.DeleteProcThreadAttributeList(sInfoEx.lpAttributeList);
                Marshal.FreeHGlobal(sInfoEx.lpAttributeList);
            }

            if(lpValue != IntPtr.Zero) {
              Marshal.FreeHGlobal(lpValue);
            }

            if(detached) {
              Kernel32.CloseHandle(pInfo.hThread);
              Kernel32.CloseHandle(pInfo.hProcess);
              return;
            }

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

        private static bool SetParent(IntPtr parentHandle, ref STARTUPINFOEX sInfoEx, ref IntPtr lpValue){
            var lpSize = IntPtr.Zero;
            var success = Kernel32.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref lpSize);
            if (success || lpSize == IntPtr.Zero)
                return false;

            sInfoEx.lpAttributeList = Marshal.AllocHGlobal(lpSize);
            success = Kernel32.InitializeProcThreadAttributeList(sInfoEx.lpAttributeList, 1, 0, ref lpSize);
            if (!success)
                return false;

            // This value should persist until the attribute list is destroyed using the DeleteProcThreadAttributeList function
            lpValue = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(lpValue, parentHandle);


            success = Kernel32.UpdateProcThreadAttribute(
                sInfoEx.lpAttributeList,
                0,
                (IntPtr)Kernel32.PROC_THREAD_ATTRIBUTE_PARENT_PROCESS,
                lpValue,
                (IntPtr)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero);

            return success;
        }

        private static bool ExtractCommandLine() {
            string dispatcher = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
            string dispatcher_dir = Path.GetDirectoryName(dispatcher);
            cwd = null; //inherit
            as_service = false;
            as_desktop_user = false;
            use_job = true;
            uwf_servicing_disabled = false;

            restart_on_network_change = false;
            detached = false;

            args = String.Empty;
            use_showwindow = !DISPACHER_WIN;

            var config = ConfigurationParser.loadConfig();

            string dispatched_cmd = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]);

            string pathKey = "PATH";
            string exePathFlavor = Environment.GetEnvironmentVariable("DISPATCHER_" + dispatched_cmd + "_FLAVOR");

            if(!String.IsNullOrEmpty(exePathFlavor))
              pathKey = "PATH_" + exePathFlavor;;

            if (!config.ContainsKey(pathKey))
            {
                Console.Error.WriteLine("Cannot resolve cmd");
                return false;
            }
            if (config.ContainsKey(FLAG_USE_SHOWWINDOW))
                use_showwindow = toBool(config[FLAG_USE_SHOWWINDOW]);


            exePath = config[pathKey];

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

            List<string> keys = new List<string>(config.Keys);
            keys.Sort();

            foreach (string key in keys) {
                string value = config[key];
                value = Replace(value, replaces);
                value = Environment.ExpandEnvironmentVariables(value);
                if (key.StartsWith("ARGV"))
                    args += EncodeParameterArgument(value) + " ";
                if (key.StartsWith("ENV_"))
                    envs[key.Remove(0, 4)] = value;
                if (key == "PRESTART_CMD")
                    execPreCmd = value;
                if (key == "CWD")
                    cwd = value;
                if (key == "UWF_SERVICING_DISABLED")
                    uwf_servicing_disabled =  toBool(value);

                if(key == "UWF_SERVICING_DETECT" || key == "AS_SERVICE") {
                  if(UWFManagement.servicingEnabled())
                    envs["UWF_SERVICING_ENABLED"] =  "true";
                }

                if (key == "AS_SERVICE") {
                    as_service = value == "auto" ? ProcessExtensions.isService() : toBool(value);

                    if(value == "auto" && as_service)
                      envs["DISPATCHED_SERVICE_MODE"] = "true";

                } if (key == "SERVICE_RESTART_ON_NETWORK_CHANGE")
                    restart_on_network_change = true;
                if (key == "AS_DESKTOP_USER") {
                    as_desktop_user = value == "auto" ? ProcessExtensions.isService() : toBool(value);
                } if (key == "OUTPUT")
                    logsPath = value;
                if(key == "USE_JOB")
                    use_job = toBool(value);
                if(key == "DETACHED")
                    detached = toBool(value);
            }
            var argv = System.Environment.GetCommandLineArgs();

            for(var i = 1; i < argv.Length; i++) {
                string value = argv[i];
                args += EncodeParameterArgument(value) + " ";
            }

            return true;
        }
        public static string Replace(string str, Dictionary<string, string> dict) {
            foreach (KeyValuePair<string, string> replacement in dict)
                str = str.Replace(replacement.Key, replacement.Value);
            return str;
        }

        public static bool toBool(string str) {
          return !(String.IsNullOrEmpty(str) || str == "false" || str == "0");
        }
        


      public static string EncodeParameterArgument(string value)
      {
          if(String.IsNullOrEmpty(value))
              return "\"\"";

          if(Regex.IsMatch(value, "^[a-zA-Z0-9_./:^,=-]+$"))
            return value;

          value = Regex.Replace(value, @"(\\*)" + "\"", @"$1\$0");
          value = Regex.Replace(value, @"^(.*\s.*?)(\\*)$", "\"$1$2$2\"");
          return value;
      }






    }


    public class ConfigurationParser
    {
        public static Dictionary<string, string> loadConfig()
        {
            Dictionary<string, string> config = new Dictionary<string, string>();

            string dispatcher_path = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
            string dispatcher_dir = Path.GetDirectoryName(dispatcher_path);
            string dispatcher_name = Path.GetFileNameWithoutExtension(dispatcher_path);

            List<string> files = new List<string>();
            files.Add(Path.Combine(dispatcher_dir, dispatcher_name + ".config"));
            files.Add(Path.Combine(dispatcher_dir, dispatcher_name + ".exe.config"));

            string dispatcher_conf = Path.Combine(dispatcher_dir, dispatcher_name + ".config.d");
            if (Directory.Exists(dispatcher_conf))
            {
                string[] dirs = Directory.GetFiles(dispatcher_conf, "*.config");
                foreach (string file in dirs)
                    files.Add(Path.GetFullPath(file));
            }

            foreach (string config_file in files)
            {
                try
                {
                    if (!File.Exists(config_file))
                        continue;
                    XmlDocument doc = new XmlDocument();
                    doc.Load(config_file);
                    XmlNodeList nodeList;
                    XmlNode root = doc.DocumentElement;
                    nodeList = root.SelectNodes("/configuration/appSettings/add[@key][@value]");
                    //Change the price on the books.
                    foreach (XmlNode addd in nodeList)
                        config[addd.Attributes["key"].Value] = addd.Attributes["value"].Value;
                }
                catch { }
            }


            return config;
        }

    }

    public class Service1 : ServiceBase {

        protected Thread t;
        private static int delay = 1000;

        private static object block = new object();

        static void AddressChangedCallback(object sender, EventArgs e)
        {
            lock (block) 
            {
                Thread.Sleep(1500); //wait for network interface to be ready
                Monitor.PulseAll(block);
            }
            delay = 1000;
        }

        protected override void OnStart(string[] args)
        {
            bool shouldRun = ! (Program.uwf_servicing_disabled && UWFManagement.servicingEnabled());

            if (shouldRun)
                t = new Thread(() => Run());
            else
                t = new Thread(() => Noop());

            t.Start();

            if(Program.restart_on_network_change)
                NetworkChange.NetworkAddressChanged += new NetworkAddressChangedEventHandler(AddressChangedCallback);
        }

        protected void Run()
        {
            lock (block)
            {
                while (true)
                {
                    Program.Run();
                    Monitor.Wait(block, delay);
                    delay = delay * 2;
                }
            }
        }

        protected void Noop()
        {
            lock (block)
            {
                while (true)
                {
                    Monitor.Wait(block, delay);
                    delay = delay * 2;
                }
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


