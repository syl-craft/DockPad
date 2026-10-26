using System.Windows.Threading;

namespace DockPad.Services;

public enum PageChangeOrigin { DockPad, StreamDock }

/// <summary>
/// Page Raccourcis partagée avec le dock ; l'origine distingue les deux sens de navigation.
/// </summary>
public sealed class ShortcutPageState
{
    private readonly Dispatcher? _dispatcher;

    public ShortcutPageState(Dispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher;
    }

    public int Page { get; private set; }
    public event Action<int, PageChangeOrigin>? Changed;

    /// <summary>
    /// Thread UI uniquement ; vérifie l’accès si un Dispatcher a été fourni.
    /// </summary>
    public void Set(int page, PageChangeOrigin origin)
    {
        _dispatcher?.VerifyAccess();
        if (Page == page) return;

        Page = page;
        var subscribers = Changed;
        if (subscribers == null) return;
        foreach (Action<int, PageChangeOrigin> subscriber in subscribers.GetInvocationList())
        {
            try { subscriber(page, origin); }
            catch (Exception ex) { LogService.Warn(ex, "Échec d’un abonné au changement de page Raccourcis."); }
        }
    }
}
