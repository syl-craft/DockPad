using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DockPad.StreamDock.Hid;

namespace DockPad.StreamDock;

/// <summary>
/// Surface du 293S V3 sur un handle HID : un fil d'écriture unique, une boucle de lecture et un maintien en vie,
/// démarrés par StartAsync. Une commande (effacement, validation, veille) rend la main une fois écrite ; une image,
/// une fois mise en file. Après la déconnexion les envois lèvent IOException, après la fermeture ObjectDisposedException.
/// </summary>
internal sealed class StreamDockSurface : IControlSurface
{
    public const string ModelName = "293S V3";
    private const int KeyImageSize = 96, DisplayImageSize = 80;
    private const byte AllKeys = 0xFF;
    private const int StartupBrightness = 100;
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(1);

    private readonly IHidConnection _connection;
    private readonly TimeProvider _time;
    private readonly object _gate = new();

    /// <summary>
    /// Écritures pas encore commencées ; celle en cours est dans _currentWrite et part toujours d'un bloc.
    /// </summary>
    private readonly LinkedList<PendingWrite> _queue = new();

    private readonly SemaphoreSlim _queueSignal = new(0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Dictionary<byte, CancellationTokenSource> _encodingByTarget = [];
    private readonly List<Action<Exception>> _disconnectedHandlers = [];
    private readonly List<Task> _notifications = [];
    private readonly Lazy<Task> _shutdown;
    private PendingWrite? _currentWrite;
    private bool _isQueueClosed;
    private Exception? _disconnectionCause;
    private volatile bool _isDisposed;
    private int _isConnectionClosed;
    private ITimer? _keepAlive;
    private Task? _writeLoop;
    private Task? _readLoop;

    public StreamDockSurface(IHidConnection connection, TimeProvider time)
    {
        _connection = connection;
        _time = time;
        _shutdown = new Lazy<Task>(ShutdownAsync);
    }

    public SurfaceDescription Description { get; } = new(ModelName, StreamDockModel.KeyRows, StreamDockModel.KeyColumns,
        KeyImageSize, StreamDockModel.DisplayCount, DisplayImageSize);

    public event Action<KeyEvent>? KeyChanged;

    /// <summary>
    /// Un abonné arrivé après la déconnexion la reçoit aussitôt : chaque abonné l'apprend une fois.
    /// </summary>
    public event Action<Exception>? Disconnected
    {
        add
        {
            if (value == null)
                return;
            Exception? cause;
            lock (_gate)
            {
                _disconnectedHandlers.Add(value);
                cause = _isDisposed ? null : _disconnectionCause;
                if (cause != null)
                    _notifications.Add(Task.Run(() => RaiseDisconnected([value], cause)));
            }
        }
        remove
        {
            if (value == null)
                return;
            lock (_gate)
                _disconnectedHandlers.Remove(value);
        }
    }

    /// <summary>
    /// Envoie la séquence de démarrage (réveil, luminosité, effacement, validation) puis lance la lecture et le
    /// maintien en vie. En cas d'échec, le handle est fermé, la surface est inutilisable et l'exception remonte.
    /// </summary>
    public async Task StartAsync(CancellationToken ct)
    {
        byte[][] startup = [StreamDockProtocol.Wake(), StreamDockProtocol.Brightness(StartupBrightness),
            StreamDockProtocol.Clear(AllKeys), StreamDockProtocol.Commit()];
        try
        {
            foreach (var report in startup)
            {
                await _connection.WriteAsync(report, ct);
            }
        }
        catch (Exception)
        {
            await DisposeAsync();
            throw;
        }

        lock (_gate)
        {
            if (_isDisposed)
                return;
            _keepAlive = _time.CreateTimer(_ => QueueKeepAlive(), state: null, KeepAliveInterval, KeepAliveInterval);
            _writeLoop = Task.Run(WriteLoopAsync);
            _readLoop = Task.Run(ReadLoopAsync);
        }
    }

    public async Task SetKeyImageAsync(SurfaceKey key, SurfaceImage image, CancellationToken ct)
    {
        var keyId = StreamDockModel.KeyId(key);
        await SendImageAsync(keyId, image, KeyImageSize, ct);
    }

    public async Task SetDisplayImageAsync(int display, SurfaceImage image, CancellationToken ct)
    {
        var displayId = StreamDockModel.DisplayId(display);
        await SendImageAsync(displayId, image, DisplayImageSize, ct);
    }

    public Task ClearAsync(CancellationToken ct) => SendCommandAsync(StreamDockProtocol.Clear(AllKeys), ct);

    /// <summary>
    /// Rend la main quand STP est écrit, donc quand tout ce qui a été mis en file avant est affiché.
    /// </summary>
    public Task CommitAsync(CancellationToken ct) => SendCommandAsync(StreamDockProtocol.Commit(), ct);

    public Task SleepAsync(CancellationToken ct) => SendCommandAsync(StreamDockProtocol.Sleep(), ct);

    /// <summary>
    /// Arrêt best effort dans un budget d'une seconde, partagé par les appels concurrents ; ne lève jamais.
    /// La fermeture du handle est toujours lancée ; budget épuisé, elle s'achève en arrière-plan.
    /// </summary>
    public ValueTask DisposeAsync() => new(_shutdown.Value);

    /// <summary>
    /// Seule la dernière image demandée pour une touche ou un écran est écrite : une demande plus récente abandonne
    /// l'encodage et l'écriture non commencée de la précédente, qui rend la main sans erreur.
    /// </summary>
    private async Task SendImageAsync(byte target, SurfaceImage image, int size, CancellationToken ct)
    {
        CancellationTokenSource superseded;
        CancellationTokenSource? older;
        lock (_gate)
        {
            ThrowIfUnusable();
            _encodingByTarget.TryGetValue(target, out older);
            superseded = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _encodingByTarget[target] = superseded;
        }
        CancelQuietly(older);

        try
        {
            var jpeg = await JpegEncodingThread.EncodeAsync(image, size, superseded.Token);
            lock (_gate)
            {
                if (_encodingByTarget.GetValueOrDefault(target) != superseded)
                    return;
                ThrowIfUnusable();
                var olderWrite = _queue.Where(write => write.ImageTarget == target).FirstOrDefault();
                if (olderWrite != null)
                    _queue.Remove(olderWrite);
                _queue.AddLast(new PendingWrite([.. StreamDockProtocol.Image(target, jpeg)], target, Written: null, IsKeepAlive: false));
            }
            _queueSignal.Release();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested == false && superseded.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_gate)
            {
                if (_encodingByTarget.GetValueOrDefault(target) == superseded)
                    _encodingByTarget.Remove(target);
            }
            superseded.Dispose();
        }
    }

