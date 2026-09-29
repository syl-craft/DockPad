using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace DockPad.Services.Updates;

public enum UpdateState { Unavailable, Idle, Checking, Available, Downloading, Ready, Installing, Failed }

public interface IUpdateBackend
{
    bool IsInstalled { get; }
    string CurrentVersion { get; }
    string ContentDirectory { get; }
    Task<UpdateRelease?> CheckAsync();
    Task DownloadAsync(UpdateRelease release, Action<int> progress, CancellationToken token);
    void Apply(UpdateRelease release);
}

public sealed record UpdateRelease(string Version, string Notes, object Package);

public sealed class VelopackBackend : IUpdateBackend
{
    private readonly UpdateManager _manager;
    public VelopackBackend() : this(new UpdateManager(new GithubSource("https://github.com/syl-craft/DockPad", null, false))) { }
    public VelopackBackend(UpdateManager manager) => _manager = manager;
    public bool IsInstalled => _manager.IsInstalled;
    public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? AppInfo.VersionText;
    public string ContentDirectory => AppContext.BaseDirectory;
    public async Task<UpdateRelease?> CheckAsync()
    {
        var result = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return result is null ? null : new(result.TargetFullRelease.Version.ToString(),
            result.TargetFullRelease.NotesMarkdown ?? "", result);
    }
    public Task DownloadAsync(UpdateRelease release, Action<int> progress, CancellationToken token) =>
        _manager.DownloadUpdatesAsync((UpdateInfo)release.Package, progress, token);
    public void Apply(UpdateRelease release) => _manager.WaitExitThenApplyUpdates(
        ((UpdateInfo)release.Package).TargetFullRelease, silent: false, restart: true,
        restartArgs: ["--update-restarted"]);
}

/// <summary>Only the resident app starts polling. UI and tests share this state machine.</summary>
public sealed class UpdateService : INotifyPropertyChanged, IDisposable
{
    private static UpdateService? _current;
    public static UpdateService Current => _current ??= new(new VelopackBackend());
    private readonly IUpdateBackend _backend;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _download;
    private Task? _polling;
    private bool _checked;
    public event PropertyChangedEventHandler? PropertyChanged;
    public UpdateState State { get; private set; }
    public UpdateRelease? Release { get; private set; }
    public int Progress { get; private set; }
    public bool Busy => State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing;
    public bool CanCheck => _backend.IsInstalled && !Busy;
    public bool CanInstall => Release is not null && !Busy;
    public bool IsDownloading => State == UpdateState.Downloading;
    public bool HasRelease => Release is not null;
    public bool IsUnavailable => State == UpdateState.Unavailable;
    public string Notes => Release?.Notes ?? "";
    public string CurrentVersion => _backend.CurrentVersion;
    public string ContentDirectory => _backend.ContentDirectory;
    public string Status => State switch
    {
        UpdateState.Unavailable => Loc.T("Update_Unavailable"),
        UpdateState.Checking => Loc.T("Update_Checking"),
        UpdateState.Available => Loc.F("Update_Available", Release!.Version),
        UpdateState.Downloading => Loc.F("Update_Downloading", Progress),
        UpdateState.Ready => Loc.T("Update_Ready"),
        UpdateState.Installing => Loc.T("Update_Installing"),
        UpdateState.Failed => Loc.T("Update_Failed"),
        _ => Loc.T(_checked ? "Update_UpToDate" : "Update_NotChecked"),
    };
    public UpdateService(IUpdateBackend backend)
    {
        _backend = backend;
        State = backend.IsInstalled ? UpdateState.Idle : UpdateState.Unavailable;
        Loc.LanguageChanged += LanguageChanged;
    }
    private void LanguageChanged(object? sender, EventArgs e) => Notify();
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public async Task CheckAsync()
    {
        if (!CanCheck || State == UpdateState.Ready) return;
        State = UpdateState.Checking; Notify();
        try
        {
            Release = await Task.Run(_backend.CheckAsync);
            _checked = true;
            State = Release is null ? UpdateState.Idle : UpdateState.Available;
        }
        catch (Exception ex) { LogService.Warn(ex, "Update check failed"); State = UpdateState.Failed; }
        Notify();
    }
    public async Task<bool> DownloadAsync()
    {
        if (State == UpdateState.Ready) return true;
        if (!CanInstall) return false;
        using var download = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _download = download; State = UpdateState.Downloading; Progress = 0; Notify();
        var progress = new Progress<int>(p => { Progress = Math.Clamp(p, 0, 100); Notify(); });
        try
        {
            await Task.Run(() => _backend.DownloadAsync(Release!, ((IProgress<int>)progress).Report, download.Token));
            State = UpdateState.Ready;
        }
        catch (OperationCanceledException) { State = UpdateState.Available; }
        catch (Exception ex) { LogService.Warn(ex, "Update download failed"); State = UpdateState.Failed; }
        finally { _download = null; Notify(); }
        return State == UpdateState.Ready;
    }
    public void CancelDownload() => _download?.Cancel();
    public void Apply()
    {
        if (State != UpdateState.Ready || Release is null) throw new InvalidOperationException("Update is not ready");
        try { _backend.Apply(Release); State = UpdateState.Installing; }
        catch { State = UpdateState.Ready; throw; }
        Notify();
    }
    public void StartPolling() => _polling ??= PollAsync();
    private async Task PollAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), _lifetime.Token);
            while (!_lifetime.IsCancellationRequested)
            {
                if (AppSettingsService.Current.CheckForUpdates && CanCheck)
                {
                    var now = DateTimeOffset.UtcNow;
                    if (now - AppSettingsService.Current.LastUpdateCheck >= TimeSpan.FromHours(6))
                    {
                        AppSettingsService.Update(s => s.LastUpdateCheck = now);
                        await CheckAsync();
                    }
                }
                await Task.Delay(TimeSpan.FromMinutes(15), _lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LogService.Warn(ex, "Update scheduler stopped"); }
    }
    public void Dispose()
    {
        _lifetime.Cancel(); CancelDownload();
        Loc.LanguageChanged -= LanguageChanged;
    }
    public static void Stop() => _current?.Dispose();
}
