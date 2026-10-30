using System.Collections.Concurrent;
using DockPad.Services.StreamDock;
using DockPad.Services.Usage;

namespace DockPad.Tests;

/// <summary>
/// Flux de charge de la fenêtre principale : cadence de 5 s, historique de 12 points, arrêt avec remise à zéro,
/// et gardes contre une mesure, un rappel de minuteur ou une notification qui survivent à un arrêt.
/// Une attente qui expire fait échouer le test ; aucune mesure bloquée ne se libère seule.
/// </summary>
public class SystemLoadFeedTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly SystemLoad Unknown = new(null, null);

    /// <summary>
    /// Moniteur dont une mesure peut rester bloquée, avant d'atteindre le moniteur interne, jusqu'à Unblock.
    /// </summary>
    private sealed class BlockingSystemLoadMonitor(ISystemLoadMonitor? inner = null) : ISystemLoadMonitor
    {
        private readonly ManualResetEventSlim _released = new(false);
        private int _samples;
        private int _resets;

        public SystemLoad Value { get; set; } = new(23, 61);
        public bool BlocksNextSample { get; set; }
        public SemaphoreSlim Entered { get; } = new(0);
        public int Samples => Volatile.Read(ref _samples);
        public int Resets => Volatile.Read(ref _resets);
        public int? SampleThreadId { get; private set; }

        public SystemLoad Sample()
        {
            Interlocked.Increment(ref _samples);
            SampleThreadId = Environment.CurrentManagedThreadId;
            var value = Value;
            if (BlocksNextSample)
            {
                BlocksNextSample = false;
                Entered.Release();
                _released.Wait();
            }
            return inner?.Sample() ?? value;
        }

        public void Reset()
        {
            Interlocked.Increment(ref _resets);
            inner?.Reset();
        }

        public void Unblock() => _released.Set();
    }

    /// <summary>
    /// Contexte qui garde les publications en file jusqu'à RunAll ; le premier Post peut rester bloqué.
    /// </summary>
    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _posts = new();
        private readonly ManualResetEventSlim _firstPostReleased = new(false);
        private int _postCount;

        public bool BlocksFirstPost { get; init; }
        public SemaphoreSlim FirstPostEntered { get; } = new(0);

        public int Pending
        {
            get { lock (_posts) return _posts.Count; }
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            if (BlocksFirstPost && Interlocked.Increment(ref _postCount) == 1)
            {
                FirstPostEntered.Release();
                _firstPostReleased.Wait();
            }
            lock (_posts)
                _posts.Enqueue((d, state));
        }

        public void ReleaseFirstPost() => _firstPostReleased.Set();

        public void RunAll()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) post;
                lock (_posts)
                {
                    if (_posts.Count == 0)
                        return;
                    post = _posts.Dequeue();
                }
                post.Callback(post.State);
            }
        }
    }

    /// <summary>
    /// Horloge qui ne déclenche rien seule : le test appelle les rappels qu'elle a reçus, même après Dispose.
    /// </summary>
    private sealed class CapturingTimeProvider : TimeProvider
    {
        public List<TimerCallback> Callbacks { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Callbacks.Add(callback);
            return new IdleTimer();
        }

        private sealed class IdleTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static async Task AdvanceAndWait(SystemLoadFeed feed, ManualTimeProvider time, TimeSpan delta)
    {
        time.Advance(delta);
        await feed.PendingSample.WaitAsync(Patience);
    }

    /// <summary>
    /// Moniteur réel dont le processeur est occupé à 50 % entre deux lectures, et la mémoire à 40 %.
    /// </summary>
    private static SystemLoadMonitor HalfBusyMonitor()
    {
        ulong elapsed = 0;
        return new SystemLoadMonitor(
            readCpu: () =>
            {
                elapsed += 1_000;
                return new CpuTimes(Idle: elapsed / 2, Kernel: elapsed, User: 0);
            },
            readMemory: () => 40);
    }

    [Fact]
    public async Task Charge_MesureToutesLes5s()
    {
        var monitor = new FakeSystemLoadMonitor();
        var time = new ManualTimeProvider();
        var feed = new SystemLoadFeed(monitor, time, publishOn: null);

        feed.Start();
        await feed.PendingSample.WaitAsync(Patience);
        Assert.Equal(1, monitor.Samples);

        await AdvanceAndWait(feed, time, TimeSpan.FromSeconds(4.9));
        Assert.Equal(1, monitor.Samples);

        await AdvanceAndWait(feed, time, TimeSpan.FromSeconds(0.1));
        Assert.Equal(2, monitor.Samples);

        await AdvanceAndWait(feed, time, Interval);
        Assert.Equal(3, monitor.Samples);
        Assert.Equal(3, feed.History.Count);
        Assert.Equal(monitor.Value, feed.Current);
    }

    [Fact]
    public async Task Charge_MesureHorsDuThreadAppelant()
    {
        var monitor = new BlockingSystemLoadMonitor();
        var time = new ManualTimeProvider();
        var feed = new SystemLoadFeed(monitor, time, publishOn: null);
        var caller = Environment.CurrentManagedThreadId;

        feed.Start();
        await feed.PendingSample.WaitAsync(Patience);
        Assert.NotEqual(caller, monitor.SampleThreadId);

        await AdvanceAndWait(feed, time, Interval);
        Assert.Equal(2, monitor.Samples);
        Assert.NotEqual(caller, monitor.SampleThreadId);
    }

    [Fact]
    public async Task Charge_HistoriquePlafonneA12()
    {
        var monitor = new FakeSystemLoadMonitor { Value = new(0, 50) };
        var time = new ManualTimeProvider();
        var feed = new SystemLoadFeed(monitor, time, publishOn: null);

        feed.Start();
        await feed.PendingSample.WaitAsync(Patience);
        for (var cpu = 1; cpu < 15; cpu++)
        {
            monitor.Value = new(cpu, 50);
            await AdvanceAndWait(feed, time, Interval);
        }

        Assert.Equal(12, feed.History.Count);
        Assert.Equal(3, feed.History[0].CpuPct);
        Assert.Equal(14, feed.History[^1].CpuPct);
        Assert.Equal(new SystemLoad(14, 50), feed.Current);
    }

    [Fact]
    public async Task Charge_Arret_ResetEtHistoriqueVide()
    {
        var monitor = new FakeSystemLoadMonitor();
        var time = new ManualTimeProvider();
        var feed = new SystemLoadFeed(monitor, time, publishOn: null);
        feed.Start();
        await feed.PendingSample.WaitAsync(Patience);
        await AdvanceAndWait(feed, time, Interval);

        feed.Stop();
        await AdvanceAndWait(feed, time, Interval * 3);

        Assert.Equal(1, monitor.Resets);
        Assert.Empty(feed.History);
        Assert.Equal(Unknown, feed.Current);
        Assert.Equal(2, monitor.Samples);
    }

    [Fact]
    public async Task Charge_Reprise_ProcesseurInconnuAuPremierTic()
    {
        var time = new ManualTimeProvider();
        var feed = new SystemLoadFeed(HalfBusyMonitor(), time, publishOn: null);
        feed.Start();
        await feed.PendingSample.WaitAsync(Patience);
        await AdvanceAndWait(feed, time, Interval);
        Assert.Equal(new SystemLoad(50, 40), feed.Current);

        feed.Stop();
        feed.Start();
        await feed.PendingSample.WaitAsync(Patience);

        Assert.Equal([new SystemLoad(null, 40)], feed.History);
    }

    /// <summary>
    /// Une mesure partie avant l'arrêt mais qui n'atteint le moniteur qu'après ne doit pas réamorcer
    /// le processeur : la reprise repart d'un processeur inconnu.
    /// </summary>
    [Fact]
    public async Task Charge_MesureAncienneApresArret_NeReamorcePasLeProcesseur()
    {
        var monitor = new BlockingSystemLoadMonitor(HalfBusyMonitor());
        var time = new ManualTimeProvider();
        var feed = new SystemLoadFeed(monitor, time, publishOn: null);
        try
        {
            feed.Start();
            await feed.PendingSample.WaitAsync(Patience);
            monitor.BlocksNextSample = true;
            time.Advance(Interval);
            Assert.True(await monitor.Entered.WaitAsync(Patience));
            var inFlight = feed.PendingSample;

            feed.Stop();
            monitor.Unblock();
            await inFlight.WaitAsync(Patience);
            await feed.PendingSample.WaitAsync(Patience);
            feed.Start();
            await feed.PendingSample.WaitAsync(Patience);

            Assert.Equal([new SystemLoad(null, 40)], feed.History);
        }
        finally
        {
            monitor.Unblock();
        }
    }

    [Fact]
    public async Task Charge_MesureEnVolPendantArret_RienPublie()
    {
        var monitor = new BlockingSystemLoadMonitor();
        var time = new ManualTimeProvider();
        var feed = new SystemLoadFeed(monitor, time, publishOn: null);
        try
        {
            feed.Start();
            await feed.PendingSample.WaitAsync(Patience);

            monitor.BlocksNextSample = true;
            monitor.Value = new(99, 99);
            time.Advance(Interval);
            Assert.True(await monitor.Entered.WaitAsync(Patience));
            var inFlight = feed.PendingSample;

            feed.Stop();
            var changes = 0;
            feed.Changed += () => changes++;
            monitor.Unblock();
            await inFlight.WaitAsync(Patience);
            await feed.PendingSample.WaitAsync(Patience);

            Assert.Equal(0, changes);
            Assert.Empty(feed.History);
            Assert.Equal(Unknown, feed.Current);
        }
        finally
        {
            monitor.Unblock();
        }
    }

    [Fact]
    public async Task Charge_ArretPuisRedemarrage_AncienneMesureIgnoree()
    {
        var stale = new SystemLoad(99, 99);
        var fresh = new SystemLoad(12, 34);
        var monitor = new BlockingSystemLoadMonitor { Value = stale, BlocksNextSample = true };
        var time = new ManualTimeProvider();
        var feed = new SystemLoadFeed(monitor, time, publishOn: null);
        try
        {
            feed.Start();
            Assert.True(await monitor.Entered.WaitAsync(Patience));
            var inFlight = feed.PendingSample;

            feed.Stop();
            monitor.Value = fresh;
            var changes = 0;
            feed.Changed += () => changes++;
            feed.Start();
            monitor.Unblock();
            await inFlight.WaitAsync(Patience);
            await feed.PendingSample.WaitAsync(Patience);

            Assert.Equal(1, changes);
            Assert.Equal([fresh], feed.History);
            Assert.Equal(fresh, feed.Current);
        }
        finally
        {
            monitor.Unblock();
        }
    }

    /// <summary>
    /// Un rappel de l'ancien minuteur, exécuté après un redémarrage, ne lance pas de mesure en plus.
    /// </summary>
    [Fact]
    public async Task Charge_RappelDeLAncienMinuteur_Ignore()
    {
        var monitor = new FakeSystemLoadMonitor();
        var time = new CapturingTimeProvider();
        var feed = new SystemLoadFeed(monitor, time, publishOn: null);
        feed.Start();
        await feed.PendingSample.WaitAsync(Patience);
        feed.Stop();
        feed.Start();
        await feed.PendingSample.WaitAsync(Patience);
        Assert.Equal(2, monitor.Samples);

        time.Callbacks[0](null);
        await feed.PendingSample.WaitAsync(Patience);
        Assert.Equal(2, monitor.Samples);

        time.Callbacks[1](null);
        await feed.PendingSample.WaitAsync(Patience);
        Assert.Equal(3, monitor.Samples);
    }

    /// <summary>
    /// Une mesure mise en file puis rendue périmée par un arrêt avant son tour n'appelle jamais le moniteur.
    /// </summary>
    [Fact]
    public async Task Charge_MesureEnFileAvantArret_NAtteintPasLeMoniteur()
    {
        var monitor = new BlockingSystemLoadMonitor { BlocksNextSample = true };
        var time = new ManualTimeProvider();
        var feed = new SystemLoadFeed(monitor, time, publishOn: null);
        try
        {
            feed.Start();
            Assert.True(await monitor.Entered.WaitAsync(Patience));
            feed.Stop();
            feed.Start();
            feed.Stop();

            monitor.Unblock();
            await feed.PendingSample.WaitAsync(Patience);

            Assert.Equal(1, monitor.Samples);
            Assert.Equal(2, monitor.Resets);
            Assert.Empty(feed.History);
        }
        finally
        {
            monitor.Unblock();
        }
    }

    /// <summary>
    /// Une mesure ralentie avant sa publication ne doit pas laisser la suivante publier avant elle.
    /// </summary>
    [Fact]
    public async Task Charge_MesuresPublieesDansLOrdre()
    {
        var monitor = new FakeSystemLoadMonitor { Value = new(1, 50) };
        var time = new ManualTimeProvider();
        var dispatcher = new QueuedSynchronizationContext { BlocksFirstPost = true };
        var feed = new SystemLoadFeed(monitor, time, dispatcher);
        try
        {
            feed.Start();
            Assert.True(await dispatcher.FirstPostEntered.WaitAsync(Patience));
            var first = feed.PendingSample;

            monitor.Value = new(2, 50);
            time.Advance(Interval);
            var afterTick = feed.PendingSample;
            if (afterTick != first)
                await afterTick.WaitAsync(Patience);
            dispatcher.ReleaseFirstPost();
            await first.WaitAsync(Patience);
            await feed.PendingSample.WaitAsync(Patience);
            dispatcher.RunAll();

            Assert.Equal(feed.History.OrderBy(load => load.CpuPct), feed.History);
            Assert.Equal(feed.History[^1], feed.Current);
        }
        finally
        {
            dispatcher.ReleaseFirstPost();
        }
    }

    /// <summary>
    /// Une notification en cours quand Stop est appelé se termine avant que Stop rende la main.
    /// Le court délai laisse seulement à un Stop non coordonné le temps de finir avant la notification.
    /// </summary>
    [Fact]
    public async Task Charge_ChangedJamaisApresLeRetourDeStop()
    {
        var monitor = new FakeSystemLoadMonitor();
        var time = new ManualTimeProvider();
        var feed = new SystemLoadFeed(monitor, time, publishOn: null);
        var events = new ConcurrentQueue<string>();
        using var handlerEntered = new SemaphoreSlim(0);
        using var handlerRelease = new ManualResetEventSlim(false);
        var isFirstHandlerCall = true;
        feed.Changed += () =>
        {
            if (isFirstHandlerCall == false)
                return;
            isFirstHandlerCall = false;
            handlerEntered.Release();
            handlerRelease.Wait();
        };
        feed.Changed += () => events.Enqueue("changed");
        try
        {
            feed.Start();
            Assert.True(await handlerEntered.WaitAsync(Patience));
            var stopping = Task.Run(() =>
            {
                feed.Stop();
                events.Enqueue("stopped");
            });
            await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromMilliseconds(200)));
            handlerRelease.Set();
            await stopping.WaitAsync(Patience);
            await feed.PendingSample.WaitAsync(Patience);

            Assert.Equal(["changed", "stopped"], events);
        }
        finally
        {
            handlerRelease.Set();
        }
    }

    [Fact]
    public async Task Charge_ProcesseurInconnu_PointConserveAvecNull()
    {
        var monitor = new FakeSystemLoadMonitor { Value = new(null, 60) };
        var time = new ManualTimeProvider();
        var feed = new SystemLoadFeed(monitor, time, publishOn: null);

        feed.Start();
        await feed.PendingSample.WaitAsync(Patience);
        monitor.Value = new(40, 60);
        await AdvanceAndWait(feed, time, Interval);
        monitor.Value = new(null, 61);
        await AdvanceAndWait(feed, time, Interval);

        Assert.Equal([new SystemLoad(null, 60), new SystemLoad(40, 60), new SystemLoad(null, 61)], feed.History);
        Assert.Equal(new SystemLoad(null, 61), feed.Current);
    }

    /// <summary>
    /// La mesure ne publie qu'à l'exécution du message posté, et un arrêt survenu entre l'envoi et l'exécution
    /// l'annule encore.
    /// </summary>
    [Fact]
    public async Task Charge_PublicationParLeContexteFourni()
    {
        var monitor = new FakeSystemLoadMonitor();
        var time = new ManualTimeProvider();
        var dispatcher = new QueuedSynchronizationContext();
        var feed = new SystemLoadFeed(monitor, time, dispatcher);
        var changes = 0;
        feed.Changed += () => changes++;

        feed.Start();
        await feed.PendingSample.WaitAsync(Patience);
        Assert.Equal(1, dispatcher.Pending);
        Assert.Empty(feed.History);

        dispatcher.RunAll();
        Assert.Equal(1, changes);
        Assert.Equal([monitor.Value], feed.History);

        await AdvanceAndWait(feed, time, Interval);
        feed.Stop();
        dispatcher.RunAll();
        Assert.Equal(1, changes);
        Assert.Empty(feed.History);
    }
}
