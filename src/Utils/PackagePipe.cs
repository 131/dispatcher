using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Utils {
    // Keep the existing BinaryReader/BinaryWriter wire format, with asynchronous I/O.
    // Each operation has a deadline; a timed-out connection is never reused.
    internal sealed class PackagePipe : IDisposable {
        const int DefaultTimeout = 20000;
        const int MaxStringBytes = 16 * 1024 * 1024;
        readonly PipeStream stream;
        readonly int timeout;

        PackagePipe(PipeStream stream, int timeout) { this.stream = stream; this.timeout = timeout; }
        internal SafePipeHandle Handle { get { return stream.SafePipeHandle; } }

        internal static PackagePipe Server(string name, int timeout = DefaultTimeout) {
            PipeSecurity security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            // NamedPipeServerStream has no PIPE_REJECT_REMOTE_CLIENTS option in 4.8.
            // Deny network logons explicitly, even when they use the same user SID.
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                PipeAccessRights.FullControl, AccessControlType.Deny));
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                security.AddAccessRule(new PipeAccessRule(identity.User, PipeAccessRights.FullControl, AccessControlType.Allow));
            return new PackagePipe(new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536, security), timeout);
        }

        internal async Task AcceptAsync(Process bootstrap) {
            var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler onExit = delegate { exited.TrySetResult(true); };
            bootstrap.Exited += onExit;
            try {
                bootstrap.EnableRaisingEvents = true;
                if (bootstrap.HasExited) exited.TrySetResult(true);
                using (var deadline = new CancellationTokenSource(timeout)) {
                    Task connection = ((NamedPipeServerStream)stream).WaitForConnectionAsync(deadline.Token);
                    await Task.WhenAny(connection, exited.Task).ConfigureAwait(false);
                    if (!connection.IsCompleted) {
                        deadline.Cancel();
                        try { await connection.ConfigureAwait(false); } catch (OperationCanceledException) { }
                        throw new InvalidOperationException("The package helper exited before connecting (exit code " + bootstrap.ExitCode + ").");
                    }
                    try { await connection.ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw new TimeoutException("Timed out entering the package context."); }
                }
            } finally { bootstrap.Exited -= onExit; }
        }

        internal static async Task<PackagePipe> ClientAsync(string name, int timeout = DefaultTimeout) {
            var client = new NamedPipeClientStream(".", name, PipeDirection.InOut,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            try {
                await client.ConnectAsync(timeout).ConfigureAwait(false);
                return new PackagePipe(client, timeout);
            } catch { client.Dispose(); throw; }
        }

        internal async Task WriteAsync(Action<BinaryWriter> write) {
            using (var buffer = new MemoryStream()) {
                using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true)) write(writer);
                byte[] bytes = buffer.ToArray();
                await WithDeadlineAsync(async delegate(CancellationToken token) {
                    await stream.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
                }, "writing to").ConfigureAwait(false);
            }
        }

        async Task WithDeadlineAsync(Func<CancellationToken, Task> operation, string action) {
            using (var deadline = new CancellationTokenSource(timeout))
            // Framework PipeStream does not cancel every in-flight read/write through
            // the token alone. Closing it also aborts pending I/O on deadline expiry.
            using (deadline.Token.Register(Dispose)) {
                try { await operation(deadline.Token).ConfigureAwait(false); }
                catch (Exception error) {
                    if (deadline.IsCancellationRequested)
                        throw new TimeoutException("Timed out " + action + " the package helper.", error);
                    throw;
                }
            }
        }

        async Task<byte[]> ReadAsync(int count) {
            byte[] bytes = new byte[count];
            await WithDeadlineAsync(async delegate(CancellationToken token) {
                int offset = 0;
                while (offset < count) {
                    int read = await stream.ReadAsync(bytes, offset, count - offset, token).ConfigureAwait(false);
                    if (read == 0) throw new EndOfStreamException("The package helper disconnected.");
                    offset += read;
                }
            }, "reading from").ConfigureAwait(false);
            return bytes;
        }

        internal async Task<int> ReadInt32Async() { return BitConverter.ToInt32(await ReadAsync(4).ConfigureAwait(false), 0); }
        internal async Task<long> ReadInt64Async() { return BitConverter.ToInt64(await ReadAsync(8).ConfigureAwait(false), 0); }
        internal async Task<bool> ReadBooleanAsync() { return (await ReadAsync(1).ConfigureAwait(false))[0] != 0; }
        internal async Task<string> ReadStringAsync() {
            // BinaryWriter prefixes UTF-8 strings with a seven-bit encoded byte count.
            int length = 0;
            for (int shift = 0; shift < 35; shift += 7) {
                byte value = (await ReadAsync(1).ConfigureAwait(false))[0];
                if (shift == 28 && value > 7) throw new IOException("Invalid package helper string length.");
                length |= (value & 127) << shift;
                if ((value & 128) == 0) {
                    if (length > MaxStringBytes) throw new IOException("Package helper string is too large.");
                    return Encoding.UTF8.GetString(await ReadAsync(length).ConfigureAwait(false));
                }
            }
            throw new IOException("Invalid package helper string length.");
        }

        public void Dispose() { stream.Dispose(); }
    }
}
