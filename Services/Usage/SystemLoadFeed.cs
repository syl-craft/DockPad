using System.Threading;
using System.Threading.Tasks;
using DockPad.Services.StreamDock;

namespace DockPad.Services.Usage;

/// <summary>
/// Charge du PC de la fenêtre principale : une mesure au démarrage puis toutes les 5 s, hors du thread UI.
/// Après le retour de Stop, aucune mesure n'atteint le moniteur avant sa remise à zéro et rien n'est publié.
/// </summary>
public sealed class SystemLoadFeed(ISystemLoadMonitor monitor, TimeProvider time, SynchronizationContext? publishOn)
{
    public const int HistoryLength = 12;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private static readonly SystemLoad Unknown = new(null, null);

    private readonly object _gate = new();
    private ITimer? _timer;
    /// <summary>
    /// Incrémentée à chaque arrêt : une mesure ou un rappel de minuteur d'une génération dépassée ne fait rien.
    /// </summary>
    private int _generation;
    /// <summary>
    /// Vrai de la mise en file d'une mesure jusqu'à sa publication : un tic qui tombe entre les deux est sauté.
    /// </summary>
    private bool _isSampling;
    /// <summary>
    /// File des accès au moniteur : mesures et remises à zéro s'y exécutent une à une, dans l'ordre de demande.
    /// </summary>
    private Task _monitorQueue = Task.CompletedTask;
    private Type? _lastFailure;

    public SystemLoad Current { get; private set; } = Unknown;

    /// <summary>
    /// Les 12 dernières mesures, la plus ancienne en premier ; un processeur inconnu garde son point.
    /// </summary>
    public IReadOnlyList<SystemLoad> History { get; private set; } = [];

    /// <summary>
    /// Levé sous le verrou du flux, sur publishOn quand il est fourni : un gestionnaire ne doit pas attendre un autre thread.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Dernier accès au moniteur mis en file, publication comprise quand publishOn est absent.
    /// </summary>
    internal Task PendingSample
    {
        get { lock (_gate) return _monitorQueue; }
    }

    public void Start()
    {
        int generation;
        lock (_gate)
        {
            if (_timer != null)
                return;
            generation = _generation;
            _timer = time.CreateTimer(_ => QueueSample(generation), null, Interval, Interval);
        }
        QueueSample(generation);
    }

    /// <summary>
    /// La remise à zéro passe par la file : elle suit toute mesure déjà commencée et précède celles de la reprise.
    /// </summary>
    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            _generation++;
            _isSampling = false;
            Current = Unknown;
            History = [];
            _monitorQueue = _monitorQueue.ContinueWith(_ => monitor.Reset(), TaskScheduler.Default);
        }
    }

    private void QueueSample(int generation)
    {
        lock (_gate)
        {
            if (generation != _generation || _isSampling)
                return;
            _isSampling = true;
            _monitorQueue = _monitorQueue.ContinueWith(_ => Sample(generation), TaskScheduler.Default);
        }
    }

    private void Sample(int generation)
    {
        lock (_gate)
        {
            if (generation != _generation)
                return;
        }

        try
        {
            SystemLoad load;
            try
            {
                load = monitor.Sample();
                _lastFailure = null;
            }
            catch (Exception ex)
            {
                if (_lastFailure != ex.GetType())
                {
                    _lastFailure = ex.GetType();
                    LogService.Warn(ex, "Charge du PC : mesure indisponible, dernière valeur conservée");
                }
                return;
            }

            if (publishOn == null)
                Publish(generation, load);
            else
                publishOn.Post(_ => Publish(generation, load), null);
        }
        finally
        {
            lock (_gate)
            {
                if (generation == _generation)
                    _isSampling = false;
            }
        }
    }

    private void Publish(int generation, SystemLoad load)
    {
        lock (_gate)
        {
            if (generation != _generation)
                return;
            Current = load;
            History = [.. History.Skip(History.Count - (HistoryLength - 1)), load];
            Changed?.Invoke();
        }
    }
}
