namespace DockPad.Services;

/// <summary>
/// Modifications des tuiles, des pages ou de leur langue, indépendantes du mode affiché.
/// </summary>
public static class TileEvents
{
    public static event Action? GridChanged;

    public static void RaiseGridChanged()
    {
        var subscribers = GridChanged;
        if (subscribers == null) return;
        foreach (Action subscriber in subscribers.GetInvocationList())
        {
            try { subscriber(); }
            catch (Exception ex) { LogService.Warn(ex, "Échec d’un abonné au changement de grille."); }
        }
    }
}
