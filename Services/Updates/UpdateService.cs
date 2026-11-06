using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace DockPad.Services.Updates;

public enum UpdateState { Unavailable, Idle, Checking, Available, Downloading, Ready, Preparing, Installing, Failed }

public interface IUpdateBackend
{
    bool ForcesShutdown => true;
    bool IsInstalled { get; }
    string CurrentVersion { get; }
    string ContentDirectory { get; }
    Task<UpdateRelease?> CheckAsync();
    Task DownloadAsync(UpdateRelease release, Action<int> progress, CancellationToken token);
    void Apply(UpdateRelease release);
}

public sealed record UpdateRelease(string Version, string Notes, object Package, long? Size = null);

/// <summary>
/// Seul l'hôte résident démarre la recherche périodique.
/// </summary>
public sealed class UpdateService : INotifyPropertyChanged, IDisposable
{
    private readonly IUpdateBackend _backend;
    private readonly UpdateServiceOptions _options;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _download;
    private Task? _polling;
    private bool _checked;
    public event PropertyChangedEventHandler? PropertyChanged;
    public UpdateState State { get; private set; }
    public UpdateRelease? Release { get; private set; }
    public int Progress { get; private set; }
    public bool Busy => State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Preparing or UpdateState.Installing;
    public bool CanCheck => _backend.IsInstalled && !Busy;
    public bool CanInstall => Release is not null && !Busy;
    public bool IsDownloading => State == UpdateState.Downloading;
    /// <summary>
    /// Attente sans pourcentage : la préparation et la fermeture n'ont pas d'avancement mesurable.
    /// </summary>
    public bool IsIndeterminate => State is UpdateState.Preparing or UpdateState.Installing;
    public bool ShowsProgress => IsDownloading || IsIndeterminate;
    public bool HasRelease => Release is not null;
    public bool IsUnavailable => State == UpdateState.Unavailable;
    public string Notes => Release?.Notes ?? "";
    public string CurrentVersion => _backend.CurrentVersion;
    public string VersionIndicator => HasRelease
        ? UpdateTexts.F("Update_VersionTransition", CurrentVersion.TrimStart('v'), Release?.Version)
        : UpdateTexts.F("Update_VersionOnly", CurrentVersion.TrimStart('v'));
    public UpdatePresentation Presentation => UpdatePresentation.For(State, _checked);
    public string Heading => UpdateTexts.F(Presentation.TitleKey, Release?.Version);
    public string StateLabel => UpdateTexts.T(Presentation.LabelKey).ToUpper(UpdateTexts.Formatting);
    public string SecondaryLabel => UpdateTexts.T(Presentation.SecondaryKey);
    public string PrimaryLabel => UpdateTexts.T(Presentation.PrimaryKey);
    public bool SecondaryEnabled => Presentation.Secondary != UpdateAction.None;
    public bool PrimaryEnabled => Presentation.PrimaryEnabled;
    public bool PrimaryAccent => Presentation.PrimaryAccent;
    public string NewVersion => Release?.Version ?? "";
    public UpdateNotes ParsedNotes { get; private set; } = UpdateNotes.Parse("");
    public string ReleaseMetadata => string.Join(" · ", new[] {
        ParsedNotes.Date is { } date ? UpdateTexts.F("Update_Published", date.ToString("d", UpdateTexts.Formatting)) : null,
        Release?.Size is > 0 ? UpdateTexts.F("Update_Size", Release.Size.Value / 1_000_000d) : null,
    }.Where(value => value != null));
    public string DownloadSize => Release?.Size is > 0 && IsDownloading
        ? UpdateTexts.F("Update_DownloadSize", Release.Size.Value * (Progress / 100d) / 1_000_000d, Release.Size.Value / 1_000_000d) : "";
    public string LastChecked => FormatLastChecked(_options.Schedule.LastCheck, _options.Time.GetUtcNow());
    public static string FormatLastChecked(DateTimeOffset lastCheck, DateTimeOffset now)
    {
        if (lastCheck == default) return UpdateTexts.T("Update_NeverChecked");
        var age = now - lastCheck;
        if (age.TotalMinutes < 1) return UpdateTexts.T("Update_CheckedNow");
        if (age.TotalHours < 1) return UpdateTexts.F("Update_CheckedMinutes", (int)age.TotalMinutes);
        if (age.TotalDays < 1) return UpdateTexts.F("Update_CheckedHours", (int)age.TotalHours);
        return UpdateTexts.F("Update_CheckedDays", (int)age.TotalDays);
    }
    public string ContentDirectory => _backend.ContentDirectory;
    public bool ForcesShutdown => _backend.ForcesShutdown;
    public bool Automatic
    {
        get => _options.Schedule.Automatic;
        set { _options.Schedule.Automatic = value; Notify(); }
    }
    public string Status => State switch
    {
        UpdateState.Unavailable => UpdateTexts.T("Update_Unavailable"),
        UpdateState.Checking => UpdateTexts.T("Update_Checking"),
        UpdateState.Available => UpdateTexts.F("Update_Available", Release?.Version),
        UpdateState.Downloading => UpdateTexts.F("Update_Downloading", Progress),
        UpdateState.Ready => UpdateTexts.T("Update_ReadyHelp"),
        UpdateState.Preparing => UpdateTexts.T("Update_Preparing"),
        UpdateState.Installing => UpdateTexts.T("Update_Installing"),
        UpdateState.Failed => UpdateTexts.T("Update_Failed"),
        _ => UpdateTexts.T(_checked ? "Update_UpToDate" : "Update_NotChecked"),
    };
    public UpdateService(IUpdateBackend backend, UpdateServiceOptions? options = null)
    {
        _backend = backend;
        _options = options ?? new UpdateServiceOptions();
        State = backend.IsInstalled ? UpdateState.Idle : UpdateState.Unavailable;
        UpdateTexts.LanguageChanged += LanguageChanged;
    }
    private void LanguageChanged(object? sender, EventArgs e) => Notify();
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public async Task CheckAsync()
    {
        if (!_options.CanSearch() || !CanCheck || State == UpdateState.Ready) return;
        State = UpdateState.Checking; Notify();
        try
        {
            _options.Schedule.RecordCheck(_options.Time.GetUtcNow());
            Release = await Task.Run(_backend.CheckAsync);
            ParsedNotes = UpdateNotes.Parse(Release?.Notes ?? "");
            _checked = true;
            State = Release is null ? UpdateState.Idle : UpdateState.Available;
        }
        catch (Exception ex) { _options.Log.Warn(ex, "Update check failed"); State = UpdateState.Failed; }
        Notify();
    }
    public async Task<bool> DownloadAsync()
    {
        if (State == UpdateState.Ready) return true;
        var release = Release;
        if (!CanInstall || release == null) return false;
        using var download = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _download = download; State = UpdateState.Downloading; Progress = 0; Notify();
        var progress = new Progress<int>(p =>
        {
            if (State != UpdateState.Downloading || _download != download) return;
            Progress = Math.Clamp(p, 0, 100); Notify();
        });
        try
        {
            await Task.Run(() => _backend.DownloadAsync(release, ((IProgress<int>)progress).Report, download.Token));
            download.Token.ThrowIfCancellationRequested();
            Progress = 100;
            State = UpdateState.Ready;
        }
        catch (OperationCanceledException) { State = UpdateState.Available; }
        catch (Exception ex) { _options.Log.Warn(ex, "Update download failed"); State = UpdateState.Failed; }
        finally { _download = null; Notify(); }
        return State == UpdateState.Ready;
    }
    public void CancelDownload() => _download?.Cancel();

