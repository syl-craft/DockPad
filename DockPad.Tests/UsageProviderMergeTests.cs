using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;
using DockPad.Services.Usage;

namespace DockPad.Tests;

public class UsageProviderMergeTests
{
    /// <summary>Un provider minimal dont la sonde est dictée par le test.</summary>
    private sealed class FakeProvider(string id, string name, AiProbe probe) : IUsageProvider
    {
        public string Id => id;
        public string Name => name;
        public AiProbe Probe() => probe;
        public Task<AiUsage?> ReadAsync(CancellationToken ct) => Task.FromResult<AiUsage?>(null);
    }

    private static (IUsageProvider, AiProbe) Probe(string id, string displayName,
                                                   bool hiddenByDefault = false, string dataPath = "")
    {
        var probe = new AiProbe
        {
            Available = true, DisplayName = displayName,
            HiddenByDefault = hiddenByDefault, DataPath = dataPath,
        };
        return (new FakeProvider(id, displayName, probe), probe);
    }

    [Fact]
    public void Merge_NouveauFournisseur_AjouteEnFinDeListe()
    {
        var existing = new List<AiProviderEntry>
        {
            new() { Id = "claude", Name = "Claude Code", DetectedName = "Claude Code", Order = 0 },
        };

        var merged = AiDetectionService.Merge(existing, [Probe("demo", "Démo")]);

        var demo = merged.Single(p => p.Id == "demo");
        Assert.Equal(1, demo.Order);
        Assert.Equal("Démo", demo.Name);
        Assert.True(demo.Detected);
    }

    [Fact]
    public void Merge_NouveauFournisseurMasqueParDefaut_EstCreeMasque()
    {
        var merged = AiDetectionService.Merge([], [Probe("demo", "Démo", hiddenByDefault: true)]);

        Assert.True(merged.Single().Hidden);
    }

    [Fact]
    public void Merge_MasqueParDefautMaisDejaAffiche_ResteAffiche()
    {
        // HiddenByDefault n'agit qu'à la découverte : une redétection ne doit pas défaire un choix.
        var existing = new List<AiProviderEntry>
        {
            new() { Id = "demo", Name = "Démo", DetectedName = "Démo", Hidden = false, Order = 0 },
        };

        var merged = AiDetectionService.Merge(existing, [Probe("demo", "Démo", hiddenByDefault: true)]);

        Assert.False(merged.Single().Hidden);
    }

    [Fact]
    public void Merge_MasquageEtOrdre_Preserves()
    {
        var existing = new List<AiProviderEntry>
        {
            new() { Id = "claude", Name = "Claude Code", DetectedName = "Claude Code", Hidden = true, Order = 7 },
        };

        var entry = AiDetectionService.Merge(existing, [Probe("claude", "Claude Code")]).Single();

        Assert.True(entry.Hidden);
        Assert.Equal(7, entry.Order);
    }

    [Fact]
    public void Merge_NomPersonnalise_Preserve()
    {
        var existing = new List<AiProviderEntry>
        {
            new() { Id = "claude", Name = "Mon Claude", DetectedName = "Claude Code", Order = 0 },
        };

        var entry = AiDetectionService.Merge(existing, [Probe("claude", "Claude Code 2")]).Single();

        Assert.Equal("Mon Claude", entry.Name);
        Assert.Equal("Claude Code 2", entry.DetectedName);   // la trace du nom détecté suit
    }

    [Fact]
    public void Merge_NomNonPersonnalise_SuitLeNomDetecte()
    {
        var existing = new List<AiProviderEntry>
        {
            new() { Id = "claude", Name = "Claude Code", DetectedName = "Claude Code", Order = 0 },
        };

        var entry = AiDetectionService.Merge(existing, [Probe("claude", "Claude Code 2")]).Single();

        Assert.Equal("Claude Code 2", entry.Name);
    }

