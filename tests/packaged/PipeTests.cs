using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using Utils;

class PipeTests {
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static async Task Expect<T>(Task operation) where T : Exception {
        Require(await Task.WhenAny(operation, Task.Delay(5000)) == operation, "Operation did not finish within five seconds.");
        try { await operation; } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    static async Task Connected(Func<PackagePipe, NamedPipeClientStream, Task> test, int timeout = 1000) {
        string name = "dispatcher-pipe-test-" + Guid.NewGuid().ToString("N");
        using (PackagePipe server = PackagePipe.Server(name, timeout))
        using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        using (Process current = Process.GetCurrentProcess()) {
            Task accept = server.AcceptAsync(current);
            await client.ConnectAsync(1000);
            await accept;
            await test(server, client);
        }
    }

    static async Task Run() {
        using (Process current = Process.GetCurrentProcess())
        using (PackagePipe server = PackagePipe.Server("dispatcher-no-client-" + Guid.NewGuid().ToString("N"), 150))
            await Expect<TimeoutException>(server.AcceptAsync(current));
        await Expect<TimeoutException>(PackagePipe.ClientAsync("dispatcher-no-server-" + Guid.NewGuid().ToString("N"), 150));

        using (PackagePipe server = PackagePipe.Server("dispatcher-exited-" + Guid.NewGuid().ToString("N"))) {
            var start = new ProcessStartInfo("cmd.exe", "/c exit 23") { UseShellExecute = false, CreateNoWindow = true };
            using (Process child = Process.Start(start))
                await Expect<InvalidOperationException>(server.AcceptAsync(child));
        }
        Console.WriteLine("PASS pipe connection deadlines and helper exit before connecting");

        await Connected(async (server, client) => {
            await Expect<TimeoutException>(server.ReadInt32Async());
        }, 150);
        await Connected(async (server, client) => {
            await client.WriteAsync(new byte[] { 1, 2 }, 0, 2);
            await Expect<TimeoutException>(server.ReadInt32Async());
        }, 150);
        await Connected(async (server, client) => {
            await Expect<TimeoutException>(server.WriteAsync(writer => writer.Write(new byte[1024 * 1024])));
        }, 150);
        await Connected(async (server, client) => {
            await client.WriteAsync(new byte[] { 1, 2 }, 0, 2);
            client.Dispose();
            await Expect<EndOfStreamException>(server.ReadInt32Async());
        });
        Console.WriteLine("PASS stalled reads, partial reads, blocked writes and disconnected peers");

        await Connected(async (server, client) => {
            string expected = "quotes \" slash \\ newline\n é漢字 " + new string('z', 200);
            byte[] bytes;
            using (var memory = new MemoryStream()) {
                using (var writer = new BinaryWriter(memory, Encoding.UTF8, true)) {
                    writer.Write(123456789); writer.Write(long.MinValue); writer.Write(true);
                    writer.Write(""); writer.Write(expected);
                }
                bytes = memory.ToArray();
            }
            Task reading = ReadValues(server, expected);
            foreach (byte value in bytes) await client.WriteAsync(new byte[] { value }, 0, 1);
            await reading;
            Task writing = server.WriteAsync(writer => { writer.Write(expected); writer.Write(false); });
            using (var reader = new BinaryReader(client, Encoding.UTF8, true)) {
                Require(reader.ReadString() == expected, "BinaryReader cannot read the managed transport.");
                Require(!reader.ReadBoolean(), "Boolean reply mismatch.");
            }
            await writing;
        });
        await Connected(async (server, client) => {
            byte[] badLength = { 255, 255, 255, 255, 255 };
            await client.WriteAsync(badLength, 0, badLength.Length);
            await Expect<IOException>(server.ReadStringAsync());
        });
        Console.WriteLine("PASS fragmented BinaryReader/BinaryWriter protocol, Unicode and malformed strings");
    }

    static async Task ReadValues(PackagePipe server, string expected) {
        Require(await server.ReadInt32Async() == 123456789, "Int32 mismatch.");
        Require(await server.ReadInt64Async() == long.MinValue, "Int64 mismatch.");
        Require(await server.ReadBooleanAsync(), "Boolean mismatch.");
        Require(await server.ReadStringAsync() == "", "Empty string mismatch.");
        Require(await server.ReadStringAsync() == expected, "Fragmented string mismatch.");
    }

    static int Main() {
        try { Run().GetAwaiter().GetResult(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
