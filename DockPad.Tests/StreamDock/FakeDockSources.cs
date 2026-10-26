using System.Collections.Concurrent;
using DockPad.Models;
using DockPad.Services.StreamDock;

namespace DockPad.Tests;

internal sealed class FakeDockSources : IDockSources
{
    public IReadOnlyList<ShortcutEntry> Tiles { get; set; } = [];
    public IReadOnlyList<PageConfig> PageConfigs { get; set; } = [];
    public ConcurrentQueue<ShortcutEntry> Runs { get; } = new();
    public Exception? EntriesFailure { get; set; }
    public Exception? PagesFailure { get; set; }
    public Exception? RunFailure { get; set; }
    public IReadOnlyList<ShortcutEntry> Entries() => EntriesFailure == null ? Tiles : throw EntriesFailure;
    public IReadOnlyList<PageConfig> Pages() => PagesFailure == null ? PageConfigs : throw PagesFailure;
    public Action? AfterRun { get; set; }
    public void Run(ShortcutEntry entry)
    {
        if (RunFailure != null) throw RunFailure;
        Runs.Enqueue(entry);
        AfterRun?.Invoke();
    }
    public Task<IReadOnlyList<AiUsage>> ReadUsagesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<AiUsage>>([]);
    public UsageConfig UsageConfig() => new();
}
