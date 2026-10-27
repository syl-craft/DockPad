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
    private static readonly TimeSpan LoadInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan UsageInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RedrawInterval = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan MaximumRedrawInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(1);
    private readonly IControlSurfaceDriver _driver;
    private readonly IDockSources _sources;
    private readonly IDockRenderer _renderer;
    private readonly TimeProvider _time;
    private readonly ISystemLoadMonitor _load;
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
    private SideStripState _sideStrip = new([], new UsageConfig(), new SystemLoad(null, null), SideStripStyle.Columns);
    private long _sideStripRevision;
    private SurfaceImage? _publishedSideStrip;
    private Task _renderingSideStrip = Task.CompletedTask;
    private Task _pendingSideStripRenders = Task.CompletedTask;
    private Type? _loadFailure;
    private ITimer? _loadTimer;
    private Type? _sideStripFailure;
    private ITimer? _usageTimer;
    private Task _readingUsages = Task.CompletedTask;
    private Type? _usageFailure;
    private bool _syncPages;
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
    private int _queuedWork;
    private string? _connectionState;

    public StreamDockService(IControlSurfaceDriver driver, IDockSources sources, IDockRenderer renderer, TimeProvider time,
        ISystemLoadMonitor load, Action<string, Exception?>? log = null)
    {
        _driver = driver;
        _sources = sources;
        _renderer = renderer;
        _time = time;
        _load = load;
        _log = log ?? WriteLog;
        _stopToken = _stopping.Token;
        _shutdown = new Lazy<Task>(StopAsync);
    }

    public bool SyncPages
    {
        get => Volatile.Read(ref _syncPages);
        set
        {
            lock (_gate)
            {
                if (_syncPages == value) return;
                Volatile.Write(ref _syncPages, value);
                if (value && _started && _stopped == false)
                    Enqueue(AlignShortcutPageAsync);
            }
        }
    }

    public SideStripStyle SideStripStyle
    {
        get { lock (_gate) return _sideStrip.Style; }
        set
        {
            lock (_gate)
            {
                var style = Enum.IsDefined(value) ? value : SideStripStyle.Columns;
                if (_sideStrip.Style == style) return;
                _sideStrip = _sideStrip with { Style = style };
                ++_sideStripRevision;
                if (_started && _stopped == false) Enqueue(RedrawSideStripAsync);
            }
        }
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

    /// <summary>
    /// Deux changements croisés pendant le passage sur le thread UI peuvent laisser les pages désalignées.
    /// </summary>
    public void OnShortcutPageChanged(int page, PageChangeOrigin origin)
    {
        lock (_gate)
        {
            if (_started == false || _stopped || SyncPages == false || origin != PageChangeOrigin.DockPad) return;
            Enqueue(() => ShowShortcutPageAsync(page));
        }
    }

    private Task AlignShortcutPageAsync()
    {
        if (_session == null || SyncPages == false) return Task.CompletedTask;
        try { return ShowShortcutPageAsync(_sources.CurrentShortcutPage()); }
        catch (Exception ex)
        {
            ReportFailure(ex, "Stream Dock : source Raccourcis illisible.");
            return Task.CompletedTask;
        }
    }

    private Task ShowShortcutPageAsync(int page)
    {
        var session = _session;
        if (session == null || SyncPages == false) return Task.CompletedTask;
        try
        {
            if (DockLayout.PagesWithTiles(_sources.Entries()).Contains(page) == false) return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            ReportFailure(ex, "Stream Dock : source Raccourcis illisible.");
            return Task.CompletedTask;
        }
        var target = new DockScreen.Page(page, 0);
        if (_wantedScreen == target) return Task.CompletedTask;
        _wantedScreen = target;
        return ShowScreenAsync(session, NextRevision());
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
    internal Task WhenIdleAsync(bool includeUsages = true)
    {
        var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_started == false || _stopped) return Task.CompletedTask;
            Enqueue(() =>
            {
                _ = CompleteWhenIdleAsync(_rendering, _renderingSideStrip, includeUsages ? _readingUsages : Task.CompletedTask, includeUsages, idle);
                return Task.CompletedTask;
            });
        }
        return idle.Task;
    }

    // Inclut la continuation des rendus des sessions interrompues, sans retarder la fermeture.
    internal Task WhenSideStripRendersCompleteAsync()
    {
        lock (_gate) return _pendingSideStripRenders;
    }

    private async Task CompleteWhenIdleAsync(Task rendering, Task renderingSideStrip, Task readingUsages, bool includeUsages, TaskCompletionSource idle)
    {
        await Task.WhenAny(Task.WhenAll(rendering, renderingSideStrip, readingUsages), _stopSignal.Task).ConfigureAwait(false);
        lock (_gate)
        {
            if (_stopped)
            {
                idle.TrySetResult();
                return;
            }
            Enqueue(() =>
            {
                var currentUsages = includeUsages ? _readingUsages : Task.CompletedTask;
                // Une lecture terminée de façon synchrone renvoie la même tâche partagée : seule la file vide prouve que sa publication est passée.
                if (rendering == _rendering && renderingSideStrip == _renderingSideStrip && readingUsages == currentUsages && Volatile.Read(ref _queuedWork) == 0) idle.TrySetResult();
                else _ = CompleteWhenIdleAsync(_rendering, _renderingSideStrip, currentUsages, includeUsages, idle);
                return Task.CompletedTask;
            });
        }
    }

    private void OnSurfacesChanged() => Enqueue(ConnectAsync);

    private void Enqueue(Func<Task> work)
    {
        Interlocked.Increment(ref _queuedWork);
        if (_work.Writer.TryWrite(work) == false) Interlocked.Decrement(ref _queuedWork);
    }

    private async Task WorkAsync()
    {
        try
        {
            await foreach (var work in _work.Reader.ReadAllAsync(_stopToken))
            {
                Interlocked.Decrement(ref _queuedWork);
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
            lock (_gate)
            {
                _sideStrip = new SideStripState([], new UsageConfig(), new SystemLoad(null, null), _sideStrip.Style);
                ++_sideStripRevision;
                _publishedSideStrip = null;
            }
            if (SyncPages)
            {
                try
                {
                    int page = _sources.CurrentShortcutPage();
                    if (DockLayout.PagesWithTiles(_sources.Entries()).Contains(page))
                        _wantedScreen = new DockScreen.Page(page, 0);
                }
                catch (Exception ex) { ReportFailure(ex, "Stream Dock : source Raccourcis illisible."); }
            }
            await ShowScreenAsync(session, CurrentRevision()).ConfigureAwait(false);
            lock (_gate)
            {
                if (_stopped || _session != session || session.Interrupted.Task.IsCompleted) return;
                if (session.Surface.Description.SideStrip == null) return;
                _load.Reset();
                _loadTimer = _time.CreateTimer(_ => Enqueue(() => SampleLoadAsync(session)), null, LoadInterval, LoadInterval);
                Enqueue(() => SampleLoadAsync(session));
                _usageTimer = _time.CreateTimer(_ => QueueUsageRefresh(session), null, UsageInterval, UsageInterval);
                QueueUsageRefresh(session);
            }
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
            int? shortcutPage = content is DockKey.OpenPage page ? page.PageIndex : null;
            await ShowScreenAsync(session, NextRevision(), shortcutPage: shortcutPage).ConfigureAwait(false);
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

    private Task ShowScreenAsync(Session session, long revision, bool keepScreen = false, int? shortcutPage = null)
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
        _rendering = RenderAndQueueAsync(session, revision, view, shortcutPage);
        return Task.CompletedTask;
    }

    private async Task RenderAndQueueAsync(Session session, long revision, DockView view, int? shortcutPage)
    {
        try
        {
            var description = session.Surface.Description;
            var normal = new SurfaceImage[view.Keys.Count];
            var pressed = new SurfaceImage[view.Keys.Count];
            for (int index = 0; index < view.Keys.Count; index++)
            {
                normal[index] = await InSessionAsync(session, _renderer.RenderKeyAsync(
                    view.Keys[index], pressed: false, description.KeyImageSize)).ConfigureAwait(false);
                pressed[index] = await InSessionAsync(session, _renderer.RenderKeyAsync(
                    view.Keys[index], pressed: true, description.KeyImageSize)).ConfigureAwait(false);
                if (revision != CurrentRevision()) return;
            }
            Enqueue(() => PublishViewAsync(session, revision, view, normal, pressed, shortcutPage));
        }
        catch (Exception ex)
        {
            Enqueue(() => _session == session && revision == CurrentRevision()
                ? Task.FromException(ex) : Task.CompletedTask);
        }
    }

    private async Task PublishViewAsync(Session session, long revision, DockView view,
        SurfaceImage[] normal, SurfaceImage[] pressed, int? shortcutPage)
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
        await InSessionAsync(session, session.Surface.CommitAsync(session.Token)).ConfigureAwait(false);
        lock (_gate)
        {
            ThrowIfInterrupted(session);
            _displayed = new DisplayedView(++_viewNumber, view, normal, pressed);
            _wantedScreen = view.Screen;
        }
        ReportState("connecté");
        if (SyncPages && shortcutPage != null && view.Screen is DockScreen.Page page && page.PageIndex == shortcutPage.Value)
        {
            try { _sources.ShowShortcutPage(page.PageIndex); }
            catch (Exception ex) { ReportFailure(ex, "Stream Dock : synchronisation échouée."); }
        }
    }

    public void OnUsageConfigChanged()
    {
        lock (_gate)
        {
            if (_started == false || _stopped) return;
            Enqueue(() =>
            {
                var session = _session;
                if (session == null || session.Interrupted.Task.IsCompleted || session.Surface.Description.SideStrip == null) return Task.CompletedTask;
                lock (_gate)
                {
                    var refresh = session.UsageRefresh;
                    session.UsageRefresh = null;
                    if (refresh != null) _ = refresh.Cancel.Value;
                    QueueUsageRefresh(session);
                }
                return Task.CompletedTask;
            });
        }
    }

    private void QueueUsageRefresh(Session session)
    {
        lock (_gate)
        {
            if (_stopped || _session != session || session.Interrupted.Task.IsCompleted || session.UsageRefresh != null
                || session.Surface.Description.SideStrip == null) return;
            var refresh = new UsageRefresh(session.Token, CancelAndDisposeAsync);
            session.UsageRefresh = refresh;
            Enqueue(() =>
            {
                if (session.UsageRefresh == refresh)
                    _readingUsages = ReadUsagesAsync(session, refresh);
                return Task.CompletedTask;
            });
        }
    }

    private async Task ReadUsagesAsync(Session session, UsageRefresh refresh)
    {
        try
        {
            ThrowIfInterrupted(session);
            refresh.Token.ThrowIfCancellationRequested();
            var config = _sources.UsageConfig();
            IReadOnlyList<AiUsage> usages = [];
            if (config.Enabled)
            {
                usages = await InUsageRefreshAsync(session, refresh, _sources.ReadUsagesAsync(refresh.Token)).ConfigureAwait(false);
                config = _sources.UsageConfig();
            }
            var snapshot = new UsageConfig { Enabled = config.Enabled, AlertThreshold = config.AlertThreshold };
            Enqueue(() => CompleteUsageRefreshAsync(session, refresh, usages, snapshot));
        }
        catch (Exception ex)
        {
            Enqueue(() =>
            {
                try
                {
                    if (IsCurrentUsageRefresh(session, refresh) == false) return Task.CompletedTask;
                    var state = ex.GetType();
                    if (_usageFailure == state) return Task.CompletedTask;
                    _usageFailure = state;
                    _log("Stream Dock : quotas indisponibles, écrans conservés.", ex);
                    return Task.CompletedTask;
                }
                finally { FinishUsageRefresh(session, refresh); }
            });
        }
    }

    private async Task<T> InUsageRefreshAsync<T>(Session session, UsageRefresh refresh, Task<T> operation)
    {
        try { return await InSessionAsync(session, operation.WaitAsync(refresh.Token)).ConfigureAwait(false); }
        catch
        {
            if (operation.IsCompleted == false) _ = ObserveAsync(operation);
            throw;
        }
    }

    private bool IsCurrentUsageRefresh(Session session, UsageRefresh refresh) =>
        _session == session && session.UsageRefresh == refresh && session.Interrupted.Task.IsCompleted == false
        && _stopSignal.Task.IsCompleted == false && refresh.Token.IsCancellationRequested == false;

    private Task CompleteUsageRefreshAsync(Session session, UsageRefresh refresh, IReadOnlyList<AiUsage> usages, UsageConfig config)
    {
        try
        {
            if (IsCurrentUsageRefresh(session, refresh) == false) return Task.CompletedTask;
            lock (_gate)
            {
                _sideStrip = _sideStrip with { Usages = config.Enabled ? usages : [], Config = config };
                ++_sideStripRevision;
            }
            _usageFailure = null;
            return RedrawSideStripAsync();
        }
        finally { FinishUsageRefresh(session, refresh); }
    }

    private void FinishUsageRefresh(Session session, UsageRefresh refresh)
    {
        lock (_gate)
        {
            if (session.UsageRefresh == refresh) session.UsageRefresh = null;
        }
        _ = refresh.Cancel.Value;
    }

    private Task SampleLoadAsync(Session session)
    {
        if (IsCurrentSession(session) == false || session.Surface.Description.SideStrip == null) return Task.CompletedTask;
        SystemLoad load;
        try
        {
            load = _load.Sample();
            _loadFailure = null;
        }
        catch (Exception ex)
        {
            if (IsCurrentSession(session) && _loadFailure != ex.GetType())
            {
                _loadFailure = ex.GetType();
                _log("Stream Dock : mesure système indisponible, valeurs précédentes conservées.", ex);
            }
            return Task.CompletedTask;
        }
        lock (_gate)
        {
            if (IsCurrentSession(session) == false) return Task.CompletedTask;
            _sideStrip = _sideStrip with { Load = load };
            ++_sideStripRevision;
        }
        return RedrawSideStripAsync();
    }

    private Task RedrawSideStripAsync()
    {
        var session = _session;
        if (session == null || IsCurrentSession(session) == false) return Task.CompletedTask;
        var size = session.Surface.Description.SideStrip;
        if (size == null) return Task.CompletedTask;
        SideStripState state;
        long revision;
        lock (_gate)
        {
            state = _sideStrip;
            revision = _sideStripRevision;
        }
        var rendering = RenderSideStripAndQueueAsync(session, revision, state, size.Value);
        _renderingSideStrip = _renderingSideStrip.IsCompleted ? rendering : Task.WhenAll(_renderingSideStrip, rendering);
        lock (_gate)
            _pendingSideStripRenders = _pendingSideStripRenders.IsCompleted ? rendering : Task.WhenAll(_pendingSideStripRenders, rendering);
        return Task.CompletedTask;
    }

    private async Task RenderSideStripAndQueueAsync(Session session, long revision, SideStripState state, SurfaceSize size)
    {
        try
        {
            var content = SideStripPlanner.Plan(state.Usages, state.Config, state.Load, _time.GetLocalNow().DateTime);
            var image = await _renderer.RenderSideStripAsync(content, state.Style, size).ConfigureAwait(false);
            if (IsCurrentSideStrip(session, revision))
                Enqueue(() => PublishSideStripAsync(session, revision, image));
        }
        catch (Exception ex)
        {
            Enqueue(() =>
            {
                if (IsCurrentSession(session)) ReportSideStripFailure(ex);
                return Task.CompletedTask;
            });
        }
    }

    private async Task PublishSideStripAsync(Session session, long revision, SurfaceImage image)
    {
        if (IsCurrentSideStrip(session, revision) == false) return;
        if (_publishedSideStrip != null && SameImage(_publishedSideStrip, image))
        {
            _sideStripFailure = null;
            return;
        }
        try
        {
            await InSessionAsync(session, session.Surface.SetSideStripImageAsync(image, session.Token)).ConfigureAwait(false);
            // Une image mise en file peut encore être validée par le prochain appui, même si son rendu est dépassé.
            _publishedSideStrip = null;
            if (IsCurrentSideStrip(session, revision) == false) return;
            await InSessionAsync(session, session.Surface.CommitAsync(session.Token)).ConfigureAwait(false);
            lock (_gate)
            {
                if (IsCurrentSession(session) == false) return;
                _publishedSideStrip = image;
            }
            _sideStripFailure = null;
        }
        catch (Exception ex) when (ex is not IOException)
        {
            _publishedSideStrip = null;
            if (IsCurrentSession(session)) ReportSideStripFailure(ex);
        }
    }

    private bool IsCurrentSession(Session session) =>
        _session == session && session.Interrupted.Task.IsCompleted == false && _stopSignal.Task.IsCompleted == false;

    private bool IsCurrentSideStrip(Session session, long revision)
    {
        lock (_gate) return IsCurrentSession(session) && revision == _sideStripRevision;
    }

    private void ReportSideStripFailure(Exception cause)
    {
        if (_sideStripFailure == cause.GetType()) return;
        _sideStripFailure = cause.GetType();
        _log("Stream Dock : colonne indisponible, nouvelle tentative prévue.", cause);
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
        _publishedSideStrip = null;
        _renderingSideStrip = Task.CompletedTask;
        _loadTimer?.Dispose();
        _loadTimer = null;
        _readingUsages = Task.CompletedTask;
        _usageTimer?.Dispose();
        _usageTimer = null;
        if (session == null) return;
        session.Interrupted.TrySetResult();
        _closing = CloseSurfaceAsync(session);
    }

    private async Task CloseSurfaceAsync(Session session)
    {
        using var budget = new CancellationTokenSource(ShutdownBudget, _time);
        session.Interrupted.TrySetResult();
        _ = session.Cancel.Value;
        var refresh = session.UsageRefresh;
        if (refresh != null) _ = refresh.Cancel.Value;
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
                _usageTimer?.Dispose();
                _loadTimer?.Dispose();
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

    private sealed record SideStripState(IReadOnlyList<AiUsage> Usages, UsageConfig Config, SystemLoad Load, SideStripStyle Style);

    private sealed class UsageRefresh
    {
        public CancellationToken Token { get; }
        public Lazy<Task> Cancel { get; }

        public UsageRefresh(CancellationToken sessionToken, Func<CancellationTokenSource, Task> cancelAndDispose)
        {
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
            Token = cancellation.Token;
            Cancel = new Lazy<Task>(() => cancelAndDispose(cancellation));
        }
    }

    private sealed class Session
    {
        private readonly CancellationTokenSource _cancellation = new();
        public UsageRefresh? UsageRefresh { get; set; }
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
        SurfaceImage[] Pressed);
}
