using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DockPad.Models;
using DockPad.Surfaces;

namespace DockPad.Services.StreamDock;

/// <summary>
/// Sérialise connexion, affichage et appuis ; seule une vue validée par la surface peut déclencher une action.
/// </summary>
public sealed class StreamDockService : IAsyncDisposable
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RedrawInterval = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan MaximumRedrawInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(1);
    private readonly IControlSurfaceDriver _driver;
    private readonly IDockSources _sources;
    private readonly IDockRenderer _renderer;
    private readonly TimeProvider _time;
    private readonly Action<string, Exception?> _log;
    private readonly CancellationToken _stopToken;
    private readonly object _gate = new();
    private readonly Channel<Func<Task>> _work = Channel.CreateUnbounded<Func<Task>>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _stopSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lazy<Task> _shutdown;
    private readonly Dictionary<SurfaceKey, long> _presses = [];
    private Session? _session;
    private DisplayedView? _displayed;
    private DockScreen _wantedScreen = new DockScreen.Home(0);
    private UsageDisplay?[] _displays = [];
    private ITimer? _retry;
    private ITimer? _redraw;
    private Task _worker = Task.CompletedTask;
    private Task _rendering = Task.CompletedTask;
    private Task _closing = Task.CompletedTask;
    private long? _redrawStarted;
    private bool _started;
    private bool _stopped;
    private long _redrawRevision;
    private long _gridRevision;
    private long _viewNumber;
    private string? _connectionState;

    public StreamDockService(IControlSurfaceDriver driver, IDockSources sources, IDockRenderer renderer, TimeProvider time,
        Action<string, Exception?>? log = null)
    {
        _driver = driver;
        _sources = sources;
        _renderer = renderer;
        _time = time;
        _log = log ?? WriteLog;
        _stopToken = _stopping.Token;
        _shutdown = new Lazy<Task>(StopAsync);
    }

    public DockScreen Screen
    {
        get
        {
            lock (_gate)
                return _displayed?.View.Screen ?? new DockScreen.Home(0);
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _stopped) return;
            _started = true;
            _worker = Task.Run(WorkAsync);
            Enqueue(ConnectAsync);
            try
            {
                _driver.SurfacesChanged += OnSurfacesChanged;
                _retry = _time.CreateTimer(_ => OnSurfacesChanged(), null, RetryInterval, RetryInterval);
            }
            catch (Exception ex) { ReportFailure(ex, "Stream Dock : surveillance indisponible."); }
        }
    }

    public void OnGridChanged()
    {
        lock (_gate)
        {
            if (_started == false || _stopped) return;
            long revision = ++_gridRevision;
            ++_redrawRevision;
            try
            {
                _redrawStarted ??= _time.GetTimestamp();
                var remaining = MaximumRedrawInterval - _time.GetElapsedTime(_redrawStarted.Value);
                var delay = remaining < RedrawInterval ? remaining : RedrawInterval;
                if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
                _redraw?.Dispose();
                _redraw = _time.CreateTimer(_ => QueueRedraw(revision), null, delay, Timeout.InfiniteTimeSpan);
            }
            catch (Exception ex) { ReportFailure(ex, "Stream Dock : planification échouée."); }
        }
    }

    private void QueueRedraw(long revision)
    {
        lock (_gate)
        {
            if (_stopped || revision != _gridRevision) return;
            _redrawStarted = null;
            Enqueue(RedrawAsync);
        }
    }

    public ValueTask DisposeAsync() => new(_shutdown.Value);

    // Barrière de synchronisation des tests : ne fait pas avancer les minuteurs.
    internal Task WhenIdleAsync()
    {
        var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_started == false || _stopped) return Task.CompletedTask;
            Enqueue(() =>
            {
                _ = CompleteWhenIdleAsync(_rendering, idle);
                return Task.CompletedTask;
            });
        }
        return idle.Task;
    }

    private async Task CompleteWhenIdleAsync(Task rendering, TaskCompletionSource idle)
    {
        await Task.WhenAny(rendering, _stopSignal.Task).ConfigureAwait(false);
        lock (_gate)
        {
            if (_stopped)
            {
                idle.TrySetResult();
                return;
            }
            Enqueue(() =>
            {
                if (rendering == _rendering) idle.TrySetResult();
                else _ = CompleteWhenIdleAsync(_rendering, idle);
                return Task.CompletedTask;
            });
        }
    }

    private void OnSurfacesChanged() => Enqueue(ConnectAsync);

    private void Enqueue(Func<Task> work) => _work.Writer.TryWrite(work);

    private async Task WorkAsync()
    {
        try
        {
            await foreach (var work in _work.Reader.ReadAllAsync(_stopToken))
            {
                if (_stopSignal.Task.IsCompleted) return;
                try { await work().ConfigureAwait(false); }
                catch (OperationCanceledException) when (_stopSignal.Task.IsCompleted) { return; }
                catch (Exception ex)
                {
                    // Le rappel de déconnexion possède la cause et la reprise immédiate.
                    if (_session?.Interrupted.Task.IsCompleted == true) continue;
                    ReportFailure(ex, "Stream Dock : connexion échouée, reprise prévue.");
                    ForgetSurface();
                }
            }
        }
        catch (OperationCanceledException) when (_stopSignal.Task.IsCompleted) { }
        catch (Exception ex) { ReportFailure(ex, "Stream Dock : boucle interrompue."); }
    }

    private async Task ConnectAsync()
    {
        if (_session != null || _stopSignal.Task.IsCompleted) return;
        try
        {
            await _closing.WaitAsync(_stopToken).ConfigureAwait(false);
            _stopToken.ThrowIfCancellationRequested();
            var info = _driver.FindSurfaces().FirstOrDefault();
            if (info == null)
            {
                ReportState("absent");
                return;
            }

            // L'ouverture tardive reste responsable de fermer la surface après un arrêt.
            _stopToken.ThrowIfCancellationRequested();
            var opening = OpenSessionAsync(info);
            try { await opening.WaitAsync(_stopToken).ConfigureAwait(false); }
            catch
            {
                if (opening.IsCompleted == false) _ = ObserveAsync(opening);
                throw;
            }
            var session = _session;
            if (session == null) return;
            _presses.Clear();
            _wantedScreen = new DockScreen.Home(0);
            _displays = new UsageDisplay?[session.Surface.Description.DisplayCount];
            await ShowScreenAsync(session, CurrentRevision()).ConfigureAwait(false);
        }
        catch (SurfaceBusyException) { ReportState("occupé"); }
    }

    private async Task OpenSessionAsync(SurfaceInfo info)
    {
        var surface = await _driver.OpenAsync(info, _stopToken).ConfigureAwait(false);
        var session = new Session(surface, ReportFailure, CancelAndDisposeAsync);
        session.KeyHandler = key => ReceiveKey(session, key);
        session.DisconnectHandler = cause => ReceiveDisconnection(session, cause);
        try
        {
            surface.KeyChanged += session.KeyHandler;
            surface.Disconnected += session.DisconnectHandler;
        }
        catch
        {
            await CloseSurfaceAsync(session).ConfigureAwait(false);
            throw;
        }
        lock (_gate)
        {
            if (_stopped == false)
            {
                _session = session;
                return;
            }
        }
        await CloseSurfaceAsync(session).ConfigureAwait(false);
    }

    private static void WriteLog(string message, Exception? cause)
    {
        if (cause == null) LogService.Info(message);
        else LogService.Warn(cause, message);
    }

    private void ReportFailure(Exception cause, string message) =>
        ReportState($"en échec ({cause.GetType().FullName})", cause, message);

    private void ReportState(string state, Exception? cause = null, string? message = null)
    {
        lock (_gate)
        {
            if (_connectionState == state) return;
            _connectionState = state;
        }
        _log(message ?? $"Stream Dock : {state}.", cause);
    }

    private void ReceiveDisconnection(Session session, Exception cause)
    {
        bool reconnect;
        lock (_gate)
        {
            if (_stopped || session.Interrupted.Task.IsCompleted) return;
            session.Disconnection = cause;
            session.Interrupted.TrySetResult();
            reconnect = _session == session;
        }
        _ = session.Cancel.Value;
        Enqueue(async () =>
        {
            if (_session != session) return;
            ReportFailure(cause, "Stream Dock déconnecté ; reconnexion immédiate.");
            ForgetSurface();
            if (reconnect) await ConnectAsync().ConfigureAwait(false);
        });
    }

    private void ReceiveKey(Session session, KeyEvent key)
    {
        lock (_gate)
        {
            if (_stopped || _session != session || session.Interrupted.Task.IsCompleted || _displayed == null) return;
            long number = _displayed.Number;
            Enqueue(() => HandleKeyAsync(session, key, number));
        }
    }

    private async Task HandleKeyAsync(Session session, KeyEvent keyEvent, long receivedView)
    {
        if (_session != session || session.Interrupted.Task.IsCompleted || _displayed == null) return;
        var description = session.Surface.Description;
        var key = keyEvent.Key;
        if (key.Row < 0 || key.Row >= description.KeyRows || key.Column < 0 || key.Column >= description.KeyColumns) return;
        int index = key.Row * description.KeyColumns + key.Column;
        var displayed = _displayed;
        var content = displayed.View.Keys[index];

        if (keyEvent.State == KeyState.Pressed)
        {
            if (_presses.ContainsKey(key)) return;
            // Même périmé, un appui reste mémorisé jusqu'au relâchement pour interdire les répétitions.
            _presses.Add(key, receivedView);
            if (receivedView != displayed.Number || content is DockKey.Empty) return;
            await SendKeyAsync(session, key, displayed.Pressed[index]).ConfigureAwait(false);
            return;
        }

        if (_presses.Remove(key, out long pressedView) == false || content is DockKey.Empty) return;
        await SendKeyAsync(session, key, displayed.Normal[index]).ConfigureAwait(false);
        if (pressedView != displayed.Number || receivedView != displayed.Number) return;
        ThrowIfInterrupted(session);
        var target = DockLayout.Navigate(displayed.View, content);
        if (target != null)
        {
            _wantedScreen = target;
            await ShowScreenAsync(session, NextRevision()).ConfigureAwait(false);
        }
        else if (content is DockKey.RunTile tile)
        {
            try
            {
                _sources.Run(SnapshotEntry(tile.Entry));
                ReportState("connecté");
            }
            catch (Exception ex) { ReportFailure(ex, "Stream Dock : lancement échoué."); }
        }
    }

    private async Task SendKeyAsync(Session session, SurfaceKey key, SurfaceImage image)
    {
        ThrowIfInterrupted(session);
        await InSessionAsync(session, session.Surface.SetKeyImageAsync(key, image, session.Token)).ConfigureAwait(false);
        await InSessionAsync(session, session.Surface.CommitAsync(session.Token)).ConfigureAwait(false);
    }

    private Task RedrawAsync()
    {
        if (_session == null) return Task.CompletedTask;
        return ShowScreenAsync(_session, NextRevision(), keepScreen: true);
    }

    private long CurrentRevision()
    {
        lock (_gate) return _redrawRevision;
    }

    private long NextRevision()
    {
        lock (_gate) return ++_redrawRevision;
    }

    private Task ShowScreenAsync(Session session, long revision, bool keepScreen = false)
    {
        ThrowIfInterrupted(session);
        var description = session.Surface.Description;
        DockView view;
        try
        {
            var entries = _sources.Entries().Select(SnapshotEntry).ToArray();
            var pages = _sources.Pages();
            var screen = keepScreen ? DockLayout.Keep(_wantedScreen, entries) : _wantedScreen;
            view = DockLayout.Build(screen, entries, pages, description.KeyRows * description.KeyColumns);
        }
        catch (Exception ex)
        {
            ReportFailure(ex, "Stream Dock : sources illisibles.");
            return Task.CompletedTask;
        }
        _rendering = RenderAndQueueAsync(session, revision, view, _displays.ToArray());
        return Task.CompletedTask;
    }

    private async Task RenderAndQueueAsync(Session session, long revision, DockView view, UsageDisplay?[] displays)
    {
        try
        {
            var description = session.Surface.Description;
            var normal = new SurfaceImage[view.Keys.Count];
            var pressed = new SurfaceImage[view.Keys.Count];
            var displayImages = new SurfaceImage[displays.Length];
            for (int index = 0; index < view.Keys.Count; index++)
            {
                normal[index] = await InSessionAsync(session, _renderer.RenderKeyAsync(
                    view.Keys[index], pressed: false, description.KeyImageSize)).ConfigureAwait(false);
                pressed[index] = await InSessionAsync(session, _renderer.RenderKeyAsync(
                    view.Keys[index], pressed: true, description.KeyImageSize)).ConfigureAwait(false);
                if (revision != CurrentRevision()) return;
            }
            for (int index = 0; index < displays.Length; index++)
                displayImages[index] = await InSessionAsync(session,
                    _renderer.RenderDisplayAsync(displays[index], description.DisplayImageSize)).ConfigureAwait(false);
            Enqueue(() => PublishViewAsync(session, revision, view, normal, pressed, displays, displayImages));
        }
        catch (Exception ex)
        {
            Enqueue(() => _session == session && revision == CurrentRevision()
                ? Task.FromException(ex) : Task.CompletedTask);
        }
    }

    private async Task PublishViewAsync(Session session, long revision, DockView view,
        SurfaceImage[] normal, SurfaceImage[] pressed, UsageDisplay?[] displays, SurfaceImage[] displayImages)
    {
        if (_session != session || revision != CurrentRevision()) return;
        ThrowIfInterrupted(session);
        var description = session.Surface.Description;
        var previous = _displayed;
        for (int index = 0; index < view.Keys.Count; index++)
        {
            if (previous != null && SameImage(previous.Normal[index], normal[index])) continue;
            var key = new SurfaceKey(index / description.KeyColumns, index % description.KeyColumns);
            await InSessionAsync(session, session.Surface.SetKeyImageAsync(key, normal[index], session.Token)).ConfigureAwait(false);
        }
        for (int index = 0; index < displays.Length; index++)
        {
            if (previous != null && previous.Displays[index] == displays[index]) continue;
            await InSessionAsync(session, session.Surface.SetDisplayImageAsync(index, displayImages[index], session.Token)).ConfigureAwait(false);
        }
        await InSessionAsync(session, session.Surface.CommitAsync(session.Token)).ConfigureAwait(false);
        lock (_gate)
        {
            ThrowIfInterrupted(session);
            _displayed = new DisplayedView(++_viewNumber, view, normal, pressed, displays);
            _wantedScreen = view.Screen;
        }
        ReportState("connecté");
    }

    private static ShortcutEntry SnapshotEntry(ShortcutEntry entry)
    {
        var copy = TileGroupService.Clone(entry);
        CopySlots(entry, copy);
        return copy;
    }

    private static void CopySlots(ShortcutEntry source, ShortcutEntry copy)
    {
        // Slot n'est pas sérialisé par le clonage commun, mais détermine l'ordre et la cible des enfants.
        copy.Slot = source.Slot;
        if (source.Children == null || copy.Children == null) return;
        for (int index = 0; index < source.Children.Count; index++)
        {
            var child = source.Children[index];
            var childCopy = copy.Children[index];
            if (child != null && childCopy != null) CopySlots(child, childCopy);
        }
    }

    private static bool SameImage(SurfaceImage first, SurfaceImage second) =>
        first.Width == second.Width && first.Height == second.Height && first.Bgra32.AsSpan().SequenceEqual(second.Bgra32);

    private void ThrowIfInterrupted(Session session)
    {
        if (_stopSignal.Task.IsCompleted) throw new OperationCanceledException(_stopToken);
        if (session.Interrupted.Task.IsCompleted == false) return;
        if (session.Disconnection != null) ExceptionDispatchInfo.Capture(session.Disconnection).Throw();
        throw new OperationCanceledException(session.Token);
    }

    private async Task InSessionAsync(Session session, Task operation)
    {
        var completed = await Task.WhenAny(operation, session.Interrupted.Task, _stopSignal.Task).ConfigureAwait(false);
        if (completed != operation || session.Interrupted.Task.IsCompleted || _stopSignal.Task.IsCompleted)
            _ = ObserveAsync(operation);
        ThrowIfInterrupted(session);
        await operation.ConfigureAwait(false);
    }

    private async Task<T> InSessionAsync<T>(Session session, Task<T> operation)
    {
        await InSessionAsync(session, (Task)operation).ConfigureAwait(false);
        return await operation.ConfigureAwait(false);
    }

    private async Task ObserveAsync(Task operation)
    {
        try { await operation.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (Exception ex) { ReportFailure(ex, "Stream Dock : opération abandonnée terminée en échec."); }
    }

    private void ForgetSurface()
    {
        Session? session;
        lock (_gate)
        {
            session = _session;
            _session = null;
            _displayed = null;
        }
        _presses.Clear();
        _displays = [];
        if (session == null) return;
        session.Interrupted.TrySetResult();
        _closing = CloseSurfaceAsync(session);
    }

    private async Task CloseSurfaceAsync(Session session)
    {
        using var budget = new CancellationTokenSource(ShutdownBudget, _time);
        session.Interrupted.TrySetResult();
        _ = session.Cancel.Value;
        try { await session.Close.Value.WaitAsync(budget.Token).ConfigureAwait(false); }
        catch (Exception ex) { ReportFailure(ex, "Stream Dock : fermeture incomplète."); }
    }

    private async Task CancelAndDisposeAsync(CancellationTokenSource cancellation)
    {
        try { await cancellation.CancelAsync().ConfigureAwait(false); }
        catch (Exception ex) { ReportFailure(ex, "Stream Dock : annulation échouée."); }
        finally { cancellation.Dispose(); }
    }

    private async Task StopAsync()
    {
        // Le budget démarre avant de déléguer la fermeture : un pilote peut bloquer dès l'appel de DisposeAsync.
        using var budget = new CancellationTokenSource(ShutdownBudget, _time);
        Session? session;
        lock (_gate)
        {
            _stopped = true;
            _stopSignal.TrySetResult();
            _work.Writer.TryComplete();
            session = _session;
        }
        var cleanup = Task.Run(async () =>
        {
            try
            {
                _retry?.Dispose();
                _redraw?.Dispose();
                _driver.SurfacesChanged -= OnSurfacesChanged;
            }
            catch (Exception ex) { ReportFailure(ex, "Stream Dock : surveillance persistante après arrêt."); }
            _ = CancelAndDisposeAsync(_stopping);
            if (session != null)
            {
                session.Interrupted.TrySetResult();
                await CloseSurfaceAsync(session).ConfigureAwait(false);
            }
            await _closing.ConfigureAwait(false);
            await _worker.ConfigureAwait(false);
        });
        try { await cleanup.WaitAsync(budget.Token).ConfigureAwait(false); }
        catch (Exception ex)
        {
            ReportFailure(ex, "Stream Dock : arrêt interrompu après expiration.");
            _ = ObserveAsync(cleanup);
        }
    }

    private sealed class Session
    {
        private readonly CancellationTokenSource _cancellation = new();
        public IControlSurface Surface { get; }
        public CancellationToken Token { get; }
        public Exception? Disconnection { get; set; }
        public Lazy<Task> Cancel { get; }
        public TaskCompletionSource Interrupted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action<KeyEvent>? KeyHandler { get; set; }
        public Action<Exception>? DisconnectHandler { get; set; }
        public Lazy<Task> Close { get; }

        public Session(IControlSurface surface, Action<Exception, string> reportFailure,
            Func<CancellationTokenSource, Task> cancelAndDispose)
        {
            Surface = surface;
            Token = _cancellation.Token;
            Cancel = new Lazy<Task>(() => cancelAndDispose(_cancellation));
            Close = new Lazy<Task>(() => Task.Run(async () =>
            {
                try
                {
                    Surface.KeyChanged -= KeyHandler;
                    Surface.Disconnected -= DisconnectHandler;
                }
                catch (Exception ex) { reportFailure(ex, "Stream Dock : désabonnement échoué."); }
                finally
                {
                    try { await Surface.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception ex) { reportFailure(ex, "Stream Dock : fermeture échouée."); }
                }
            }));
        }
    }

    private sealed record DisplayedView(long Number, DockView View, SurfaceImage[] Normal,
        SurfaceImage[] Pressed, UsageDisplay?[] Displays);
}
