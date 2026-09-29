using System.Reflection;
using Velopack;

if (args.Length > 1 && args[0] == "--benchmark") return await RelayBenchmark.Run(args[1]);
var output = Environment.GetEnvironmentVariable("DOCKPAD_UPDATE_PROBE");
if (!args.Contains("--baseline"))
VelopackApp.Build().SetAutoApplyOnStartup(false).OnRestarted(_ =>
{
    File.Delete(Path.Combine(AppContext.BaseDirectory, "..", ".dockpad-update"));
}).Run();
if (args.Length > 1 && args[0] == "--relay") return await RelayBenchmark.Send(args[1]);
if (string.IsNullOrEmpty(output)) return 2;
Directory.CreateDirectory(output);
string Version() => Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
if (args.Contains("--mcp"))
{
    File.WriteAllText(Path.Combine(output, "holding"), Environment.ProcessId.ToString());
    Console.WriteLine("ready");
    Console.Out.Flush();
    Console.ReadLine();
    return 0;
}
if (args.Length > 1 && args[0] == "--update")
{
    var manager = new UpdateManager(args[1]);
    var release = await manager.CheckForUpdatesAsync();
    if (release is null) return 3;
    await manager.DownloadUpdatesAsync(release);
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "..", ".dockpad-update"), "");
    manager.WaitExitThenApplyUpdates(release.TargetFullRelease, silent: true, restart: true);
    return 0;
}
if (args.Length > 1 && args[0] == "--url")
    File.WriteAllText(Path.Combine(output, "url"), args[1]);
File.WriteAllText(Path.Combine(output, "version"), Version());
return 0;
