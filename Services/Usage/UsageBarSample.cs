using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;
using DockPad.Services.StreamDock;

namespace DockPad.Services.Usage;

/// <summary>
/// Barre Usage IA d'exemple des vignettes de style : valeurs fixes, aucun fournisseur réel interrogé, aucune mesure
/// réelle de la charge et aucune écriture dans la configuration.
/// </summary>
public static class UsageBarSample
{
    private static readonly DateTime Now = new(2026, 10, 30, 14, 0, 0);

    /// <summary>Mesures du processeur et de la mémoire, la plus ancienne en premier ; la dernière est celle qu'on lit.</summary>
    private static readonly int[] CpuSeries = [12, 18, 15, 30, 22, 19, 41, 35, 20, 17, 26, 23];
    private static readonly int[] MemorySeries = [58, 58, 59, 60, 60, 59, 61, 62, 61, 60, 61, 61];

    public static UsageBarViewModel Create(SideStripStyle style)
    {
        var config = new UsageConfig
        {
            Enabled = true,
            Placement = UsagePlacement.Right,
            BarStyle = style,
            DefaultProviderId = "claude",
            Providers =
            {
                new AiProviderEntry { Id = "claude", Name = "Claude", Detected = true, Order = 0 },
                new AiProviderEntry { Id = "codex", Name = "Codex", Detected = true, Order = 1 },
                new AiProviderEntry { Id = "antigravity", Name = "Antigravity", Detected = true, Order = 2 },
            },
        };

        IUsageProvider[] providers =
        [
            new SampleProvider("claude", "Claude", ProviderLogos.ClaudeAccent, Window(62), Window(44)),
            new SampleProvider("codex", "Codex", "#10A37F", null, Window(27)),
            new SampleProvider("antigravity", "Antigravity", ProviderLogos.AntigravityAccent, Window(88), Window(18)),
        ];

        var usage = new UsageViewModel(new UsageService(providers), () => config, () => Now);
        // Les relevés d'exemple sont déjà terminés : la lecture se termine sur place, sans attente.
        usage.RefreshAsync().GetAwaiter().GetResult();

        return new UsageBarViewModel(usage, FilledLoadFeed(), () => Now);
    }

    private static UsageWindow Window(int usedPct) => new() { UsedPct = usedPct, ResetsAt = Now.AddDays(1) };

    /// <summary>
    /// Flux de charge rempli de ses douze mesures fixes : son minuteur est tiré à la main, jamais par l'horloge.
    /// </summary>
    private static SystemLoadFeed FilledLoadFeed()
    {
        var time = new HandFiredTimeProvider();
        var feed = new SystemLoadFeed(new SampleLoadMonitor(), time, publishOn: null);
        feed.Start();
        feed.PendingSample.Wait();

        for (var count = 1; count < CpuSeries.Length; count++)
        {
            time.Fire();
            feed.PendingSample.Wait();
        }

        return feed;
    }

    /// <summary>
    /// Un fournisseur d'exemple : ni la démonstration, donc éligible, ni aucun accès au disque ni au réseau.
    /// </summary>
    private sealed class SampleProvider(string id, string name, string accent, UsageWindow? session, UsageWindow? week)
        : IUsageProvider
    {
        public string Id => id;
        public string Name => name;

        public AiProbe Probe() => new() { Available = true, DisplayName = name, Glyph = name[..1], AccentColor = accent };

        public Task<AiUsage?> ReadAsync(CancellationToken ct) => Task.FromResult<AiUsage?>(new AiUsage
        {
            ProviderId = id, Name = name, Glyph = name[..1], AccentColor = accent, Session = session, Week = week,
        });
    }

    /// <summary>
    /// Rend les mesures d'exemple dans l'ordre, puis la dernière.
    /// </summary>
    private sealed class SampleLoadMonitor : ISystemLoadMonitor
    {
        private int _next;

        public SystemLoad Sample()
        {
            var at = Math.Min(_next++, CpuSeries.Length - 1);
            return new SystemLoad(CpuSeries[at], MemorySeries[at]);
        }

        public void Reset() => _next = 0;
    }

    private sealed class HandFiredTimeProvider : TimeProvider
    {
        private HandFiredTimer? _timer;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            _timer = new HandFiredTimer(callback, state);

        public void Fire() => _timer?.Fire();

        private sealed class HandFiredTimer(TimerCallback callback, object? state) : ITimer
        {
            public void Fire() => callback(state);
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
