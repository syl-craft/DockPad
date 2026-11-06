using Mue.Velopack;
using Mue;

namespace DockPad.Services.UpdateHost;

public static class DockPadUpdates
{
    private static UpdateService? _service;
    public static UpdateService Service => _service ??= new(new VelopackBackend(BackendOptions), ServiceOptions);
    public static IUpdateDialogHost DialogHost { get; } = new DockPadUpdateDialogHost();
    public static VelopackBackendOptions BackendOptions => new("https://github.com/syl-craft/DockPad")
    {
        Prerelease = false,
        RestartArgs = ["--update-restarted"],
        FallbackVersion = AppInfo.VersionText,
        ForcesShutdown = false,
    };
    public static UpdateServiceOptions ServiceOptions => new()
    {
        Log = new DockPadUpdateLog(),
        Schedule = new DockPadUpdateSchedule(),
    };
    public static Mue.Wpf.UpdatesDialog CreateDialog(UpdateService? updates = null) => new(updates ?? Service, DialogHost);
    public static void Stop() => _service?.Dispose();
}

public sealed class DockPadUpdateLog : IUpdateLog
{
    public void Warn(Exception ex, string context) => LogService.Warn(ex, context);
}

public sealed class DockPadUpdateSchedule : IUpdateSchedule
{
    public bool Automatic
    {
        get => AppSettingsService.Current.CheckForUpdates;
        set => AppSettingsService.Update(s => s.CheckForUpdates = value);
    }
    public DateTimeOffset LastCheck => AppSettingsService.Current.LastUpdateCheck;
    public void RecordCheck(DateTimeOffset at) => AppSettingsService.Update(s => s.LastUpdateCheck = at);
}
