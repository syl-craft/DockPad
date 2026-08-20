using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;
using DockPad.Services.Usage;

namespace DockPad.Tests;

public class UsageViewModelTests
{
    private sealed class StubProvider(AiUsage? usage) : IUsageProvider
    {
        public string Id => usage?.ProviderId ?? "vide";
        public string Name => Id;
        public AiProbe Probe() => new() { Available = true, DisplayName = Id };
        public Task<AiUsage?> ReadAsync(CancellationToken ct) => Task.FromResult(usage);
    }

    private static AiUsage Usage(string id, string name = "", bool demo = false,
                                 long session = 12_400, long day = 86_000, long month = 1_200_000,
                                 int requests = 47, string cost = "$3.80", string model = "claude-opus-5",
                                 int? sessionUsed = 62, int? weekUsed = 44) => new()
    {
        ProviderId = id,
        Name = name.Length > 0 ? name : id,
        Glyph = "X",
        AccentColor = "#123456",
        Model = model,
        Cost = cost,
        SessionTokens = session,
        DayTokens = day,
        MonthTokens = month,
        Requests = requests,
        Session = sessionUsed is null ? null : new UsageWindow { UsedPct = sessionUsed.Value },
        Week = weekUsed is null ? null : new UsageWindow { UsedPct = weekUsed.Value },
        IsDemo = demo,
    };

    private static UsageViewModel Build(UsageConfig config, params AiUsage?[] usages)
    {
        var providers = usages.Select(u => (IUsageProvider)new StubProvider(u)).ToList();
        var service = new UsageService(providers);
        return new UsageViewModel(service, () => config, () => new DateTime(2026, 8, 20, 12, 0, 0));
    }

    private static UsageConfig ConfigFor(params (string Id, bool Hidden)[] providers)
    {
        var config = new UsageConfig();
        var order = 0;
        foreach (var (id, hidden) in providers)
        {
            config.Providers.Add(new AiProviderEntry { Id = id, Name = id, Hidden = hidden, Order = order++ });
        }
        return config;
    }

    // --- Métriques

    [Fact]
    public async Task Metrics_AvecCout_SixColonnesEtCoutEnCinquiemePosition()
    {
        var vm = Build(ConfigFor(("a", false)), Usage("a"));
        await vm.RefreshAsync();

        Assert.Equal(6, vm.Metrics.Count);
        Assert.Equal("Coût est.", vm.Metrics[4].Label);
        Assert.Equal("$3.80", vm.Metrics[4].Value);
        Assert.Equal("Modèle", vm.Metrics[5].Label);
    }

    [Fact]
    public async Task Metrics_SansCout_CinqColonnes()
    {
        var config = ConfigFor(("a", false));
        config.ShowCost = false;
        var vm = Build(config, Usage("a"));
        await vm.RefreshAsync();

        Assert.Equal(5, vm.Metrics.Count);
        Assert.DoesNotContain(vm.Metrics, m => m.Label.StartsWith("Coût"));
        Assert.Equal("Modèle", vm.Metrics[4].Label);
    }

    [Fact]
    public async Task Metrics_SessionNulle_AfficheTiretEtNonZero()
    {
        // Zéro jeton de session veut dire « aucun bloc actif » : une absence de mesure.
        var vm = Build(ConfigFor(("a", false)), Usage("a", session: 0));
        await vm.RefreshAsync();

        Assert.Equal(UsageViewModel.Unknown, vm.Metrics[0].Value);
    }

    [Fact]
    public async Task Metrics_CoutEtModeleVides_AffichentTiret()
    {
        var vm = Build(ConfigFor(("a", false)), Usage("a", cost: "", model: ""));
        await vm.RefreshAsync();

        Assert.Equal(UsageViewModel.Unknown, vm.Metrics[4].Value);
        Assert.Equal(UsageViewModel.Unknown, vm.Metrics[5].Value);
    }

    // --- Onglets

    [Fact]
    public async Task ShowTabs_UnSeulFournisseur_PasDOnglets()
    {
        var vm = Build(ConfigFor(("a", false)), Usage("a", name: "Claude Code"));
        await vm.RefreshAsync();

        Assert.False(vm.ShowTabs);
        Assert.Empty(vm.Tabs);
        Assert.Equal("Claude Code", vm.SoloName);
    }

    [Fact]
    public async Task ShowTabs_DeuxFournisseurs_OngletsPresents()
    {
        var vm = Build(ConfigFor(("a", false), ("b", false)), Usage("a"), Usage("b"));
        await vm.RefreshAsync();

        Assert.True(vm.ShowTabs);
        Assert.Equal(2, vm.Tabs.Count);
        Assert.True(vm.Tabs[0].IsSelected);
        Assert.False(vm.Tabs[1].IsSelected);
    }

    [Fact]
    public async Task ShowTabs_FournisseurMasque_AbsentDesOnglets()
    {
        var vm = Build(ConfigFor(("a", false), ("b", true)), Usage("a"), Usage("b"));
        await vm.RefreshAsync();

        Assert.False(vm.ShowTabs);
        Assert.DoesNotContain(vm.Tabs, t => t.ProviderId == "b");
    }

    // --- Sélection

