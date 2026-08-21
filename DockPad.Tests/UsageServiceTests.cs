using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;
using DockPad.Services.Usage;

namespace DockPad.Tests;

public class UsageServiceTests
{
    /// <summary>Provider espion : compte ses lectures, et peut renvoyer null, lever, ou traîner.</summary>
    private sealed class SpyProvider(string id, AiUsage? usage = null,
                                     bool throws = false, int delayMs = 0) : IUsageProvider
    {
        public int ReadCount { get; private set; }

        public string Id => id;
        public string Name => id;
        public AiProbe Probe() => new() { Available = true, DisplayName = id };

        public async Task<AiUsage?> ReadAsync(CancellationToken ct)
        {
            ReadCount++;
            if (delayMs > 0) await Task.Delay(delayMs, ct);
            if (throws) throw new InvalidOperationException("lecture cassée");
            return usage;
        }
    }

    private static AiUsage Usage(string id, long day = 100) => new()
    {
        ProviderId = id, Name = id, Glyph = "X", AccentColor = "#000", DayTokens = day,
    };

    private static UsageConfig Config(params (string Id, bool Hidden, int Order)[] providers)
    {
        var config = new UsageConfig();
        foreach (var (id, hidden, order) in providers)
        {
            config.Providers.Add(new AiProviderEntry { Id = id, Name = id, Hidden = hidden, Order = order });
        }
        return config;
    }

    [Fact]
    public async Task RefreshAsync_AucunProvider_RetourneVide()
    {
        var service = new UsageService([]);

        Assert.Empty(await service.RefreshAsync(new UsageConfig(), CancellationToken.None));
    }

    [Fact]
    public async Task RefreshAsync_ProviderMasque_NEstPasInterroge()
    {
        // Le masquage doit couper la lecture, pas seulement l'affichage : lire un provider masqué,
        // c'est du disque et du réseau dépensés pour rien.
        var spy = new SpyProvider("demo", Usage("demo"));
        var service = new UsageService([spy]);

        var result = await service.RefreshAsync(Config(("demo", true, 0)), CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(0, spy.ReadCount);
    }

    [Fact]
    public async Task RefreshAsync_ProviderAbsentDeLaConfig_EstInterroge()
    {
        // Première exécution : la config est vide, tout doit s'afficher malgré tout.
        var spy = new SpyProvider("claude", Usage("claude"));
        var service = new UsageService([spy]);

        Assert.Single(await service.RefreshAsync(new UsageConfig(), CancellationToken.None));
    }

    [Fact]
    public async Task RefreshAsync_ProviderQuiLeve_EstAbsentMaisLesAutresRestent()
    {
        var service = new UsageService([
            new SpyProvider("casse", throws: true),
            new SpyProvider("ok", Usage("ok")),
        ]);

        var result = await service.RefreshAsync(
            Config(("casse", false, 0), ("ok", false, 1)), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("ok", result[0].ProviderId);
    }

    [Fact]
    public async Task RefreshAsync_ProviderQuiRenvoieNull_EstAbsent()
    {
        var service = new UsageService([new SpyProvider("vide"), new SpyProvider("ok", Usage("ok"))]);

        var result = await service.RefreshAsync(
            Config(("vide", false, 0), ("ok", false, 1)), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("ok", result[0].ProviderId);
    }

    [Fact]
    public async Task RefreshAsync_ResultatOrdonneSelonLaConfig()
    {
        var service = new UsageService([new SpyProvider("a", Usage("a")), new SpyProvider("b", Usage("b"))]);

        var result = await service.RefreshAsync(
            Config(("a", false, 5), ("b", false, 1)), CancellationToken.None);

        Assert.Equal(["b", "a"], result.Select(u => u.ProviderId));
    }

    [Fact]
    public async Task RefreshAsync_ProvidersInterrogesEnParallele()
    {
        // Deux lectures de 200 ms en parallèle tiennent largement sous 400 ms.
        var service = new UsageService([
            new SpyProvider("a", Usage("a"), delayMs: 200),
            new SpyProvider("b", Usage("b"), delayMs: 200),
        ]);

        var start = Environment.TickCount64;
        var result = await service.RefreshAsync(
            Config(("a", false, 0), ("b", false, 1)), CancellationToken.None);
        var elapsed = Environment.TickCount64 - start;

        Assert.Equal(2, result.Count);
        Assert.True(elapsed < 400, $"lectures apparemment séquentielles ({elapsed} ms)");
    }

    [Fact]
    public async Task RefreshAsync_Annulation_LeveOperationCanceled()
    {
        var service = new UsageService([new SpyProvider("lent", Usage("lent"), delayMs: 5_000)]);
        using var cts = new CancellationTokenSource();

        var task = service.RefreshAsync(Config(("lent", false, 0)), cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