    /// <summary>
    /// Entre le téléchargement et la fermeture : écritures en attente, recherche des processus bloquants.
    /// </summary>
    public void BeginPreparation()
    {
        if (State != UpdateState.Ready) throw new InvalidOperationException("Update is not downloaded");
        State = UpdateState.Preparing; Notify();
    }

    /// <summary>
    /// Mise à jour abandonnée ou reportée : la version téléchargée reste prête à installer.
    /// </summary>
    public void CancelPreparation()
    {
        if (State != UpdateState.Preparing) return;
        State = UpdateState.Ready; Notify();
    }

    public void Apply()
    {
        if (State is not (UpdateState.Ready or UpdateState.Preparing) || Release is null)
            throw new InvalidOperationException("Update is not ready");
        try { _backend.Apply(Release); State = UpdateState.Installing; }
        catch { State = UpdateState.Ready; Notify(); throw; }
        Notify();
    }
    public void StartPolling() => _polling ??= PollAsync();
    private async Task PollAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), _options.Time, _lifetime.Token);
            while (!_lifetime.IsCancellationRequested)
            {
                if (_options.CanSearch() && Automatic && CanCheck)
                {
                    var now = _options.Time.GetUtcNow();
                    if (now - _options.Schedule.LastCheck >= TimeSpan.FromHours(6))
                    {
                        await CheckAsync();
                    }
                }
                await Task.Delay(TimeSpan.FromMinutes(15), _options.Time, _lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _options.Log.Warn(ex, "Update scheduler stopped"); }
    }
    public void Dispose()
    {
        _lifetime.Cancel(); CancelDownload();
        UpdateTexts.LanguageChanged -= LanguageChanged;
    }
}
