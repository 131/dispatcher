using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Utils {
    internal static class EnvironmentProvider {
        const int DefaultTimeout = 5000;
        const int MaximumOutputChars = 16 * 1024 * 1024;

        internal static Dictionary<string, string> Run(string commandLine, int timeout) {
            if (String.IsNullOrWhiteSpace(commandLine)) throw new ArgumentException("ENV_PROVIDER must contain a command line.");
            if (timeout <= 0) timeout = DefaultTimeout;
            string executable, arguments;
            SplitCommandLine(commandLine, out executable, out arguments);

            ProcessStartInfo start = new ProcessStartInfo(executable, arguments);
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;

            using (Process process = new Process()) {
                process.StartInfo = start;
                StringBuilder output = new StringBuilder(), error = new StringBuilder();
                object outputLock = new object(), errorLock = new object();
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs) {
                    if (eventArgs.Data == null) return;
                    lock (outputLock) {
                        if (output.Length <= MaximumOutputChars) output.AppendLine(eventArgs.Data);
                    }
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs) {
                    if (eventArgs.Data == null) return;
                    lock (errorLock) {
                        if (error.Length <= MaximumOutputChars) error.AppendLine(eventArgs.Data);
                    }
                };

                try {
                    if (!process.Start()) throw new InvalidOperationException("ENV_PROVIDER could not be started.");
                    process.BeginOutputReadLine(); process.BeginErrorReadLine();
                    if (!process.WaitForExit(timeout)) {
                        try { process.Kill(); } catch { }
                        process.WaitForExit();
                        throw new TimeoutException("ENV_PROVIDER timed out after " + timeout + " ms.");
                    }
                    process.WaitForExit(); // flush asynchronous output handlers
                } catch (System.ComponentModel.Win32Exception failure) {
                    throw new InvalidOperationException("ENV_PROVIDER could not be started: " + failure.Message, failure);
                }

                string stdout, stderr;
                lock (outputLock) stdout = output.ToString();
                lock (errorLock) stderr = error.ToString();
                if (stdout.Length > MaximumOutputChars || stderr.Length > MaximumOutputChars)
                    throw new InvalidOperationException("ENV_PROVIDER output exceeded 16 MiB.");
                if (process.ExitCode != 0) {
                    string detail = SafeDiagnostic(stderr);
                    throw new InvalidOperationException("ENV_PROVIDER exited with code " + process.ExitCode + (detail.Length == 0 ? "." : ": " + detail));
                }

                try {
                    object parsed = new JavaScriptSerializer { MaxJsonLength = MaximumOutputChars }.DeserializeObject(stdout);
                    Dictionary<string, object> values = parsed as Dictionary<string, object>;
                    if (values == null) throw new InvalidDataException("the root value is not an object");
                    Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (KeyValuePair<string, object> item in values) {
                        if (String.IsNullOrEmpty(item.Key) || item.Key.IndexOf('=') >= 0 || item.Value == null || !(item.Value is string))
                            throw new InvalidDataException("every entry must have a non-empty variable name and a string value");
                        result[item.Key] = (string)item.Value;
                    }
                    return result;
                } catch (Exception failure) {
                    throw new InvalidOperationException("ENV_PROVIDER did not return a valid JSON object of string values.", failure);
                }
            }
        }


        static void SplitCommandLine(string commandLine, out string executable, out string arguments) {
            string value = commandLine.Trim();
            if (value[0] == '"') {
                int end = 1;
                while (end < value.Length && value[end] != '"') end++;
                if (end == value.Length) throw new ArgumentException("ENV_PROVIDER has an unterminated executable quote.");
                executable = value.Substring(1, end - 1);
                arguments = value.Substring(end + 1).TrimStart();
            } else {
                int end = value.IndexOfAny(new[] { ' ', '\t' });
                if (end < 0) { executable = value; arguments = ""; }
                else { executable = value.Substring(0, end); arguments = value.Substring(end + 1).TrimStart(); }
            }
            if (executable.Length == 0) throw new ArgumentException("ENV_PROVIDER must name an executable.");
        }

        // Provider stderr may contain credentials. Include only a short first line
        // that does not look like an assignment or a token; otherwise redact it.
        static string SafeDiagnostic(string value) {
            if (String.IsNullOrWhiteSpace(value)) return "";
            string line = value.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
            if (line.Length > 300 || line.IndexOf('=') >= 0 || line.IndexOf('{') >= 0 || line.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0)
                return "provider diagnostics redacted";
            return line;
        }
    }
}
