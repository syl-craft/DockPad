using System.Threading;
using System.Threading.Tasks;
using DockPad.StreamDock.Hid;

namespace DockPad.StreamDock;

/// <summary>
/// Pilote du MiraBox Stream Dock 293S V3, chargé par DockPad depuis plugins\StreamDock\.
/// </summary>
public sealed class StreamDockDriver : IControlSurfaceDriver
{
    internal const string DriverName = "StreamDock";

    private static readonly TimeSpan WriteTimeout = TimeSpan.FromMilliseconds(500);

    private readonly object _gate = new();
    private Action? _surfacesChanged;
    private HidArrivalWatcher? _watcher;

    public string Name => DriverName;

    /// <summary>
    /// L'abonnement Windows est pris au premier abonné et rendu au départ du dernier : le pilote n'a pas de Dispose.
    /// Levé sur le pool de threads, jamais dans le rappel Windows : un abonné peut s'y désabonner.
    /// </summary>
    public event Action? SurfacesChanged
    {
        add
        {
            lock (_gate)
            {
                _surfacesChanged += value;
                if (_watcher == null && _surfacesChanged != null)
                    _watcher = StartWatching();
            }
        }
        remove
        {
            HidArrivalWatcher? stopped = null;
            lock (_gate)
            {
                _surfacesChanged -= value;
                if (_surfacesChanged == null)
                {
                    stopped = _watcher;
                    _watcher = null;
                }
            }
            // Hors du verrou : le désabonnement Windows attend la fin d'un rappel en cours.
            stopped?.Dispose();
        }
    }

    public IReadOnlyList<SurfaceInfo> FindSurfaces() => HidDeviceFinder.Find();

    /// <summary>
    /// La surface rendue a déjà passé sa séquence de démarrage ; sinon le handle est fermé et l'erreur remonte.
    /// </summary>
    public async Task<IControlSurface> OpenAsync(SurfaceInfo surface, CancellationToken ct)
    {
        if (surface.DriverName != DriverName)
            throw new ArgumentException($"Surface belongs to driver '{surface.DriverName}', not '{DriverName}'.", nameof(surface));

        var connection = await Task.Run(() => HidConnection.Open(surface.DevicePath, WriteTimeout), ct);
        var dock = new StreamDockSurface(connection, TimeProvider.System);
        await dock.StartAsync(ct);
        return dock;
    }

    internal bool IsWatchingArrivals
    {
        get
        {
            lock (_gate)
                return _watcher != null;
        }
    }

    /// <summary>
    /// Null si Windows refuse l'abonnement : la reprise périodique de DockPad retrouve quand même le dock.
    /// </summary>
    private HidArrivalWatcher? StartWatching()
    {
        try
        {
            return new HidArrivalWatcher(RaiseSurfacesChanged);
        }
        catch (Exception ex)
        {
            DriverTrace.Warning("HID arrival notifications unavailable", ex);
            return null;
        }
    }

    private void RaiseSurfacesChanged()
    {
        Action? handlers;
        lock (_gate)
            handlers = _surfacesChanged;
        handlers?.Invoke();
    }
}
