using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Utils {
    // Native app-model APIs resolve the target; COM starts the package-context helper.
    // Configuration, environment transport and target creation stay in the dispatcher.
    internal static class PackagedApplication {
        const uint Suspended = 4, UnicodeEnvironment = 0x400;
        internal delegate Process Bootstrap(string pipeName);

        internal static void ValidateAppName(string name) {
            int separator = name == null ? -1 : name.IndexOf('!');
            if (separator < 1 || separator == name.Length - 1 || name.LastIndexOf('!') != separator)
                throw new ArgumentException("APP_NAME must be a PackageFamilyName!ApplicationId.");
        }

        // GetPackagesByPackageFamily returns packages registered for the current user.
        static string[] RegisteredPackages(string family) {
            uint count = 0, length = 0;
            int error = Native.GetPackagesByPackageFamily(family, ref count, IntPtr.Zero, ref length, IntPtr.Zero);
            if (error != 0 && error != 122) throw new Win32Exception(error, "Cannot enumerate APP_NAME packages.");
            if (count == 0) throw new InvalidOperationException("APP_NAME package is not installed for this Windows user.");
            IntPtr names = Marshal.AllocHGlobal(checked((int)count * IntPtr.Size));
            IntPtr buffer = Marshal.AllocHGlobal(checked((int)length * 2));
            try {
                error = Native.GetPackagesByPackageFamily(family, ref count, names, ref length, buffer);
                if (error != 0) throw new Win32Exception(error, "Cannot enumerate APP_NAME packages.");
                string[] result = new string[count];
                for (int i = 0; i < count; i++) result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(names, i * IntPtr.Size));
                return result;
            } finally { Marshal.FreeHGlobal(buffer); Marshal.FreeHGlobal(names); }
        }

        static string PackagePath(string fullName) {
            uint length = 0;
            int error = Native.GetPackagePathByFullName(fullName, ref length, null);
            if (error != 122) throw new Win32Exception(error, "Cannot resolve the APP_NAME installation path.");
            StringBuilder path = new StringBuilder(checked((int)length));
            error = Native.GetPackagePathByFullName(fullName, ref length, path);
            if (error != 0) throw new Win32Exception(error, "Cannot resolve the APP_NAME installation path.");
            return path.ToString();
        }

        static string Attribute(XmlElement element, string name) {
            foreach (XmlAttribute attribute in element.Attributes)
                if (String.Equals(attribute.LocalName, name, StringComparison.OrdinalIgnoreCase)) return attribute.Value;
            return "";
        }

        internal static string ResolveExecutable(string appName) {
            ValidateAppName(appName);
            int separator = appName.IndexOf('!');
            string family = appName.Substring(0, separator), appId = appName.Substring(separator + 1);
            string executable = null;
            Version selectedVersion = null;
            bool desktop = false;
            foreach (string fullName in RegisteredPackages(family)) {
                string directory = PackagePath(fullName);
                XmlDocument manifest = new XmlDocument(); manifest.XmlResolver = null;
                manifest.Load(Path.Combine(directory, "AppxManifest.xml"));
                XmlNamespaceManager namespaces = new XmlNamespaceManager(manifest.NameTable);
                namespaces.AddNamespace("p", manifest.DocumentElement.NamespaceURI);
                XmlElement identity = (XmlElement)manifest.SelectSingleNode("/p:Package/p:Identity", namespaces);
                Version version = new Version(identity.GetAttribute("Version"));
                foreach (XmlElement application in manifest.SelectNodes("/p:Package/p:Applications/p:Application", namespaces)) {
                    if (application.GetAttribute("Id") != appId || (selectedVersion != null && version.CompareTo(selectedVersion) <= 0)) continue;
                    string behavior = Attribute(application, "RuntimeBehavior");
                    desktop = application.GetAttribute("EntryPoint") == "Windows.FullTrustApplication" || behavior == "packagedClassicApp" || behavior == "win32App";
                    string relativePath = application.GetAttribute("Executable");
                    if (String.IsNullOrEmpty(relativePath)) continue;
                    executable = Path.GetFullPath(Path.Combine(directory, relativePath));
                    selectedVersion = version;
                }
            }
            if (executable == null) throw new InvalidOperationException("APP_NAME application is missing from the package manifest.");
            if (!desktop) throw new NotSupportedException("APP_NAME currently requires a desktop / FullTrust application.");
            if (!File.Exists(executable)) throw new FileNotFoundException("The APP_NAME executable is missing.", executable);
            return executable;
        }

        // This is the internal COM interface used by Windows' desktop-package launcher.
        // Keep its ABI isolated here; no PowerShell or Appx module is loaded at runtime.
        [ComImport, Guid("F158268A-D5A5-45CE-99CF-00D6C3F3FC0A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDesktopAppXActivator {
            [PreserveSig] int Activate([MarshalAs(UnmanagedType.LPWStr)] string appId,
                [MarshalAs(UnmanagedType.LPWStr)] string executable,
                [MarshalAs(UnmanagedType.LPWStr)] string arguments, out IntPtr processHandle);
            [PreserveSig] int ActivateWithOptions([MarshalAs(UnmanagedType.LPWStr)] string appId,
                [MarshalAs(UnmanagedType.LPWStr)] string executable,
                [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, uint parentProcessId, out IntPtr processHandle);
        }

        internal static Process StartInPackage(string appName, string executable, string arguments) {
            ValidateAppName(appName);
            IDesktopAppXActivator activator = null;
            IntPtr handle = IntPtr.Zero;
            try {
                activator = (IDesktopAppXActivator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("168EB462-775F-42AE-9111-D714B2306C2E")));
                // Match the Windows launcher's PreventBreakaway flags: nonpackaged
                // executable process tree, app-installer update check, Centennial process.
                const uint options = 4 | 16 | 32;
                int result = activator.ActivateWithOptions(appName, executable, arguments, options, 0, out handle);
                if (result < 0) Marshal.ThrowExceptionForHR(result);
                uint pid = Native.GetProcessId(handle);
                if (pid == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Native package activation did not return a process handle.");
                return Process.GetProcessById(checked((int)pid));
            } catch (COMException error) {
                throw new InvalidOperationException("Native APP_NAME activation failed (HRESULT 0x" + error.ErrorCode.ToString("X8") + "): " + error.Message, error);
            } finally {
                if (handle != IntPtr.Zero) Native.CloseHandle(handle);
                if (activator != null) Marshal.ReleaseComObject(activator);
            }
        }

        static Process BootstrapPackage(string appName, string pipeName) {
            string target = ResolveExecutable(appName);
            string dispatcher = Process.GetCurrentProcess().MainModule.FileName;
            string arguments = "--dispatcher-package-helper " + pipeName + " " + Dispatcher.Program.EncodeParameterArgument(target);
            return StartInPackage(appName, dispatcher, arguments);
        }

        internal static uint Run(string appName, string arguments, string cwd, bool hide, bool useJob, bool detached, string output) {
            ValidateAppName(appName);
            return RunCore(arguments, cwd, hide, useJob, detached, output,
                delegate(string pipe) { return BootstrapPackage(appName, pipe); });
        }

        // Separate transport/creation from the bootstrap so it can be exercised with
        // an ordinary probe executable, without installing a test Store package.
        internal static uint RunCore(string arguments, string cwd, bool hide, bool useJob, bool detached, string output, Bootstrap bootstrap) {
            return RunCoreAsync(arguments, cwd, hide, useJob, detached, output, bootstrap).GetAwaiter().GetResult();
        }

        static async Task<uint> RunCoreAsync(string arguments, string cwd, bool hide, bool useJob, bool detached, string output, Bootstrap bootstrap) {
            string directory = Path.GetFullPath(String.IsNullOrEmpty(cwd) ? Environment.CurrentDirectory : cwd);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("CWD does not exist: " + directory);
            string log = String.IsNullOrEmpty(output) ? "" : Path.GetFullPath(output);
            string pipeName = "dispatcher-app-" + Guid.NewGuid().ToString("N");
            Process launcher = null;
            Process helper = null;
            IntPtr target = IntPtr.Zero;
            Job job = null;
            using (PackagePipe pipe = PackagePipe.Server(pipeName)) {
                try {
                    launcher = bootstrap(pipeName);
                    await pipe.AcceptAsync(launcher).ConfigureAwait(false);
                    uint actualClient;
                    if (!Native.GetNamedPipeClientProcessId(pipe.Handle, out actualClient) || actualClient != (uint)launcher.Id)
                        throw new IOException("Unexpected package helper process.");
                    if (await pipe.ReadInt32Async().ConfigureAwait(false) != 1) throw new IOException("Invalid package helper protocol.");
                    int helperPid = await pipe.ReadInt32Async().ConfigureAwait(false);
                    if (actualClient != (uint)helperPid)
                        throw new IOException("Unexpected package helper process.");
                    helper = Process.GetProcessById(helperPid);
                    await pipe.ReadStringAsync().ConfigureAwait(false); // Resolved target path; instance policy belongs to the application.
                    if (useJob && !detached) {
                        // Assign the helper before it creates the target, so descendants
                        // inherit the job. Some Windows hosts impose their own job policy.
                        job = new Job();
                        if (!job.AddProcess(helper.Handle)) {
                            Console.Error.WriteLine("dispatcher: could not assign the package helper to a job (" + Marshal.GetLastWin32Error() + ").");
                            job.Dispose(); job = null;
                        }
                    }
                    await pipe.WriteAsync(writer => {
                        writer.Write(arguments ?? "");
                        writer.Write(directory);
                        writer.Write(hide);
                        writer.Write(detached);
                        writer.Write(log);
                        writer.Write(Process.GetCurrentProcess().Id);
                        for (int i = 0; i < 3; i++) {
                            IntPtr original = Native.GetStdHandle(-10 - i), copied = IntPtr.Zero;
                            // Console handles are obtained by attaching to the caller below.
                            if (!detached && original != IntPtr.Zero && original != new IntPtr(-1) && Native.GetFileType(original) != 2)
                                Native.DuplicateHandle(Native.GetCurrentProcess(), original, helper.Handle, out copied, 0, true, 2);
                            writer.Write(copied.ToInt64());
                        }
                        IDictionary env = Environment.GetEnvironmentVariables();
                        writer.Write(env.Count);
                        foreach (DictionaryEntry entry in env) {
                            writer.Write((string)entry.Key);
                            writer.Write((string)entry.Value);
                        }
                    }).ConfigureAwait(false);
                    if (!await pipe.ReadBooleanAsync().ConfigureAwait(false)) throw new InvalidOperationException(await pipe.ReadStringAsync().ConfigureAwait(false));
                    int targetPid = await pipe.ReadInt32Async().ConfigureAwait(false);
                    target = Native.OpenProcess(0x101000, false, targetPid); // synchronize + query limited information
                    if (target == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot monitor the packaged application.");
                    await pipe.WriteAsync(writer => writer.Write(true)).ConfigureAwait(false); // target stays suspended until the parent is ready
                    if (!await pipe.ReadBooleanAsync().ConfigureAwait(false)) throw new InvalidOperationException(await pipe.ReadStringAsync().ConfigureAwait(false));
                    if (detached) return 0;
                    await WaitForExitAsync(target).ConfigureAwait(false);
                    uint exit;
                    if (!Native.GetExitCodeProcess(target, out exit)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    return exit;
                } finally {
                    if (target != IntPtr.Zero) Native.CloseHandle(target);
                    if (job != null) job.Dispose();
                    if (helper != null) helper.Dispose();
                    if (launcher != null) launcher.Dispose();
                }
            }
        }

        internal static int Helper(string pipeName, string executable) {
            return HelperAsync(pipeName, executable).GetAwaiter().GetResult();
        }

        static async Task<int> HelperAsync(string pipeName, string executable) {
            using (PackagePipe pipe = await PackagePipe.ClientAsync(pipeName).ConfigureAwait(false)) {
                PROCESS_INFORMATION process = new PROCESS_INFORMATION();
                IntPtr block = IntPtr.Zero, log = IntPtr.Zero;
                IntPtr[] handles = new IntPtr[3];
                bool resumed = false;
                Exception failure = null;
                try {
                    await pipe.WriteAsync(writer => {
                        writer.Write(1);
                        writer.Write(Process.GetCurrentProcess().Id);
                        writer.Write(executable);
                    }).ConfigureAwait(false);
                    string arguments = await pipe.ReadStringAsync().ConfigureAwait(false);
                    string cwd = await pipe.ReadStringAsync().ConfigureAwait(false);
                    bool hide = await pipe.ReadBooleanAsync().ConfigureAwait(false), detached = await pipe.ReadBooleanAsync().ConfigureAwait(false);
                    string output = await pipe.ReadStringAsync().ConfigureAwait(false);
                    int parentPid = await pipe.ReadInt32Async().ConfigureAwait(false);
                    for (int i = 0; i < 3; i++) handles[i] = new IntPtr(await pipe.ReadInt64Async().ConfigureAwait(false));
                    int count = await pipe.ReadInt32Async().ConfigureAwait(false);
                    if (count < 0 || count > 32768) throw new IOException("Invalid environment block.");
                    SortedDictionary<string, string> env = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < count; i++) env[await pipe.ReadStringAsync().ConfigureAwait(false)] = await pipe.ReadStringAsync().ConfigureAwait(false);
                    StringBuilder environment = new StringBuilder();
                    foreach (KeyValuePair<string, string> entry in env) environment.Append(entry.Key).Append('=').Append(entry.Value).Append('\0');
                    environment.Append('\0');
                    if (count == 0) environment.Append('\0');
                    block = Marshal.StringToHGlobalUni(environment.ToString());
                    if (!detached) Native.AttachConsole((uint)parentPid);
                    for (int i = 0; i < 3; i++) if (!detached && handles[i] == IntPtr.Zero) handles[i] = Native.GetStdHandle(-10 - i);
                    STARTUPINFO startup = new STARTUPINFO();
                    startup.cb = Marshal.SizeOf(startup);
                    if (hide) { startup.dwFlags |= 1; startup.wShowWindow = 0; }
                    if (output.Length > 0) {
                        SECURITY_ATTRIBUTES attributes = new SECURITY_ATTRIBUTES();
                        attributes.nLength = Marshal.SizeOf(attributes); attributes.bInheritHandle = 1;
                        log = Native.CreateFile(output, 4, 3, ref attributes, 4, 0, IntPtr.Zero);
                        if (log == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open OUTPUT.");
                        handles[1] = log; handles[2] = log;
                    }
                    if (detached || handles[0] != IntPtr.Zero || handles[1] != IntPtr.Zero || handles[2] != IntPtr.Zero) {
                        startup.dwFlags |= 0x100;
                        startup.hStdInput = handles[0]; startup.hStdOutput = handles[1]; startup.hStdError = handles[2];
                    }
                    StringBuilder command = new StringBuilder("\"" + executable + "\" " + arguments);
                    uint flags = Suspended | UnicodeEnvironment | (detached ? 8u : 0u);
                    if (!Native.CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, !detached || output.Length > 0, flags, block, cwd, ref startup, out process))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot create the packaged application.");
                    await pipe.WriteAsync(writer => { writer.Write(true); writer.Write(process.dwProcessId); }).ConfigureAwait(false);
                    if (!await pipe.ReadBooleanAsync().ConfigureAwait(false)) throw new IOException("The dispatcher cancelled the launch.");
                    if (Native.ResumeThread(process.hThread) == UInt32.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
                    resumed = true;
                    await pipe.WriteAsync(writer => writer.Write(true)).ConfigureAwait(false);
                    return 0;
                } catch (Exception error) {
                    failure = error;
                } finally {
                    if (process.hProcess != IntPtr.Zero) {
                        if (!resumed) Native.TerminateProcess(process.hProcess, 1);
                        Native.CloseHandle(process.hProcess); Native.CloseHandle(process.hThread);
                    }
                    if (block != IntPtr.Zero) Marshal.FreeHGlobal(block);
                    if (log != IntPtr.Zero && log != new IntPtr(-1)) Native.CloseHandle(log);
                }
                try { await pipe.WriteAsync(writer => { writer.Write(false); writer.Write(failure.Message); }).ConfigureAwait(false); } catch { }
                return 1;
            }
        }

        static async Task WaitForExitAsync(IntPtr process) {
            using (var wait = new EventWaitHandle(false, EventResetMode.AutoReset)) {
                wait.SafeWaitHandle = new SafeWaitHandle(process, false);
                var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                RegisteredWaitHandle registration = ThreadPool.RegisterWaitForSingleObject(wait,
                    delegate { exited.TrySetResult(true); }, null, System.Threading.Timeout.Infinite, true);
                try { await exited.Task.ConfigureAwait(false); }
                finally { registration.Unregister(null); }
            }
        }

        static class Native {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern int GetPackagesByPackageFamily(string family, ref uint count, IntPtr names, ref uint length, IntPtr buffer);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern int GetPackagePathByFullName(string fullName, ref uint length, StringBuilder path);
            [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint GetProcessId(IntPtr process);
            [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern IntPtr CreateFile(string name, uint access, uint share, ref SECURITY_ATTRIBUTES attributes, uint disposition, uint flags, IntPtr template);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string cwd, ref STARTUPINFO startup, out PROCESS_INFORMATION process);
            [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
            [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint ResumeThread(IntPtr thread);
            [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool TerminateProcess(IntPtr process, uint exit);
            [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetExitCodeProcess(IntPtr process, out uint exit);
            [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool CloseHandle(IntPtr handle);
            [DllImport("kernel32.dll")] internal static extern IntPtr GetCurrentProcess();
            [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr source, IntPtr targetProcess, out IntPtr target, uint access, bool inherit, uint options);
            [DllImport("kernel32.dll")] internal static extern IntPtr GetStdHandle(int which);
            [DllImport("kernel32.dll")] internal static extern uint GetFileType(IntPtr handle);
            [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool AttachConsole(uint pid);
        }
    }
}