    [Fact]
    public void Merge_FournisseurDisparu_ConserveMaisNonDetecte()
    {
        // Le supprimer détruirait son masquage et son ordre pour une absence peut-être temporaire.
        var existing = new List<AiProviderEntry>
        {
            new() { Id = "claude", Name = "Mon Claude", DetectedName = "Claude Code",
                    Hidden = true, Order = 3, Detected = true },
        };

        var merged = AiDetectionService.Merge(existing, [Probe("demo", "Démo")]);

        var claude = merged.Single(p => p.Id == "claude");
        Assert.False(claude.Detected);
        Assert.True(claude.Hidden);
        Assert.Equal(3, claude.Order);
        Assert.Equal("Mon Claude", claude.Name);
    }

    [Fact]
    public void Merge_SondeNonDisponible_ConserveEnNonDetecte()
    {
        var existing = new List<AiProviderEntry>
        {
            new() { Id = "claude", Name = "Claude Code", DetectedName = "Claude Code", Order = 0, Detected = true },
        };
        var provider = new FakeProvider("claude", "Claude Code",
            new AiProbe { Available = false, DisplayName = "Claude Code", Detail = "non installé" });

        var entry = AiDetectionService.Merge(existing, [(provider, provider.Probe())]).Single();

        Assert.False(entry.Detected);
    }

    [Fact]
    public void Merge_CheminDeDonnees_MisAJour()
    {
        var existing = new List<AiProviderEntry>
        {
            new() { Id = "claude", Name = "Claude Code", DetectedName = "Claude Code", DataPath = @"C:\vieux" },
        };

        var entry = AiDetectionService.Merge(existing, [Probe("claude", "Claude Code", dataPath: @"C:\neuf")]).Single();

        Assert.Equal(@"C:\neuf", entry.DataPath);
    }

    [Fact]
    public void Merge_EntreeInconnueDuRegistre_ConserveeTelleQuelle()
    {
        // Retour arrière de version : le fournisseur d'une version plus récente ne doit pas
        // disparaître du fichier, sinon son masquage et son ordre sont perdus.
        var existing = new List<AiProviderEntry>
        {
            new() { Id = "codex", Name = "Codex", DetectedName = "Codex", Hidden = true, Order = 5 },
        };

        var merged = AiDetectionService.Merge(existing, [Probe("claude", "Claude Code")]);

        var codex = merged.Single(p => p.Id == "codex");
        Assert.True(codex.Hidden);
        Assert.Equal(5, codex.Order);
    }

    [Fact]
    public void Merge_ResultatTrieParOrdre()
    {
        var existing = new List<AiProviderEntry>
        {
            new() { Id = "b", Name = "B", DetectedName = "B", Order = 2 },
            new() { Id = "a", Name = "A", DetectedName = "A", Order = 1 },
        };

        var merged = AiDetectionService.Merge(existing, []);

        Assert.Equal(["a", "b"], merged.Select(p => p.Id));
    }

    [Fact]
    public void Detect_SondeQuiLeve_FournisseurNonDisponibleSansPlantage()
    {
        var config = new UsageConfig();

        var merged = AiDetectionService.Detect([new ThrowingProvider()], config);

        var entry = Assert.Single(merged.Providers);
        Assert.Equal("boom", entry.Id);
        Assert.False(entry.Detected);
    }

    private sealed class ThrowingProvider : IUsageProvider
    {
        public string Id => "boom";
        public string Name => "Boom";
        public AiProbe Probe() => throw new InvalidOperationException("sonde cassée");
        public Task<AiUsage?> ReadAsync(CancellationToken ct) => Task.FromResult<AiUsage?>(null);
    }

    [Fact]
    public void Detect_ConserveLesAutresReglages()
    {
        var config = new UsageConfig { AlertThreshold = 40, ShowCost = false, DefaultProviderId = "claude" };

        var result = AiDetectionService.Detect([], config);

        Assert.Equal(40, result.AlertThreshold);
        Assert.False(result.ShowCost);
        Assert.Equal("claude", result.DefaultProviderId);
    }
}
