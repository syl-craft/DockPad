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
    public int ShortcutPage { get; set; }
    public ConcurrentQueue<int> ShownPages { get; } = new();
    public Action<int>? AfterShowPage { get; set; }
    public UsageConfig Config { get; set; } = new();
    public IReadOnlyList<AiUsage> Usages { get; set; } = [];
    public Func<CancellationToken, Task<IReadOnlyList<AiUsage>>>? ReadUsages { get; set; }
    public ConcurrentQueue<CancellationToken> UsageReads { get; } = new();
    public IReadOnlyList<ShortcutEntry> Entries() => EntriesFailure == null ? Tiles : throw EntriesFailure;
    public IReadOnlyList<PageConfig> Pages() => PagesFailure == null ? PageConfigs : throw PagesFailure;
    public Action? AfterRun { get; set; }
    public void Run(ShortcutEntry entry)
    {
        if (RunFailure != null) throw RunFailure;
        Runs.Enqueue(entry);
        AfterRun?.Invoke();
    }
    public int CurrentShortcutPage() => ShortcutPage;
    public void ShowShortcutPage(int page)
    {
        ShortcutPage = page;
        ShownPages.Enqueue(page);
        AfterShowPage?.Invoke(page);
    }
    public Task<IReadOnlyList<AiUsage>> ReadUsagesAsync(CancellationToken ct)
    {
        UsageReads.Enqueue(ct);
        return ReadUsages?.Invoke(ct) ?? Task.FromResult(Usages);
    }
    public UsageConfig UsageConfig() => Config;
}
