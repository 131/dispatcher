﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Utils;
using System.Text;

namespace murrayju.ProcessExtensions
{
    public static class ProcessExtensions
    {
        #region Win32 Constants

        private const uint INVALID_SESSION_ID = 0xFFFFFFFF;
        private static readonly IntPtr WTS_CURRENT_SERVER_HANDLE = IntPtr.Zero;

        private const int LOGON32_LOGON_INTERACTIVE = 2;
        private const int LOGON32_PROVIDER_DEFAULT = 0;


        #endregion

        #region DllImports


      [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Auto)]
      public static extern bool LoadUserProfile(IntPtr hToken, ref PROFILEINFO lpProfileInfo);

      public struct PROFILEINFO
      {
          public int dwSize;
          public int dwFlags;
          public string lpUserName;
          public string lpProfilePath;
          public string lpDefaultPath;
          public string lpServerName;
          public string lpPolicyPath;
          public IntPtr hProfile;
      }


        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LogonUser(
            String lpszUsername, 
            String lpszDomain, 
            String lpszPassword,
            int dwLogonType, 
            int dwLogonProvider, 
            ref IntPtr phToken);

        [DllImport("advapi32.dll", EntryPoint = "CreateProcessAsUser", SetLastError = true, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        private static extern bool CreateProcessAsUser(
            IntPtr hToken,
            String lpApplicationName,
            String lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandle,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            String lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("advapi32.dll", EntryPoint = "DuplicateTokenEx")]
        private static extern bool DuplicateTokenEx(
            IntPtr ExistingTokenHandle,
            uint dwDesiredAccess,
            IntPtr lpThreadAttributes,
            int TokenType,
            int ImpersonationLevel,
            ref IntPtr DuplicateTokenHandle);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool CreateEnvironmentBlock(ref IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

        [DllImport("userenv.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hSnapshot);

        [DllImport("kernel32.dll")]
        private static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("Wtsapi32.dll")]
        private static extern uint WTSQueryUserToken(uint SessionId, ref IntPtr phToken);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern int WTSEnumerateSessions(
            IntPtr hServer,
            int Reserved,
            int Version,
            ref IntPtr ppSessionInfo,
            ref int pCount);

        #endregion

        #region Win32 Structs

        private enum SW
        {
            SW_HIDE = 0,
            SW_SHOWNORMAL = 1,
            SW_NORMAL = 1,
            SW_SHOWMINIMIZED = 2,
            SW_SHOWMAXIMIZED = 3,
            SW_MAXIMIZE = 3,
            SW_SHOWNOACTIVATE = 4,
            SW_SHOW = 5,
            SW_MINIMIZE = 6,
            SW_SHOWMINNOACTIVE = 7,
            SW_SHOWNA = 8,
            SW_RESTORE = 9,
            SW_SHOWDEFAULT = 10,
            SW_MAX = 10
        }

        private enum WTS_CONNECTSTATE_CLASS
        {
            WTSActive,
            WTSConnected,
            WTSConnectQuery,
            WTSShadow,
            WTSDisconnected,
            WTSIdle,
            WTSListen,
            WTSReset,
            WTSDown,
            WTSInit
        }


        private enum SECURITY_IMPERSONATION_LEVEL
        {
            SecurityAnonymous = 0,
            SecurityIdentification = 1,
            SecurityImpersonation = 2,
            SecurityDelegation = 3,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STARTUPINFO
        {
            public int cb;
            public String lpReserved;
            public String lpDesktop;
            public String lpTitle;
            public uint dwX;
            public uint dwY;
            public uint dwXSize;
            public uint dwYSize;
            public uint dwXCountChars;
            public uint dwYCountChars;
            public uint dwFillAttribute;
            public uint dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        private enum TOKEN_TYPE
        {
            TokenPrimary = 1,
            TokenImpersonation = 2
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WTS_SESSION_INFO
        {
            public readonly UInt32 SessionID;

            [MarshalAs(UnmanagedType.LPStr)]
            public readonly String pWinStationName;

            public readonly WTS_CONNECTSTATE_CLASS State;
        }

        #endregion




    public static string WhereSearch(string filename) {

            var paths = new List<string>();
            paths.Add(Environment.CurrentDirectory);

            foreach(string path in Environment.GetEnvironmentVariable("PATH").Split(';'))
                paths.Add(path);

            var extensions = new List<string>();
            foreach (string ext in Environment.GetEnvironmentVariable("PATHEXT").Split(';'))
                if(ext.StartsWith("."))
                    extensions.Add(ext);

            foreach(string path in paths)
            {
                foreach (string ext in extensions)
                {
                    var fullpath = Path.Combine(path, filename);
                    if (File.Exists(fullpath))
                        return fullpath;
                    fullpath = Path.Combine(path, filename + ext);
                    if (File.Exists(fullpath))
                        return fullpath;
                }
            }
            return filename;
    }



  static List<byte> ExtractMultiString(IntPtr ptr)
  {
      List<byte> foo = new List<byte>(0);
      while (true)
      {
          string str = Marshal.PtrToStringUni(ptr);
          if (str.Length == 0)
              break;
          foo.AddRange(Encoding.Unicode.GetBytes(str));
          foo.Add((byte)0);
          foo.Add((byte)0);
          ptr = new IntPtr(ptr.ToInt64() + (str.Length + 1 /* char \0 */) * sizeof(char));
      }
      return foo;

  }

        // Gets the user token from the currently active session
        private static bool GetSessionUserToken(ref IntPtr phUserToken)
        {
            var bResult = false;
            var hImpersonationToken = IntPtr.Zero;
            var activeSessionId = INVALID_SESSION_ID;
            var pSessionInfo = IntPtr.Zero;
            var sessionCount = 0;

            // Get a handle to the user access token for the current active session.
            if (WTSEnumerateSessions(WTS_CURRENT_SERVER_HANDLE, 0, 1, ref pSessionInfo, ref sessionCount) != 0)
            {
                var arrayElementSize = Marshal.SizeOf(typeof(WTS_SESSION_INFO));
                var current = (int) pSessionInfo;

                for (var i = 0; i < sessionCount; i++)
                {
                    var si = (WTS_SESSION_INFO)Marshal.PtrToStructure((IntPtr)current, typeof(WTS_SESSION_INFO));
                    current += arrayElementSize;

                    if (si.State == WTS_CONNECTSTATE_CLASS.WTSActive)
                    {
                        activeSessionId = si.SessionID;
                    }
                }
            }

            // If enumerating did not work, fall back to the old method
            if (activeSessionId == INVALID_SESSION_ID)
            {
                activeSessionId = WTSGetActiveConsoleSessionId();
            }

            if (WTSQueryUserToken(activeSessionId, ref hImpersonationToken) != 0)
            {
                // Convert the impersonation token to a primary token
                bResult = DuplicateTokenEx(hImpersonationToken, 0, IntPtr.Zero,
                    (int)SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation, (int)TOKEN_TYPE.TokenPrimary,
                    ref phUserToken);

                CloseHandle(hImpersonationToken);
            }

            return bResult;
        }


      public static bool isService()
      {
          string userName = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
          return userName == "S-1-5-18" || userName == "S-1-5-19" || userName == "S-1-5-20";
      }

      internal static PROCESS_INFORMATION StartProcessAsCurrentUser(string appPath, Dictionary<string, string> envs,
              string args = "", string workDir = null, bool visible = true, string logsPath = null) {

          var hUserToken = IntPtr.Zero;

          if (!GetSessionUserToken(ref hUserToken))
            throw new Exception("StartProcessAsCurrentUser: GetSessionUserToken failed.");

          try {
            return StartProcessAsUser(hUserToken, appPath, envs, args, workDir, visible, logsPath);
          } finally {
            CloseHandle(hUserToken);
          }
      }


    internal static PROCESS_INFORMATION StartProcessAsLogonUser(string username, string password, string appPath, Dictionary<string, string> envs,
            string args = "", string workDir = null, bool visible = true, string logsPath = null) {
        IntPtr userToken = IntPtr.Zero;

        // Attempt to log in user
        if (!LogonUser(username, ".", password, LOGON32_LOGON_INTERACTIVE, LOGON32_PROVIDER_DEFAULT, ref userToken))
          throw new Exception("Invalid logon auth");


        PROFILEINFO profileInfo = new PROFILEINFO {
              dwSize = Marshal.SizeOf(typeof(PROFILEINFO)),
              lpUserName = username,
              dwFlags = 1
        };

        bool loadSuccess = LoadUserProfile(userToken, ref profileInfo);
        if (!loadSuccess)
          throw new Exception("Could not load profile");



        try {
          return StartProcessAsUser(userToken, appPath, envs, args, workDir, visible, logsPath);
        } finally {
          CloseHandle(userToken);
        }
    }



      internal static PROCESS_INFORMATION StartProcessAsUser(IntPtr hUserToken, string appPath, Dictionary<string, string> envs,
            string args = "", string workDir = null, bool visible = true, string logsPath = null)
        {
            var startInfo = new STARTUPINFO();
            var procInfo = new PROCESS_INFORMATION();
            var pEnv = IntPtr.Zero;
            int iResultOfCreateProcessAsUser;
            appPath = WhereSearch(appPath);

            string cmdLine = "\"" + appPath + "\" " + args;

            startInfo.cb = Marshal.SizeOf(typeof(STARTUPINFO));
            IntPtr hLogs = IntPtr.Zero;


            try
            {
                uint dwCreationFlags = Kernel32.CREATE_UNICODE_ENVIRONMENT | (uint)(visible ? Kernel32.CREATE_NEW_CONSOLE : Kernel32.CREATE_NO_WINDOW);
                dwCreationFlags |= Kernel32.CREATE_BREAKAWAY_FROM_JOB;

                startInfo.wShowWindow = (short)(visible ? SW.SW_SHOW : SW.SW_HIDE);
                startInfo.lpDesktop = "winsta0\\default";
                startInfo.dwFlags = Kernel32.STARTF_USESTDHANDLES;

                if (!String.IsNullOrEmpty(logsPath)) {
                    SECURITY_ATTRIBUTES lpSecurityAttributes = new SECURITY_ATTRIBUTES();
                    lpSecurityAttributes.bInheritHandle = 1;
                    lpSecurityAttributes.nLength = Marshal.SizeOf(lpSecurityAttributes);
                    hLogs = Kernel32.CreateFile(logsPath, Kernel32.DesiredAccess.FILE_APPEND_DATA, 0x00000003 //share read& w
                    , ref lpSecurityAttributes, Kernel32.CreationDisposition.OPEN_ALWAYS, 0, IntPtr.Zero);

                    startInfo.hStdOutput = hLogs;
                    startInfo.hStdError = hLogs;
                }

                if (!CreateEnvironmentBlock(ref pEnv, hUserToken, false))
                {
                    throw new Exception("StartProcessAsCurrentUser: CreateEnvironmentBlock failed.");
                }

                List<byte> envSB = ExtractMultiString(pEnv);

                foreach (KeyValuePair<string, string> env in envs) {
                    envSB.AddRange(Encoding.Unicode.GetBytes(env.Key + "=" + env.Value));
                    envSB.Add((byte)0);
                    envSB.Add((byte)0);
                }
                envSB.Add((byte)0);
                envSB.Add((byte)0);

                byte[] foo = envSB.ToArray();

                IntPtr envdst = Marshal.AllocHGlobal(foo.Length);
                Marshal.Copy(envSB.ToArray(), 0, envdst, envSB.ToArray().Length);


                if (!CreateProcessAsUser(hUserToken,
                    null, // Application Name
                    cmdLine, // Command Line
                    IntPtr.Zero,
                    IntPtr.Zero,
                    true,
                    dwCreationFlags,
                    envdst,
                    workDir, // Working directory
                    ref startInfo,
                    out procInfo))
                {
                    iResultOfCreateProcessAsUser = Marshal.GetLastWin32Error();
                    throw new Exception("StartProcessAsCurrentUser: CreateProcessAsUser failed.  Error Code -" + iResultOfCreateProcessAsUser);
                }


                iResultOfCreateProcessAsUser = Marshal.GetLastWin32Error();
            } finally {
                if (pEnv != IntPtr.Zero)
                {
                    DestroyEnvironmentBlock(pEnv);
                }
            }

            return procInfo;
        }

    }
}
