using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using DockPad.Models;

namespace DockPad.Services;

internal sealed record FolderMenuEntry(string DisplayName, string Command, BitmapSource? Icon);

/// <summary>
/// Prépare les données du menu hors du thread d'interface ; les contrôles restent propres à chaque ouverture.
/// </summary>
internal sealed class FolderMenuCache
{
    private readonly object _gate = new();
    private readonly Func<IReadOnlyList<FolderMenuEntry>> _load;
    private IReadOnlyList<FolderMenuEntry>? _entries;
    private long _version = -1;
    private Task _refresh = Task.CompletedTask;

    internal FolderMenuCache(Func<IReadOnlyList<FolderMenuEntry>>? load = null) => _load = load ?? LoadEntries;

    internal IReadOnlyList<FolderMenuEntry>? ReadyEntries
    {
        get
        {
            lock (_gate) return _version == RegistryService.MenuVersion ? _entries : null;
        }
    }

    internal Task RefreshAsync()
    {
        lock (_gate)
        {
            if (!_refresh.IsCompleted) return _refresh;
            var version = RegistryService.MenuVersion;
            _refresh = Task.Run(() =>
            {
                try
                {
                    var entries = _load();
                    lock (_gate)
                    {
                        // Save ou Delete a pu finir pendant l'extraction d'une icône : ce relevé ne doit
                        // jamais rendre de nouveau utilisables les données qui viennent d'être invalidées.
                        if (version != RegistryService.MenuVersion) return;
                        _entries = entries;
                        _version = version;
                    }
                }
                catch (Exception ex) { LogService.Warn(ex, "Préparation du menu contextuel dossier"); }
            });
            return _refresh;
        }
    }

    internal static IReadOnlyList<FolderMenuEntry> LoadEntries() =>
        RegistryService.LoadForTarget(ContextMenuTarget.FolderBackground)
            .Where(entry => !string.IsNullOrEmpty(entry.Command))
            .Select(entry => new FolderMenuEntry(entry.DisplayName, entry.Command, IconStoreService.LoadImage(entry.IconPath)))
            .ToArray();
}
