using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;

namespace DockPad.Services.StreamDock;

/// <summary>
/// Lectures accessibles depuis tout thread ; Run assure le passage sur le thread UI.
/// </summary>
public interface IDockSources
{
    IReadOnlyList<ShortcutEntry> Entries();
    IReadOnlyList<PageConfig> Pages();
    void Run(ShortcutEntry entry);
    Task<IReadOnlyList<AiUsage>> ReadUsagesAsync(CancellationToken ct);
    UsageConfig UsageConfig();
}