    [Fact]
    public async Task Select_ChangeLesMetriquesEtLesJauges()
    {
        var vm = Build(ConfigFor(("a", false), ("b", false)),
                       Usage("a", day: 1_000, sessionUsed: 10),
                       Usage("b", day: 2_000, sessionUsed: 90));
        await vm.RefreshAsync();

        vm.Select("b");

        Assert.Equal("2k", vm.Metrics[1].Value);
        Assert.Equal(90, vm.SessionGauge!.UsedPct);
        Assert.True(vm.Tabs.Single(t => t.ProviderId == "b").IsSelected);
        Assert.False(vm.Tabs.Single(t => t.ProviderId == "a").IsSelected);
    }

    [Fact]
    public async Task Select_NeModifiePasLeReglageParDefaut()
    {
        // Le fournisseur affiché au démarrage est un réglage explicite ; un clic est une sélection
        // de session. Écrire à chaque clic mettrait la config en concurrence avec sa fenêtre.
        var config = ConfigFor(("a", false), ("b", false));
        config.DefaultProviderId = "a";
        var vm = Build(config, Usage("a"), Usage("b"));
        await vm.RefreshAsync();

        vm.Select("b");

        Assert.Equal("a", config.DefaultProviderId);
    }

    [Fact]
    public async Task Select_FournisseurInconnu_NeChangeRien()
    {
        var vm = Build(ConfigFor(("a", false)), Usage("a", day: 1_000));
        await vm.RefreshAsync();

        vm.Select("inexistant");

        Assert.Equal("1k", vm.Metrics[1].Value);
    }

    [Fact]
    public async Task DefaultProviderId_EstHonoreALOuverture()
    {
        var config = ConfigFor(("a", false), ("b", false));
        config.DefaultProviderId = "b";
        var vm = Build(config, Usage("a", day: 1_000), Usage("b", day: 2_000));

        await vm.RefreshAsync();

        Assert.Equal("2k", vm.Metrics[1].Value);
    }

    [Fact]
    public async Task DefaultProviderId_Masque_RetombeSurLePremierVisibleSansEffacerLeReglage()
    {
        var config = ConfigFor(("a", false), ("b", true));
        config.DefaultProviderId = "b";
        var vm = Build(config, Usage("a", day: 1_000), Usage("b", day: 2_000));

        await vm.RefreshAsync();

        Assert.Equal("1k", vm.Metrics[1].Value);
        Assert.Equal("b", config.DefaultProviderId);   // le réglage survit au masquage
    }

    [Fact]
    public async Task DefaultProviderId_Inconnu_RetombeSurLePremierVisible()
    {
        var config = ConfigFor(("a", false));
        config.DefaultProviderId = "jamais-vu";
        var vm = Build(config, Usage("a", day: 1_000));

        await vm.RefreshAsync();

        Assert.Equal("1k", vm.Metrics[1].Value);
    }

    // --- Visibilité

    [Fact]
    public async Task IsVisible_TousMasques_Faux()
    {
        var vm = Build(ConfigFor(("a", true)), Usage("a"));
        await vm.RefreshAsync();

        Assert.False(vm.IsVisible);
        Assert.Empty(vm.Metrics);
    }

    [Fact]
    public async Task IsVisible_ReglageDesactive_Faux()
    {
        var config = ConfigFor(("a", false));
        config.Enabled = false;
        var vm = Build(config, Usage("a"));

        await vm.RefreshAsync();

        Assert.False(vm.IsVisible);
    }

    [Fact]
    public async Task IsVisible_ProviderSansDonnees_Faux()
    {
        // `null` seul lierait le tableau entier de params, pas un élément.
        var vm = Build(ConfigFor(("a", false)), new AiUsage?[] { null });
        await vm.RefreshAsync();

        Assert.False(vm.IsVisible);
    }

    // --- Jauges

    [Fact]
    public async Task Jauge_SansQuota_SignaleLAbsencePlutotQueZero()
    {
        var vm = Build(ConfigFor(("a", false)), Usage("a", sessionUsed: null, weekUsed: null));
        await vm.RefreshAsync();

        Assert.False(vm.SessionGauge!.HasQuota);
        Assert.False(vm.WeekGauge!.HasQuota);
    }

    [Fact]
    public async Task Jauge_CouleurSuitLeSeuilDeLaConfig()
    {
        var config = ConfigFor(("a", false));
        config.AlertThreshold = 45;                       // restant 38 % < 45 → critique
        var vm = Build(config, Usage("a", sessionUsed: 62));

        await vm.RefreshAsync();

        Assert.Equal(UsageFormat.Critical, vm.SessionGauge!.Color);
    }

    [Fact]
    public async Task Jauge_AfficheLeRestantEtLeConsomme()
    {
        var vm = Build(ConfigFor(("a", false)), Usage("a", sessionUsed: 62));
        await vm.RefreshAsync();

        Assert.Equal(38, vm.SessionGauge!.RemainingPct);
        Assert.Equal(62, vm.SessionGauge.UsedPct);
    }

    // --- Démo

    [Fact]
    public async Task IsDemo_SuitLeFournisseurAffiche()
    {
        var vm = Build(ConfigFor(("reel", false), ("demo", false)),
                       Usage("reel"), Usage("demo", demo: true));
        await vm.RefreshAsync();

        Assert.False(vm.IsDemo);

        vm.Select("demo");
        Assert.True(vm.IsDemo);
    }
}
