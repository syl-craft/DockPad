using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;
using DockPad.Services.Localization;
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
                                 int requests = 47, string cost = "$4", string model = "claude-opus-5",
                                 int? sessionUsed = 62, int? weekUsed = 44,
                                 string usageUrl = "", string quotaNotice = "") => new()
    {
        ProviderId = id,
        Name = name.Length > 0 ? name : id,
        Glyph = "X",
        AccentColor = "#123456",
        Model = model,
        Cost = cost,
        UsageUrl = usageUrl,
        SessionTokens = session,
        DayTokens = day,
        MonthTokens = month,
        Requests = requests,
        QuotaNotice = quotaNotice,
        QuotaNoticeNote = quotaNotice.Length > 0 ? "HTTP 429 TooManyRequests" : "",
        Session = sessionUsed is null ? null : new UsageWindow { UsedPct = sessionUsed.Value },
        Week = weekUsed is null ? null : new UsageWindow { UsedPct = weekUsed.Value },
        IsDemo = demo,
    };

    private static UsageViewModel Build(UsageConfig config, params AiUsage?[] usages)
    {
        // Les libellés attendus par ces tests sont les français : la langue est posée
        // explicitement, sinon le résultat dépendrait de celle laissée par une autre classe.
        Loc.SetCulture(System.Globalization.CultureInfo.GetCultureInfo("fr"));

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

    // --- Libellés des jauges

    [Fact]
    public async Task Gauges_SansLibelleFourni_SessionEtSemaine()
    {
        var vm = Build(ConfigFor(("a", false)), Usage("a"));
        await vm.RefreshAsync();

        Assert.Equal("session", vm.SessionGauge!.Label);
        Assert.Equal("semaine", vm.WeekGauge!.Label);
    }

    [Fact]
    public async Task Gauges_LibelleFourniParLeFournisseur_Repris()
    {
        // Antigravity range deux réservoirs hebdomadaires dans les deux créneaux : « session »
        // y serait faux.
        var usage = new AiUsage
        {
            ProviderId = "a", Name = "a", Glyph = "X", AccentColor = "#123456",
            Session = new UsageWindow { UsedPct = 38, Label = "Gemini" },
            Week = new UsageWindow { UsedPct = 75, Label = "Claude · GPT" },
        };
        var vm = Build(ConfigFor(("a", false)), usage);
        await vm.RefreshAsync();

        Assert.Equal("Gemini", vm.SessionGauge!.Label);
        Assert.Equal("Claude · GPT", vm.WeekGauge!.Label);
    }

    // --- Métriques

    [Fact]
    public async Task Metrics_AvecCout_SixColonnesEtCoutEnCinquiemePosition()
    {
        var vm = Build(ConfigFor(("a", false)), Usage("a"));
        await vm.RefreshAsync();

        Assert.Equal(6, vm.Metrics.Count);
        Assert.Equal("Coût est.", vm.Metrics[4].Label);
        Assert.Equal("$4", vm.Metrics[4].Value);
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
        Assert.Equal("a", vm.SoloProviderId);
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
        Assert.Equal("b", vm.SoloProviderId);
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
    public async Task ReglageDesactive_NInterrogeAucunFournisseur()
    {
        // « Désactivé » doit couper la surveillance, pas seulement l'affichage : aucun scan disque
        // ni appel réseau, pour aucun fournisseur.
        var spies = new[] { new CountingProvider("a"), new CountingProvider("b") };
        var config = ConfigFor(("a", false), ("b", false));
        config.Enabled = false;
        var vm = new UsageViewModel(new UsageService(spies), () => config,
                                    () => new DateTime(2026, 8, 20, 12, 0, 0));

        await vm.RefreshAsync();

        Assert.All(spies, spy => Assert.Equal(0, spy.ReadCount));
    }

    private sealed class CountingProvider(string id) : IUsageProvider
    {
        public int ReadCount { get; private set; }
        public string Id => id;
        public string Name => id;
        public AiProbe Probe() => new() { Available = true, DisplayName = id };

        public Task<AiUsage?> ReadAsync(CancellationToken ct)
        {
            ReadCount++;
            return Task.FromResult<AiUsage?>(null);
        }
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
    public async Task Jauge_SansQuota_NAffirmeAucuneMesure()
    {
        // HasQuota faux masque la jauge entière côté vue. Sans ça le pourcentage vaudrait 0 et
        // afficherait « 0 % session », une mesure affirmée là où il n'y a pas de donnée — ce qui
        // arrive au démarrage, avant que la première lecture ait abouti.
        var vm = Build(ConfigFor(("a", false)), Usage("a", sessionUsed: null, weekUsed: null));
        await vm.RefreshAsync();

        Assert.False(vm.SessionGauge!.HasQuota);
        Assert.False(vm.WeekGauge!.HasQuota);
        Assert.Equal("", vm.SessionGauge.Reset);
        Assert.Equal("", vm.SessionGauge.Tooltip);
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
    public async Task Jauge_ExposeLeConsommeEtLeRestant()
    {
        var vm = Build(ConfigFor(("a", false)), Usage("a", sessionUsed: 62));
        await vm.RefreshAsync();

        Assert.Equal(62, vm.SessionGauge!.UsedPct);
        Assert.Equal(38, vm.SessionGauge.RemainingPct);
    }

    [Fact]
    public async Task Jauge_LibelleCourtMaisInfobulleExplicite()
    {
        // Le libellé est court par choix. « 62 % session » ne disant pas si le chiffre est le
        // consommé ou le restant, l'infobulle doit lever le doute — c'est le consommé que compte
        // claude.ai/settings/usage, et deux références pour la même donnée ne doivent pas se
        // contredire.
        var vm = Build(ConfigFor(("a", false)), Usage("a", sessionUsed: 62));
        await vm.RefreshAsync();

        Assert.Equal("session", vm.SessionGauge!.Label);
        Assert.Equal("semaine", vm.WeekGauge!.Label);
        Assert.Contains("62 % utilisés", vm.SessionGauge.Tooltip);
        Assert.Contains("38 % restants", vm.SessionGauge.Tooltip);
    }

    [Fact]
    public async Task Notice_QuotaIndisponible_RelayeeAvecSonInfobulle()
    {
        // Les jauges masquées prennent la place d'une explication : sans elle, le bandeau ne dit
        // pas ce qu'il se passe et la raison ne vit que dans le fichier de log.
        var vm = Build(ConfigFor(("a", false)),
                       Usage("a", sessionUsed: null, weekUsed: null,
                             quotaNotice: "Quota indisponible — nouvelle tentative dans 4 min"));
        await vm.RefreshAsync();

        Assert.True(vm.HasQuotaNotice);
        Assert.Contains("nouvelle tentative", vm.QuotaNotice);
        Assert.Contains("429", vm.QuotaNoticeTooltip);
    }

    [Fact]
    public async Task Notice_FournisseurSansQuota_RienAAfficher()
    {
        // Codex, Gemini et Copilot n'exposent aucun quota : leurs jauges sont absentes par nature,
        // pas en panne. Une notice y serait un faux problème.
        var vm = Build(ConfigFor(("a", false)), Usage("a", sessionUsed: null, weekUsed: null));
        await vm.RefreshAsync();

        Assert.False(vm.HasQuotaNotice);
        Assert.Equal("", vm.QuotaNotice);
    }

    [Fact]
    public async Task Notice_QuotaRevenu_LaNoticeDisparait()
    {
        var vm = Build(ConfigFor(("a", false)), Usage("a", sessionUsed: 62, weekUsed: 44));
        await vm.RefreshAsync();

        Assert.False(vm.HasQuotaNotice);
    }

    // --- Langue

    [Fact]
    public async Task Langue_BasculeePendantQueLeBandeauTourne_LesLibellesSuivent()
    {
        // La langue se change depuis les Options, avec la grille et son bandeau visibles derriere :
        // sans abonnement, le bandeau gardait ses libelles et ses nombres jusqu'au rafraichissement
        // suivant, soit jusqu'a une minute plus tard.
        var vm = Build(ConfigFor(("a", false)), Usage("a"));
        await vm.RefreshAsync();
        vm.Start();
        Assert.Equal("Jour", vm.Metrics[1].Label);

        Loc.SetCulture(System.Globalization.CultureInfo.GetCultureInfo("en"));

        Assert.Equal("Day", vm.Metrics[1].Label);
        Assert.Equal("session", vm.SessionGauge!.Label);
        vm.Stop();
    }

    [Fact]
    public async Task Langue_BasculeeApresStop_NeTouchePlusAuBandeau()
    {
        // Une fenetre rangee n'a rien a mettre a jour, et l'abonnement doit lacher : un evenement
        // statique qui retient chaque ViewModel cree est une fuite, et les captures en creent
        // plusieurs par processus.
        var vm = Build(ConfigFor(("a", false)), Usage("a"));
        await vm.RefreshAsync();
        vm.Start();
        vm.Stop();
        Loc.SetCulture(System.Globalization.CultureInfo.GetCultureInfo("en"));

        Assert.Equal("Jour", vm.Metrics[1].Label);
    }

    // --- Attente

    private sealed class SlowProvider(AiUsage usage, TimeSpan delay) : IUsageProvider
    {
        public string Id => usage.ProviderId;
        public string Name => Id;
        public AiProbe Probe() => new() { Available = true, DisplayName = Id };

        public async Task<AiUsage?> ReadAsync(CancellationToken ct)
        {
            await Task.Delay(delay, ct);
            return usage;
        }
    }

    [Fact]
    public async Task IsLoading_VraiPendantLaLecturePuisFauxALaFin()
    {
        // Une lecture réelle parcourt des centaines de fichiers : l'attente doit se voir, et le
        // bandeau garde ses valeurs précédentes pendant ce temps.
        var config = ConfigFor(("a", false));
        var vm = new UsageViewModel(
            new UsageService([new SlowProvider(Usage("a"), TimeSpan.FromMilliseconds(300))]),
            () => config, () => new DateTime(2026, 8, 20, 12, 0, 0));

        var refresh = vm.RefreshAsync();
        Assert.True(vm.IsLoading);

        await refresh;
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task IsLoading_RetombeAFauxMemeEnCasDEchec()
    {
        var config = ConfigFor(("boom", false));
        var vm = new UsageViewModel(new UsageService([new ThrowingProvider()]),
                                    () => config, () => new DateTime(2026, 8, 20, 12, 0, 0));

        await vm.RefreshAsync();

        Assert.False(vm.IsLoading);
    }

    private sealed class ThrowingProvider : IUsageProvider
    {
        public string Id => "boom";
        public string Name => "Boom";
        public AiProbe Probe() => new() { Available = true, DisplayName = "Boom" };
        public Task<AiUsage?> ReadAsync(CancellationToken ct) =>
            throw new InvalidOperationException("lecture cassée");
    }

    [Fact]
    public async Task IsLoading_UneLectureDepassee_NEteintPasLeSablier()
    {
        // Séquence de la touche Alt+Espace : masquer puis réafficher enchaîne deux rafraîchissements.
        // Le finally de l'ancienne éteignait le sablier pendant la lecture de la nouvelle.
        var config = ConfigFor(("a", false));
        var vm = new UsageViewModel(
            new UsageService([new SlowProvider(Usage("a"), TimeSpan.FromMilliseconds(400))]),
            () => config, () => new DateTime(2026, 8, 20, 12, 0, 0));

        var premier = vm.RefreshAsync();
        var second = vm.RefreshAsync();      // supplante le premier

        await premier;                        // l'annulé rend la main
        Assert.True(vm.IsLoading);            // la lecture récente tourne toujours

        await second;
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task UneLectureDepassee_NEcrasePasLesValeursRecentes()
    {
        // Le chemin d'exception de l'ancienne invocation faisait _snapshots = [] puis Rebuild, ce qui
        // vidait le bandeau APRÈS que la récente l'avait rempli.
        var config = ConfigFor(("casse", false), ("ok", false));
        var vm = new UsageViewModel(
            new UsageService([
                new ThrowingAfterDelayProvider("casse", TimeSpan.FromMilliseconds(300)),
                new SlowProvider(Usage("ok", day: 5_000), TimeSpan.FromMilliseconds(50)),
            ]),
            () => config, () => new DateTime(2026, 8, 20, 12, 0, 0));

        var premier = vm.RefreshAsync();
        await Task.Delay(50);
        var second = vm.RefreshAsync();

        await Task.WhenAll(premier, second);

        Assert.True(vm.IsVisible);
        Assert.NotEmpty(vm.Metrics);
    }

    private sealed class ThrowingAfterDelayProvider(string id, TimeSpan delay) : IUsageProvider
    {
        public string Id => id;
        public string Name => id;
        public AiProbe Probe() => new() { Available = true, DisplayName = id };

        public async Task<AiUsage?> ReadAsync(CancellationToken ct)
        {
            await Task.Delay(delay, ct);
            throw new InvalidOperationException("lecture cassée");
        }
    }

    [Fact]
    public async Task IsLoading_MarqueLOngletSelectionneSeulement()
    {
        // Providers lents : avec des lectures synchrones, RefreshAsync se termine avant l'assertion
        // et l'état intermédiaire n'est jamais observable.
        var config = ConfigFor(("a", false), ("b", false));
        var slow = TimeSpan.FromMilliseconds(200);
        var vm = new UsageViewModel(
            new UsageService([new SlowProvider(Usage("a"), slow), new SlowProvider(Usage("b"), slow)]),
            () => config, () => new DateTime(2026, 8, 20, 12, 0, 0));
        await vm.RefreshAsync();

        // Les onglets existent : un nouveau rafraîchissement doit marquer le seul onglet affiché.
        var refresh = vm.RefreshAsync();

        Assert.True(vm.Tabs.Single(t => t.ProviderId == "a").IsLoading);
        Assert.False(vm.Tabs.Single(t => t.ProviderId == "b").IsLoading);

        await refresh;
        Assert.All(vm.Tabs, t => Assert.False(t.IsLoading));
    }

    // --- Page du fournisseur

    [Fact]
    public async Task UsageUrl_SansUrl_LeLienEstMasque()
    {
        var vm = Build(ConfigFor(("a", false)), Usage("a"));
        await vm.RefreshAsync();

        Assert.False(vm.HasUsageUrl);
        Assert.Equal("", vm.UsageUrl);
    }

    [Fact]
    public async Task UsageUrl_AvecUrl_LeLienEstVisible()
    {
        var vm = Build(ConfigFor(("a", false)),
                       Usage("a", usageUrl: "https://claude.ai/new#settings/usage"));
        await vm.RefreshAsync();

        Assert.True(vm.HasUsageUrl);
        Assert.Equal("https://claude.ai/new#settings/usage", vm.UsageUrl);
    }

    [Fact]
    public async Task UsageUrl_SuitLeFournisseurAffiche()
    {
        var vm = Build(ConfigFor(("a", false), ("b", false)),
                       Usage("a", usageUrl: "https://a.example/usage"),
                       Usage("b"));
        await vm.RefreshAsync();

        Assert.Equal("https://a.example/usage", vm.UsageUrl);

        vm.Select("b");
        Assert.False(vm.HasUsageUrl);   // le fournisseur suivant n'a pas de page
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

    // --- IA mise en avant : gestes, éligibilité et mode Auto

    /// <summary>
    /// Fournisseur dont le relevé se change entre deux lectures, qui compte ses appels et peut
    /// retenir sa réponse sur une porte. Le relevé rendu est celui du moment de l'appel.
    /// </summary>
    private sealed class ScriptedProvider(AiUsage usage) : IUsageProvider
    {
        public AiUsage Usage { get; set; } = usage;
        public TaskCompletionSource? Gate { get; set; }
        public int ReadCount { get; private set; }
        public string Id => usage.ProviderId;
        public string Name => Id;
        public AiProbe Probe() => new() { Available = true, DisplayName = Id };

        public async Task<AiUsage?> ReadAsync(CancellationToken ct)
        {
            ReadCount++;
            var snapshot = Usage;
            var gate = Gate;
            if (gate != null) await gate.Task;
            return snapshot;
        }
    }

    private static ScriptedProvider Scripted(string id, int? session, int? week = null, string notice = "") =>
        new(Quota(id, session, week, notice));

    private static AiUsage Quota(string id, int? session, int? week = null, string notice = "") =>
        Usage(id, sessionUsed: session, weekUsed: week, quotaNotice: notice);

    private static UsageViewModel BuildScripted(UsageConfig config, params ScriptedProvider[] providers)
    {
        Loc.SetCulture(System.Globalization.CultureInfo.GetCultureInfo("fr"));
        return new UsageViewModel(new UsageService(providers), () => config, () => new DateTime(2026, 8, 20, 12, 0, 0));
    }

    private static UsageConfig AutoConfig(params string[] ids)
    {
        var config = ConfigFor(ids.Select(id => (id, false)).ToArray());
        config.FocusMode = UsageFocusMode.AutoClosestToLimit;
        return config;
    }

    [Fact]
    public async Task Cycle_SuivantPrecedent_EnBoucle()
    {
        // « g » n'a pas de quota : le cycle le saute.
        var vm = Build(ConfigFor(("a", false), ("g", false), ("b", false), ("c", false)),
                       Quota("a", 10), Quota("g", null), Quota("b", 20), Quota("c", 30));
        await vm.RefreshAsync();

        Assert.Equal<string>(["a", "b", "c"], vm.EligibleIds);

        vm.Cycle(+1);
        Assert.Equal("b", vm.SoloProviderId);
        vm.Cycle(+1);
        Assert.Equal("c", vm.SoloProviderId);
        vm.Cycle(+1);
        Assert.Equal("a", vm.SoloProviderId);
        vm.Cycle(-1);
        Assert.Equal("c", vm.SoloProviderId);
    }

    [Fact]
    public async Task Cycle_UnSeulEligible_RienNeChange()
    {
        var config = ConfigFor(("a", false), ("g", false));
        config.FocusMode = UsageFocusMode.AutoClosestToLimit;
        var vm = Build(config, Quota("a", 10), Quota("g", null));
        await vm.RefreshAsync();

        vm.Cycle(+1);
        vm.Cycle(-1);

        Assert.Equal("a", vm.SoloProviderId);
        Assert.True(vm.IsAutoActive);   // sans effet, le cycle ne suspend rien
    }

    [Fact]
    public async Task Geste_NEcritPasLaConfig()
    {
        var config = AutoConfig("a", "b");
        config.DefaultProviderId = "a";
        var loads = 0;
        var vm = new UsageViewModel(new UsageService([new StubProvider(Quota("a", 10)), new StubProvider(Quota("b", 90))]),
                                    () => { loads++; return config; },
                                    () => new DateTime(2026, 8, 20, 12, 0, 0));
        await vm.RefreshAsync();
        var before = System.Text.Json.JsonSerializer.Serialize(config);

        vm.SelectByGesture("a");
        vm.Cycle(+1);
        vm.Cycle(-1);

        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(config));
        Assert.Equal(UsageFocusMode.AutoClosestToLimit, config.FocusMode);
        Assert.Equal(1, loads);   // un geste ne relit pas non plus la config
    }

    [Fact]
    public async Task Auto_ChoisitLaPlusProcheDeSaLimite()
    {
        var vm = Build(AutoConfig("a", "b", "c"), Quota("a", 30, 40), Quota("b", 20, 85), Quota("c", null, 70));
        await vm.RefreshAsync();

        Assert.Equal("b", vm.SoloProviderId);
    }

    [Fact]
    public async Task Auto_EgaliteOrdreDuBandeau()
    {
        var config = AutoConfig("b", "a");
        config.DefaultProviderId = "a";
        var vm = Build(config, Quota("a", 60), Quota("b", 10, 60));
        await vm.RefreshAsync();

        Assert.Equal("b", vm.SoloProviderId);
    }

    [Fact]
    public async Task Auto_ReleveAncienIgnore()
    {
        var stale = new AiUsage
        {
            ProviderId = "a", Name = "a", Glyph = "X", AccentColor = "#123456",
            Session = new UsageWindow { UsedPct = 95, ObservedAt = new DateTime(2026, 8, 20, 9, 0, 0) },
            Week = new UsageWindow { UsedPct = 10 },
        };
        var vm = Build(AutoConfig("a", "b"), stale, Quota("b", 50));
        await vm.RefreshAsync();

        Assert.Equal("b", vm.SoloProviderId);
    }

    [Fact]
    public async Task Auto_AucuneFenetre_SelectionInchangee()
    {
        var config = AutoConfig("codex", "g");
        config.DefaultProviderId = "g";
        var vm = Build(config, Quota("codex", null, notice: "Quota indisponible"), Quota("g", null));
        await vm.RefreshAsync();

        Assert.Equal("g", vm.SoloProviderId);
        Assert.True(vm.IsAutoActive);
    }

    [Theory]
    [InlineData("onglet")]
    [InlineData("logo")]
    [InlineData("molette")]
    [InlineData("cycle-arriere")]
    public async Task Auto_SuspenduParGeste_OngletLogoMoletteCycle(string gesture)
    {
        var a = Scripted("a", 10);
        var b = Scripted("b", 90);
        var vm = BuildScripted(AutoConfig("a", "b"), a, b);
        await vm.RefreshAsync();
        Assert.Equal("b", vm.SoloProviderId);

        // Onglet du bandeau et logo de la barre passent par le même geste ; molette et Ctrl + Tab par le cycle.
        switch (gesture)
        {
            case "onglet":
            case "logo": vm.SelectByGesture("a"); break;
            case "molette": vm.Cycle(+1); break;
            case "cycle-arriere": vm.Cycle(-1); break;
        }

        b.Usage = Quota("b", 99);
        await vm.RefreshAsync();

        Assert.Equal("a", vm.SoloProviderId);
        Assert.False(vm.IsAutoActive);
    }

    [Fact]
    public async Task Auto_GesteSurLaSelectionCourante_Suspend()
    {
        var a = Scripted("a", 10);
        var b = Scripted("b", 90);
        var vm = BuildScripted(AutoConfig("a", "b"), a, b);
        await vm.RefreshAsync();

        vm.SelectByGesture("b");
        a.Usage = Quota("a", 99);
        await vm.RefreshAsync();

        Assert.Equal("b", vm.SoloProviderId);
        Assert.False(vm.IsAutoActive);
    }

    [Fact]
    public async Task Auto_ReleveTermineApresGeste_NeRetablitPas()
    {
        // Le relevé part avant le geste et finit après : il doit trouver la suspension posée.
        var a = Scripted("a", 90);
        var b = Scripted("b", 10);
        var vm = BuildScripted(AutoConfig("a", "b"), a, b);
        await vm.RefreshAsync();
        Assert.Equal("a", vm.SoloProviderId);

        b.Usage = Quota("b", 95);
        b.Gate = new TaskCompletionSource();
        var refresh = vm.RefreshAsync();

        vm.SelectByGesture("a");
        b.Gate.SetResult();
        await refresh;

        Assert.Equal("a", vm.SoloProviderId);
        Assert.False(vm.IsAutoActive);
    }

    [Fact]
    public async Task Auto_ReleveDepasse_NeChoisitPas()
    {
        // Un relevé supplanté qui ignore l'annulation rend quand même ses valeurs : la garde
        // de la lecture courante doit les écarter, Auto compris.
        var a = Scripted("a", 95);
        var b = Scripted("b", 10);
        var gate = new TaskCompletionSource();
        a.Gate = gate;
        var vm = BuildScripted(AutoConfig("a", "b"), a, b);
        var stale = vm.RefreshAsync();

        a.Gate = null;
        a.Usage = Quota("a", 10);
        b.Usage = Quota("b", 80);
        await vm.RefreshAsync();
        Assert.Equal("b", vm.SoloProviderId);

        gate.SetResult();
        await stale;

        Assert.Equal("b", vm.SoloProviderId);
    }

    [Fact]
    public async Task ApplyConfig_RepasseEnAuto_LeveLaSuspension()
    {
        var a = Scripted("a", 10);
        var b = Scripted("b", 90);
        var config = AutoConfig("a", "b");
        var vm = BuildScripted(config, a, b);
        await vm.RefreshAsync();
        vm.SelectByGesture("a");

        config.FocusMode = UsageFocusMode.Manual;
        vm.ApplyConfig();
        Assert.Equal("a", vm.SoloProviderId);   // le choix manuel garde la sélection courante
        Assert.False(vm.IsAutoActive);

        config.FocusMode = UsageFocusMode.AutoClosestToLimit;
        vm.ApplyConfig();

        Assert.True(vm.IsAutoActive);
        Assert.Equal("b", vm.SoloProviderId);   // appliqué tout de suite, sur le dernier relevé
        Assert.Equal(1, a.ReadCount);
    }

    [Fact]
    public async Task ApplyConfig_SansRelecture()
    {
        var a = Scripted("a", 10);
        var b = Scripted("b", 90);
        var config = ConfigFor(("a", false), ("b", false));
        var vm = BuildScripted(config, a, b);
        vm.Start();
        await vm.RefreshAsync();
        var reads = (a.ReadCount, b.ReadCount);

        config.Placement = UsagePlacement.Right;
        vm.ApplyConfig();
        config.FocusMode = UsageFocusMode.AutoClosestToLimit;
        config.BarStyle = SideStripStyle.HeroStack;
        config.ShowSystemLoad = false;
        vm.ApplyConfig();
        config.Placement = UsagePlacement.Bottom;
        vm.ApplyConfig();
        vm.Stop();

        Assert.Equal(reads, (a.ReadCount, b.ReadCount));
        Assert.Equal("b", vm.SoloProviderId);
    }

    [Fact]
    public void ApplyConfig_Reactivation_UneLecture()
    {
        var a = Scripted("a", 10);
        var config = ConfigFor(("a", false));
        config.Enabled = false;
        var vm = BuildScripted(config, a);
        vm.Start();
        Assert.Equal(0, a.ReadCount);

        config.Enabled = true;
        vm.ApplyConfig();

        Assert.Equal(1, a.ReadCount);
        Assert.True(vm.IsVisible);

        // Fenêtre rangée : la réactivation attend le prochain affichage.
        vm.Stop();
        config.Enabled = false;
        vm.ApplyConfig();
        config.Enabled = true;
        vm.ApplyConfig();

        Assert.Equal(1, a.ReadCount);
    }

    [Fact]
    public async Task ADroite_SelectionNonEligible_PremierEligible()
    {
        var config = ConfigFor(("g", false), ("a", false), ("b", false));
        var vm = Build(config, Quota("g", null), Quota("a", 10), Quota("b", 90));
        await vm.RefreshAsync();
        vm.SelectByGesture("g");
        Assert.Equal("g", vm.SoloProviderId);

        config.Placement = UsagePlacement.Right;
        vm.ApplyConfig();

        Assert.Equal("a", vm.SoloProviderId);
        Assert.Equal(UsagePlacement.Right, config.Placement);

        config.Placement = UsagePlacement.Bottom;
        vm.ApplyConfig();
        Assert.Equal("a", vm.SoloProviderId);   // le retour en bas garde la sélection courante
    }

    [Fact]
    public async Task ADroite_AucunEligible_SelectionInchangee()
    {
        var config = ConfigFor(("g", false), ("c", false));
        config.Placement = UsagePlacement.Right;
        config.DefaultProviderId = "c";
        var vm = Build(config, Quota("g", null), Quota("c", null));
        await vm.RefreshAsync();

        Assert.Equal("c", vm.SoloProviderId);
    }

    [Fact]
    public async Task EnBas_SelectionNonEligible_Conservee()
    {
        var config = ConfigFor(("g", false), ("a", false));
        config.DefaultProviderId = "g";
        var vm = Build(config, Quota("g", null), Quota("a", 10));
        await vm.RefreshAsync();

        Assert.Equal("g", vm.SoloProviderId);

        vm.SelectByGesture("g");
        await vm.RefreshAsync();
        Assert.Equal("g", vm.SoloProviderId);
    }

    [Fact]
    public async Task Demarrage_AutoEnregistre_Actif()
    {
        var config = AutoConfig("a", "b");
        config.DefaultProviderId = "a";
        var vm = Build(config, Quota("a", 10), Quota("b", 70));
        await vm.RefreshAsync();

        Assert.True(vm.IsAutoActive);
        Assert.Equal("b", vm.SoloProviderId);
        Assert.Equal(2, vm.Snapshots.Count);
        Assert.Same(config, vm.Config);
    }

    /// <summary>
    /// Config enregistrée du moment : chaque enregistrement est une instance distincte, comme une
    /// relecture de usage.json.
    /// </summary>
    private sealed class SavedConfig(UsageConfig initial)
    {
        public UsageConfig Current { get; set; } = initial;
    }

    private static UsageViewModel BuildSaved(SavedConfig saved, params ScriptedProvider[] providers)
    {
        Loc.SetCulture(System.Globalization.CultureInfo.GetCultureInfo("fr"));
        return new UsageViewModel(new UsageService(providers), () => saved.Current, () => new DateTime(2026, 8, 20, 12, 0, 0));
    }

    [Fact]
    public async Task Auto_ReleveAvecConfigAutoTermineApresGeste_NeLevePasLaSuspension()
    {
        // Le relevé adopté était en Manuel ; le suivant lit une config Auto, puis un geste arrive
        // pendant sa lecture. Le passage à l'Auto qu'il rapporte est antérieur au geste.
        var a = Scripted("a", 90);
        var b = Scripted("b", 10);
        var saved = new SavedConfig(ConfigFor(("a", false), ("b", false)));
        var vm = BuildSaved(saved, a, b);
        await vm.RefreshAsync();
        Assert.Equal("a", vm.SoloProviderId);

        saved.Current = AutoConfig("a", "b");
        b.Usage = Quota("b", 95);
        b.Gate = new TaskCompletionSource();
        var refresh = vm.RefreshAsync();

        vm.SelectByGesture("a");
        b.Gate.SetResult();
        await refresh;

        Assert.Equal("a", vm.SoloProviderId);
        Assert.False(vm.IsAutoActive);

        // Un choix d'Auto postérieur au geste, lui, lève la suspension.
        saved.Current = ConfigFor(("a", false), ("b", false));
        vm.ApplyConfig();
        saved.Current = AutoConfig("a", "b");
        vm.ApplyConfig();

        Assert.True(vm.IsAutoActive);
        Assert.Equal("b", vm.SoloProviderId);
    }

    [Fact]
    public async Task ApplyConfig_Masquage_SelectionReplieeSansLecture()
    {
        var a = Scripted("a", 90);
        var b = Scripted("b", 10);
        var c = Scripted("c", 20);
        var saved = new SavedConfig(ConfigFor(("a", false), ("b", false), ("c", false)));
        var vm = BuildSaved(saved, a, b, c);
        await vm.RefreshAsync();
        vm.SelectByGesture("a");

        saved.Current = ConfigFor(("a", true), ("b", false), ("c", false));
        vm.ApplyConfig();

        Assert.Equal("b", vm.SoloProviderId);
        Assert.Equal<string>(["b", "c"], vm.EligibleIds);
        Assert.DoesNotContain(vm.Snapshots, s => s.ProviderId == "a");
        vm.Cycle(+1);
        Assert.Equal("c", vm.SoloProviderId);
        vm.Cycle(+1);
        Assert.Equal("b", vm.SoloProviderId);

        // L'Auto ne choisit pas non plus le fournisseur masqué, pourtant le plus consommé.
        saved.Current = AutoConfig("b", "c");
        saved.Current.Providers.Insert(0, new AiProviderEntry { Id = "a", Name = "a", Hidden = true, Order = -1 });
        vm.ApplyConfig();
        Assert.Equal("c", vm.SoloProviderId);

        Assert.Equal((1, 1, 1), (a.ReadCount, b.ReadCount, c.ReadCount));
    }

    [Fact]
    public async Task ReleveEnVol_PuisMasquage_FournisseurMasqueNonPublie()
    {
        var a = Scripted("a", 90);
        var b = Scripted("b", 10);
        b.Gate = new TaskCompletionSource();
        var saved = new SavedConfig(ConfigFor(("a", false), ("b", false)));
        var vm = BuildSaved(saved, a, b);
        var refresh = vm.RefreshAsync();

        saved.Current = ConfigFor(("a", true), ("b", false));
        vm.ApplyConfig();
        b.Gate.SetResult();
        await refresh;

        Assert.DoesNotContain(vm.Snapshots, s => s.ProviderId == "a");
        Assert.Equal("b", vm.SoloProviderId);
        Assert.Equal<string>(["b"], vm.EligibleIds);
    }
}
