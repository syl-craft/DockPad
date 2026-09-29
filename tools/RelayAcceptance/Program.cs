using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

if (args.Length < 2) throw new ArgumentException("before-exe after-stable-exe [startup-hook-dll]");
using var mutex = new Mutex(true, "DockPad_NIN80_BenchMutex", out var created);
if (!created) throw new IOException("An acceptance test is already running");
var times = new List<double>[] { [], [] };
for (int round = 0; round < 25; round++)
{
    for (int mode = 0; mode < 2; mode++)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var server = new NamedPipeServerStream("DockPad_NIN80_BenchUrl", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var connected = server.WaitForConnectionAsync(stop.Token);
        var start = new ProcessStartInfo(args[mode]) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--url"); start.ArgumentList.Add("https://example.test/nin80");
        if (args.Length > 2) start.Environment["DOTNET_STARTUP_HOOKS"] = Path.GetFullPath(args[2]);
        var watch = Stopwatch.StartNew();
        using var child = Process.Start(start)!;
        await connected;
        using var reader = new StreamReader(server, leaveOpen: true);
        if (await reader.ReadLineAsync(stop.Token) != "https://example.test/nin80") throw new IOException("URL was changed");
        watch.Stop();
        if (mode == 1) await server.WriteAsync(Encoding.UTF8.GetBytes("ok\n"), stop.Token);
        await child.WaitForExitAsync(stop.Token);
        if (round >= 5) times[mode].Add(watch.Elapsed.TotalMilliseconds);
    }
}
for (int mode = 0; mode < 2; mode++)
{
    var sorted = times[mode].Order().ToArray();
    Console.WriteLine($"{(mode == 0 ? "DockPad before" : "DockPad packaged after")}: median={sorted[10]:F1}ms p95={sorted[18]:F1}ms; 20 warmed runs");
}
// The async console entry point may resume on another thread; disposal releases this fixture.
