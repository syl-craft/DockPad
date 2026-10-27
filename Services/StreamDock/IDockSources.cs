using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;

namespace DockPad.Services.StreamDock;

/// <summary>
/// Lectures accessibles depuis tout thread ; Run et ShowShortcutPage assurent le passage sur le thread UI.
/// </summary>
public interface IDockSources
{
    IReadOnlyList<ShortcutEntry> Entries();
    IReadOnlyList<PageConfig> Pages();
    void Run(ShortcutEntry entry);
    int CurrentShortcutPage();
    void ShowShortcutPage(int page);
    Task<IReadOnlyList<AiUsage>> ReadUsagesAsync(CancellationToken ct);
    UsageConfig UsageConfig();
}
