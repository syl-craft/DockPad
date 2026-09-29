using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

internal static class RelayBenchmark
{
    public static async Task<int> Send(string name)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(stop.Token);
        await client.WriteAsync(Encoding.UTF8.GetBytes("https://example.test\n"), stop.Token);
        using var reader = new StreamReader(client);
        return await reader.ReadLineAsync(stop.Token) == "ok" ? 0 : 1;
    }
    public static async Task<int> Run(string root)
    {
        // Isolated pipe names: never send test URLs to the real resident DockPad.
        var measurements = new List<double>[] { [], [], [] };
        for (int round = 0; round < 25; round++)
        {
            for (int mode = 0; mode < 3; mode++)
            {
                var name = "DockPad_RelayBenchmark_" + Guid.NewGuid().ToString("N");
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                var connected = server.WaitForConnectionAsync(stop.Token);
                var executable = Path.Combine(root, mode == 2 ? "" : "current", "DockPad.UpdateProbe.exe");
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--relay"); start.ArgumentList.Add(name);
                if (mode is 0 or 2) start.ArgumentList.Add("--baseline");
                var watch = Stopwatch.StartNew();
                using var child = Process.Start(start)!;
                await connected;
                using var reader = new StreamReader(server, leaveOpen: true);
                if (await reader.ReadLineAsync(stop.Token) != "https://example.test") throw new IOException("Relay failed");
                watch.Stop();
                await server.WriteAsync(Encoding.UTF8.GetBytes("ok\n"), stop.Token);
                await child.WaitForExitAsync(stop.Token);
                if (round >= 5) measurements[mode].Add(watch.Elapsed.TotalMilliseconds);
            }
        }
        string[] labels = ["Direct runtime + pipe (baseline)", "Direct + Velopack startup + pipe (avoided on relay)", "Stable launcher + fast relay (final path)"];
        for (int i = 0; i < 3; i++)
        {
            var sorted = measurements[i].Order().ToArray();
            Console.WriteLine($"{labels[i]}: median={sorted[sorted.Length / 2]:F1}ms p95={sorted[(int)(sorted.Length * .95) - 1]:F1}ms (20 warmed runs)");
        }
        return 0;
    }
}
