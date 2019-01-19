using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Configuration;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Utils;

namespace Dispatcher {
    class Program {
        
        const string FLAG_USE_SHOWWINDOW = "USE_SHOWWINDOW";

        static void Main() {
            string exePath, args, cwd;
            bool use_showwindow;

            Dictionary<string, string> envs = new Dictionary<string, string>();
            if(!ExtractCommandLine(out exePath, out args, out use_showwindow, envs, out cwd))
                Environment.Exit(1);

            string exeDir = Path.GetDirectoryName(exePath);
            envs["Path"] = Environment.GetEnvironmentVariable("PATH") + ";" + exeDir;
            foreach(KeyValuePair<string, string> env in envs)
                Environment.SetEnvironmentVariable(env.Key, env.Value);


            var pInfo = new PROCESS_INFORMATION();
            var sInfoEx = new STARTUPINFOEX();
            sInfoEx.StartupInfo = new STARTUPINFO();

            sInfoEx.StartupInfo.dwFlags = Kernel32.STARTF_USESTDHANDLES;

            if(use_showwindow)
                sInfoEx.StartupInfo.dwFlags |= Kernel32.STARTF_USESHOWWINDOW;

            IntPtr iStdOut = Kernel32.GetStdHandle(Kernel32.STD_OUTPUT_HANDLE);
            IntPtr iStdErr = Kernel32.GetStdHandle(Kernel32.STD_ERROR_HANDLE);
            IntPtr iStdIn = Kernel32.GetStdHandle(Kernel32.STD_INPUT_HANDLE);

            sInfoEx.StartupInfo.wShowWindow = Kernel32.SW_HIDE;
            sInfoEx.StartupInfo.hStdInput = iStdIn;
            sInfoEx.StartupInfo.hStdOutput = iStdOut;
            sInfoEx.StartupInfo.hStdError = iStdErr;

            //make sure dispatcher kill its child process when killed
            var job = new Job();
            job.AddProcess(Process.GetCurrentProcess().Handle);

            Kernel32.CreateProcess(
                null, exePath + " " + args, IntPtr.Zero, IntPtr.Zero, true,
                Kernel32.STARTF_USESTDHANDLES,
                IntPtr.Zero, cwd, ref sInfoEx, out pInfo);

            Kernel32.CloseHandle(pInfo.hThread);

            Kernel32.WaitForSingleObject(pInfo.hProcess, Kernel32.INFINITE);

            uint exitCode;
            Kernel32.GetExitCodeProcess(pInfo.hProcess, out exitCode);

            //clean up
            Kernel32.CloseHandle(pInfo.hProcess);
            Kernel32.CloseHandle(iStdOut);
            Kernel32.CloseHandle(iStdErr);
            Kernel32.CloseHandle(iStdIn);

            Environment.Exit((int)exitCode);
        }

        private static bool ExtractCommandLine(out string exePath, out string args, out bool use_showwindow, Dictionary<string, string> envs, out string cwd) {
            string dispatcher = Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName);
            string dispatcher_dir = Path.GetDirectoryName(dispatcher);
            cwd = null; //inherit

            //ConfigurationManager.AppSettings do not expand exe short name (e.g. search for a missing C:\windows\system32\somelo~1.config file)

            KeyValueConfigurationCollection config = new KeyValueConfigurationCollection();

            if(ConfigurationManager.AppSettings["PATH"] != null) {
                foreach(string key in ConfigurationManager.AppSettings)
                    config.Add(key,  ConfigurationManager.AppSettings[key]);
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

            if(config[FLAG_USE_SHOWWINDOW] != null)
                use_showwindow = !(config[FLAG_USE_SHOWWINDOW].Value == "0" || config[FLAG_USE_SHOWWINDOW].Value == "");
            
            if(String.IsNullOrEmpty(exePath)) {
                Console.Error.WriteLine("Cannot resolve cmd");
                return false;
            }

            string dispatched_cmd = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]);

            string exefoo = Path.GetFullPath(Path.Combine(dispatcher_dir, exePath));
            if(File.Exists(exefoo))
              exePath = exefoo; //let windows resolve it

            Dictionary<string, string> replaces = new Dictionary<string, string> {
                {"%dwd%", dispatcher_dir},
            };


            foreach(string key in config.AllKeys) {
                string value = config[key].Value;
                value = Replace(value, replaces);
                if(key.StartsWith("ARGV"))
                    args += (Regex.IsMatch(value, "^[a-zA-Z0-9_./:^,-]+$") ? value : "\"" + value + "\"") + " ";
                if(key.StartsWith("ENV_"))
                    envs[key.Remove(0, 4)] = value;
                if(key == "CWD")
                  cwd = value;
            }

            var argsStart = Environment.CommandLine.IndexOf(" ", dispatched_cmd.Length);
            if(argsStart != -1)
                args += Environment.CommandLine.Substring(argsStart + 1);

            return true;
        }
        public static string Replace(string str, Dictionary<string, string> dict) {
            foreach(KeyValuePair<string, string> replacement in dict)
                str = str.Replace(replacement.Key, replacement.Value);
            return str;
        }

    }

}
