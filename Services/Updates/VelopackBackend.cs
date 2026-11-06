using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace DockPad.Services.Updates;

public sealed record VelopackBackendOptions(string GitHubUrl)
{
    public bool Prerelease { get; init; }
    public string[] RestartArgs { get; init; } = [];
    public string FallbackVersion { get; init; } = "";
    public bool ForcesShutdown { get; init; } = true;
}

public sealed class VelopackBackend : IUpdateBackend
{
    private readonly UpdateManager _manager;
    private readonly VelopackBackendOptions _options;
    public VelopackBackend(VelopackBackendOptions options)
        : this(new UpdateManager(new GithubSource(options.GitHubUrl, null, options.Prerelease)), options) { }
    public VelopackBackend(UpdateManager manager, VelopackBackendOptions options)
    {
        _manager = manager;
        _options = options;
    }
    public bool ForcesShutdown => _options.ForcesShutdown;
    public bool IsInstalled => _manager.IsInstalled;
    public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? _options.FallbackVersion;
    public string ContentDirectory => AppContext.BaseDirectory;
    public async Task<UpdateRelease?> CheckAsync()
    {
        var result = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return result is null ? null : new(result.TargetFullRelease.Version.ToString(),
            result.TargetFullRelease.NotesMarkdown ?? "", result, result.TargetFullRelease.Size);
    }
    public Task DownloadAsync(UpdateRelease release, Action<int> progress, CancellationToken token) =>
        _manager.DownloadUpdatesAsync((UpdateInfo)release.Package, progress, token);
    public void Apply(UpdateRelease release) => _manager.WaitExitThenApplyUpdates(
        ((UpdateInfo)release.Package).TargetFullRelease, silent: false, restart: true,
        restartArgs: _options.RestartArgs);
}