    private Task SendCommandAsync(byte[] report, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return Task.FromCanceled(ct);

        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            var refusal = Refusal();
            if (refusal != null)
                return Task.FromException(refusal);
            _queue.AddLast(new PendingWrite([report], ImageTarget: null, written, IsKeepAlive: false));
        }
        _queueSignal.Release();
        return written.Task.WaitAsync(ct);
    }

    /// <summary>
    /// CONNECT passe devant les images en attente, juste après l'écriture en cours.
    /// </summary>
    private void QueueKeepAlive()
    {
        lock (_gate)
        {
            if (_isQueueClosed || _queue.Where(write => write.IsKeepAlive).Any())
                return;
            _queue.AddFirst(new PendingWrite([StreamDockProtocol.KeepAlive()], ImageTarget: null, Written: null, IsKeepAlive: true));
        }
        _queueSignal.Release();
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            while (true)
            {
                PendingWrite? write;
                lock (_gate)
                {
                    if (_isQueueClosed && _queue.Count == 0)
                        return;
                    write = _queue.First?.Value;
                    if (write != null)
                        _queue.RemoveFirst();
                    _currentWrite = write;
                }
                if (write == null)
                {
                    await _queueSignal.WaitAsync(_stopping.Token);
                    continue;
                }

                foreach (var report in write.Reports)
                {
                    await _connection.WriteAsync(report, _stopping.Token);
                }
                write.Written?.TrySetResult();
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ReportDisconnection(ex);
        }
    }

    private async Task ReadLoopAsync()
    {
        var buffer = new byte[StreamDockModel.InputReportLength];
        try
        {
            while (true)
            {
                var length = await _connection.ReadAsync(buffer, _stopping.Token);
                if (length == 0)
                    throw new IOException("HID read returned no data.");

                var keyEvent = StreamDockProtocol.ParseInput(buffer.AsSpan(0, length));
                if (keyEvent != null)
                    RaiseKeyChanged(keyEvent.Value);
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ReportDisconnection(ex);
        }
    }

    /// <summary>
    /// Première erreur seulement, hors fermeture : les écritures en attente échouent, Disconnected est levé sur un
    /// autre thread (un abonné qui ferme la surface n'attend pas la boucle qui le prévient), puis le handle est fermé.
    /// </summary>
    private void ReportDisconnection(Exception cause)
    {
        List<Action<Exception>> handlers;
        List<PendingWrite> abandoned;
        lock (_gate)
        {
            if (_disconnectionCause != null || _isDisposed)
                return;
            _disconnectionCause = cause;
            abandoned = CloseQueue();
            handlers = [.. _disconnectedHandlers];
            _notifications.Add(Task.Run(() => RaiseDisconnected(handlers, cause)));
        }

        FailWrites(abandoned, new IOException("Stream Dock disconnected before the write completed.", cause));
        _keepAlive?.Dispose();
        CancelStopping();
        CloseConnection();
    }

    private async Task ShutdownAsync()
    {
        using var budget = new CancellationTokenSource(ShutdownBudget, _time);
        List<PendingWrite> abandoned;
        bool sendsShutdown;
        lock (_gate)
        {
            _isDisposed = true;
            abandoned = CloseQueue();
            sendsShutdown = _writeLoop != null && _disconnectionCause == null;
        }
        var disposedFailure = new ObjectDisposedException(nameof(StreamDockSurface));
        FailWrites(abandoned, disposedFailure);
        _keepAlive?.Dispose();

        try
        {
            if (_writeLoop != null)
                await _writeLoop.WaitAsync(budget.Token);
            if (sendsShutdown)
            {
                byte[][] shutdown = [StreamDockProtocol.Clear(AllKeys), StreamDockProtocol.Commit(),
                    StreamDockProtocol.Disconnect(), StreamDockProtocol.Sleep()];
                foreach (var report in shutdown)
                {
                    await _connection.WriteAsync(report, budget.Token).WaitAsync(budget.Token);
                }
            }
        }
        catch (Exception ex)
        {
            DriverTrace.Warning("Stream Dock shutdown sequence incomplete", ex);
        }

        PendingWrite? interrupted;
        Task[] pending;
        lock (_gate)
        {
            interrupted = _currentWrite;
            pending = [.. _notifications, _readLoop ?? Task.CompletedTask];
        }
        interrupted?.Written?.TrySetException(disposedFailure);

        // Annulation et fermeture hors de l'appelant : même bloquées, elles ne retiennent pas la fin de l'arrêt.
        var closing = Task.Run(() =>
        {
            CancelStopping();
            CloseConnection();
        });
        try
        {
            await Task.WhenAll([closing, .. pending]).WaitAsync(budget.Token);
        }
        catch (Exception ex)
        {
            DriverTrace.Warning("Stream Dock shutdown exceeded its budget", ex);
        }
    }

    /// <summary>
    /// Appelé sous le verrou : plus rien n'entre dans la file, et les écritures non commencées sont rendues.
    /// </summary>
    private List<PendingWrite> CloseQueue()
    {
        _isQueueClosed = true;
        List<PendingWrite> abandoned = [.. _queue];
        if (_currentWrite != null && _disconnectionCause != null)
            abandoned.Add(_currentWrite);
        _queue.Clear();
        _queueSignal.Release();
        return abandoned;
    }

    private static void FailWrites(List<PendingWrite> writes, Exception failure)
    {
        foreach (var write in writes)
        {
            write.Written?.TrySetException(failure);
        }
    }

    /// <summary>
    /// Appelé sous le verrou.
    /// </summary>
    private Exception? Refusal()
    {
        if (_isDisposed)
            return new ObjectDisposedException(nameof(StreamDockSurface));
        if (_disconnectionCause != null)
            return new IOException("Stream Dock is disconnected.", _disconnectionCause);
        return null;
    }

    private void ThrowIfUnusable()
    {
        var refusal = Refusal();
        if (refusal != null)
            throw refusal;
    }

    private void CancelStopping()
    {
        try
        {
            _stopping.Cancel();
        }
        catch (Exception ex)
        {
            DriverTrace.Warning("Stream Dock cancellation failed", ex);
        }
    }

    private void CloseConnection()
    {
        if (Interlocked.Exchange(ref _isConnectionClosed, 1) == 1)
            return;
        try
        {
            _connection.Dispose();
        }
        catch (Exception ex)
        {
            DriverTrace.Warning("Stream Dock handle close failed", ex);
        }
    }

    private static void CancelQuietly(CancellationTokenSource? source)
    {
        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // L'encodage précédent est déjà terminé : rien à abandonner.
        }
    }

    /// <summary>
    /// Aucun appui n'est diffusé après la déconnexion ou la fermeture.
    /// </summary>
    private void RaiseKeyChanged(KeyEvent keyEvent)
    {
        foreach (var handler in KeyChanged?.GetInvocationList() ?? [])
        {
            lock (_gate)
            {
                if (_isDisposed || _disconnectionCause != null)
                    return;
            }
            try
            {
                ((Action<KeyEvent>)handler)(keyEvent);
            }
            catch (Exception ex)
            {
                DriverTrace.Warning("Stream Dock KeyChanged subscriber failed", ex);
            }
        }
    }

    private void RaiseDisconnected(List<Action<Exception>> handlers, Exception cause)
    {
        foreach (var handler in handlers)
        {
            if (_isDisposed)
                return;
            try
            {
                handler(cause);
            }
            catch (Exception ex)
            {
                DriverTrace.Warning("Stream Dock Disconnected subscriber failed", ex);
            }
        }
    }

    /// <summary>
    /// Rapports écrits d'un bloc ; Written est terminé une fois le dernier écrit, pour les commandes seulement.
    /// </summary>
    private sealed record PendingWrite(byte[][] Reports, byte? ImageTarget, TaskCompletionSource? Written, bool IsKeepAlive);
}
