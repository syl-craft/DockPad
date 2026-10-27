using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;

namespace DockPad.Services.StreamDock;

/// <summary>
/// Sources partagées avec la fenêtre et le bandeau Usage IA ; les actions sont transmises au thread UI.
/// </summary>
public sealed class DockSources : IDockSources
{
    private readonly QuickAccessWindow _window;

    public DockSources(QuickAccessWindow window)
    {
        _window = window;
    }

    public IReadOnlyList<ShortcutEntry> Entries() => _window.ShortcutEntries();
    public IReadOnlyList<PageConfig> Pages() => _window.ShortcutPages();

    public int CurrentShortcutPage() => _window.ShortcutPage.Page;

    public void Run(ShortcutEntry entry) =>
        _window.Dispatcher.BeginInvoke(() => _window.RunFromDock(entry));

    public void ShowShortcutPage(int page) => _window.ShowShortcutPageFromDock(page);

    public async Task<IReadOnlyList<AiUsage>> ReadUsagesAsync(CancellationToken ct) =>
        await UsageActionService.Service.RefreshAsync(UsageConfigService.Load(), ct).ConfigureAwait(false);

    public UsageConfig UsageConfig() => UsageConfigService.Load();
}
