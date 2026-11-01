using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;
using DockPad.Services.Localization;
using DockPad.Services.StreamDock;
using DockPad.Services.Usage;

namespace DockPad.Tests;

/// <summary>
/// Modèle de vue de la barre à droite : état, logos, point d'alerte, gris d'ancienneté, éléments de chacun des
/// quatre styles, charge du PC et courbe, infobulles. Aucun WPF : tout se lit sur le modèle.
/// </summary>
public class UsageBarViewModelTests
{
    private static readonly DateTime Now = new(2026, 8, 20, 12, 0, 0);
    private static readonly DateTime Stale = new(2026, 8, 20, 9, 0, 0);

    private sealed class StubProvider(AiUsage usage) : IUsageProvider
    {
        public AiUsage Snapshot { get; set; } = usage;
        public Task? PendingRead { get; set; }
        public string Id => Snapshot.ProviderId;
        public string Name => Id;
        public AiProbe Probe() => new() { Available = true, DisplayName = Id };
        public async Task<AiUsage?> ReadAsync(CancellationToken ct)
        {
            if (PendingRead != null) await PendingRead;
            return Snapshot;
        }
    }

    private sealed class Rig
    {
        public required UsageBarViewModel Bar { get; init; }
        public required UsageViewModel Usage { get; init; }
        public required SystemLoadFeed Feed { get; init; }
        public required FakeSystemLoadMonitor Monitor { get; init; }
        public required ManualTimeProvider Time { get; init; }
    }

    /// <summary>
    /// Valeur non nulle, ou échec du test : remplace l'opérateur « ! » dans les assertions.
    /// </summary>
    private static T Some<T>(T? value) where T : class
    {
        Assert.NotNull(value);
        return value ?? throw new InvalidOperationException("Value is null.");
    }

    private static AiUsage Usage(string id, int? session = 62, int? week = 44, string name = "", string notice = "",
                                 bool staleSession = false, bool demo = false, string sessionLabel = "",
                                 string weekLabel = "", DateTime? sessionResetsAt = null, string usageUrl = "") => new()
    {
        ProviderId = id,
        Name = name.Length > 0 ? name : id,
        Glyph = "X",
        AccentColor = "#123456",
        QuotaNotice = notice,
        QuotaNoticeNote = notice.Length > 0 ? "HTTP 429" : "",
        Session = session is null
            ? null
            : new UsageWindow
            {
                UsedPct = session.Value, Label = sessionLabel, ResetsAt = sessionResetsAt,
                ObservedAt = staleSession ? Stale : null,
            },
        Week = week is null ? null : new UsageWindow { UsedPct = week.Value, Label = weekLabel },
        IsDemo = demo,
        UsageUrl = usageUrl,
    };

    private static UsageConfig ConfigFor(params string[] ids)
    {
        var config = new UsageConfig();
        var order = 0;
        foreach (var id in ids)
            config.Providers.Add(new AiProviderEntry { Id = id, Name = id, Order = order++ });
        return config;
    }

    private static async Task<Rig> BuildAsync(UsageConfig config, SystemLoad? load, params AiUsage[] usages)
    {
        Loc.SetCulture(System.Globalization.CultureInfo.GetCultureInfo("fr"));

        var service = new UsageService(usages.Select(u => (IUsageProvider)new StubProvider(u)).ToList());
        var usage = new UsageViewModel(service, () => config, () => Now);
        await usage.RefreshAsync();

        var monitor = new FakeSystemLoadMonitor { Value = load ?? new SystemLoad(23, 61) };
        var time = new ManualTimeProvider();
        var feed = new SystemLoadFeed(monitor, time, publishOn: null);
        feed.Start();
        await feed.PendingSample.WaitAsync(TimeSpan.FromSeconds(10));

        return new Rig
        {
            Bar = new UsageBarViewModel(usage, feed, () => Now),
            Usage = usage, Feed = feed, Monitor = monitor, Time = time,
        };
    }

    private static Task<Rig> BuildAsync(UsageConfig config, params AiUsage[] usages) =>
        BuildAsync(config, (SystemLoad?)null, usages);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Anneaux_DoubleCharge_CycleCpuRamPuisQuotaLePlusConsomme(bool singleProvider)
    {
        var config = singleProvider ? ConfigFor("claude") : ConfigFor("claude", "codex");
        config.BarStyle = SideStripStyle.Rings;
        config.FocusMode = UsageFocusMode.AutoClosestToLimit;
        var rig = await BuildAsync(config, singleProvider
            ? [Usage("claude", 30, 85)]
            : [Usage("claude", 30, 85), Usage("codex", 20, 10)]);
        rig.Bar.PickGauge(new BarGauge("claude", BarGaugeKind.Session));

        rig.Bar.CycleLoadGauge();
        Assert.Equal(BarGaugeKind.Cpu, Some(Some(rig.Bar.FeaturedGauge).Ring.Selection).Kind);
        Assert.Equal("CPU", Some(rig.Bar.FeaturedGauge).Name);
        Assert.Equal("", Some(rig.Bar.Rings).Legend);
        Assert.Equal(singleProvider, rig.Bar.IsAuto);
        Assert.NotNull(rig.Bar.Load);

        await rig.Usage.RefreshAsync();
        rig.Bar.CycleLoadGauge();
        Assert.Equal(BarGaugeKind.Memory, Some(Some(rig.Bar.FeaturedGauge).Ring.Selection).Kind);
        Assert.Equal("RAM", Some(rig.Bar.FeaturedGauge).Name);
        Assert.Equal("", Some(rig.Bar.Rings).Legend);
        Assert.NotNull(rig.Bar.Load);

        rig.Bar.CycleLoadGauge();
        Assert.Equal(new BarGauge("claude", BarGaugeKind.Week), Some(rig.Bar.FeaturedGauge).Ring.Selection);
        Assert.Equal("85%", Some(rig.Bar.Rings).CenterText);
        Assert.NotNull(Some(rig.Bar.Rings).Inner);
        Assert.Equal(singleProvider, rig.Bar.IsAuto);
    }

    [Fact]
    public async Task Anneaux_DoubleCharge_Masquee_NeChangePasLaSelection()
    {
        var config = ConfigFor("claude");
        config.ShowSystemLoad = false;
        var rig = await BuildAsync(config, Usage("claude"));
        var featured = rig.Bar.FeaturedGauge;

        rig.Bar.CycleLoadGauge();

        Assert.Equal(featured, rig.Bar.FeaturedGauge);
    }

    [Theory]
    [InlineData(SideStripStyle.Reservoir)]
    [InlineData(SideStripStyle.Rings)]
    public async Task Jauge_ChoisitLaFenetreCliquee_EtConserveLeChoixAuReleve(SideStripStyle style)
    {
        var config = ConfigFor("claude", "codex");
        config.BarStyle = style;
        var rig = await BuildAsync(config, Usage("claude"), Usage("codex", 80, 27));
        var chosen = new BarGauge("codex", BarGaugeKind.Week);

        rig.Bar.PickGauge(chosen);

        Assert.Equal("codex", rig.Usage.SoloProviderId);
        var featured = Some(rig.Bar.FeaturedGauge);
        var reservoir = Some(rig.Bar.Reservoir);
        var rings = Some(rig.Bar.Rings);
        Assert.Equal(chosen, featured.Ring.Selection);
        Assert.Equal("27%", featured.Ring.Text);
        Assert.Equal(0.27, reservoir.Level);
        Assert.Equal("27%", rings.CenterText);
        Assert.Null(rings.Inner);
        Assert.Contains(reservoir.Dots, dot => dot.Selection == new BarGauge("claude", BarGaugeKind.Session));
        Assert.Contains(rings.Others, ring => ring.Selection == new BarGauge("claude", BarGaugeKind.Session));

        await rig.Usage.RefreshAsync();
        Assert.Equal(chosen, Some(rig.Bar.FeaturedGauge).Ring.Selection);

        rig.Bar.Cycle(1);
        Assert.Equal("62%", Some(rig.Bar.FeaturedGauge).Ring.Text);
        rig.Bar.Pick("codex");
        Assert.Equal("80%", Some(rig.Bar.FeaturedGauge).Ring.Text);
    }

    [Theory]
    [InlineData(BarGaugeKind.Cpu, "CPU", "23%", UsageBarViewModel.CpuColor)]
    [InlineData(BarGaugeKind.Memory, "RAM", "61%", UsageBarViewModel.MemoryColor)]
    public async Task Jauge_Charge_GrandFormat_SuspendAuto_EtSuitLesMesures(BarGaugeKind kind, string label,
        string text, string color)
    {
        var config = ConfigFor("claude", "codex");
        config.FocusMode = UsageFocusMode.AutoClosestToLimit;
        var rig = await BuildAsync(config, Usage("claude"), Usage("codex", 80, 27));
        var chosen = new BarGauge("", kind);

        rig.Bar.PickGauge(chosen);

        Assert.False(rig.Bar.IsAuto);
        Assert.Equal("codex", rig.Usage.SoloProviderId);
        var featured = Some(rig.Bar.FeaturedGauge);
        Assert.Equal(label, featured.Name);
        Assert.Equal(text, featured.Ring.Text);
        Assert.Equal(color, featured.Ring.Color);
        Assert.False(featured.CanOpenUsagePage);
        Assert.DoesNotContain(Some(rig.Bar.Reservoir).Dots, dot => dot.Selection == chosen);
        Assert.DoesNotContain(rig.Bar.LoadRings, ring => ring.Selection == chosen);
        Assert.Contains(Some(rig.Bar.Rings).Others, ring => ring.Text == "80%");

        var featuredBeforeMeasure = featured;
        rig.Monitor.Value = new SystemLoad(40, 62);
        rig.Time.Advance(TimeSpan.FromSeconds(5));
        await rig.Feed.PendingSample.WaitAsync(TimeSpan.FromSeconds(10));
        featured = Some(rig.Bar.FeaturedGauge);
        Assert.Same(featuredBeforeMeasure, featured);
        Assert.Same(featuredBeforeMeasure.Ring, featured.Ring);
        Assert.Equal(kind == BarGaugeKind.Cpu ? "40%" : "62%", featured.Ring.Text);
        Assert.Equal(featured.Ring.Fraction, Some(rig.Bar.Reservoir).Level);
        Assert.Equal(featured.Ring.Text, Some(rig.Bar.Rings).CenterText);

        rig.Usage.ChooseAuto();
        Assert.True(rig.Bar.IsAuto);
        // La même IA reste mise en avant : la jauge choisie reste en grand.
        Assert.Equal(chosen, Some(rig.Bar.FeaturedGauge).Ring.Selection);
        rig.Bar.Cycle(1);
        Assert.Equal("62%", Some(rig.Bar.FeaturedGauge).Ring.Text);
    }

    [Fact]
    public async Task Jauge_Quota_UnSeulFournisseur_NeSuspendPasAuto()
    {
        var config = ConfigFor("claude");
        config.FocusMode = UsageFocusMode.AutoClosestToLimit;
        var rig = await BuildAsync(config, Usage("claude"));

        rig.Bar.PickGauge(new BarGauge("claude", BarGaugeKind.Week));

        Assert.True(rig.Bar.IsAuto);
        Assert.Equal("44%", Some(rig.Bar.FeaturedGauge).Ring.Text);
    }

    [Theory]
    [InlineData(BarGaugeKind.Cpu)]
    [InlineData(BarGaugeKind.Memory)]
    public async Task Jauge_Charge_UnSeulFournisseur_NeSuspendPasAuto(BarGaugeKind kind)
    {
        var config = ConfigFor("claude");
        config.FocusMode = UsageFocusMode.AutoClosestToLimit;
        var rig = await BuildAsync(config, Usage("claude"));

        rig.Bar.PickGauge(new BarGauge("", kind));

        Assert.True(rig.Bar.IsAuto);
        Assert.Equal(kind, Some(Some(rig.Bar.FeaturedGauge).Ring.Selection).Kind);
    }

    [Theory]
    [InlineData(BarGaugeKind.Cpu)]
    [InlineData(BarGaugeKind.Memory)]
    public async Task Jauge_ChargeDecochee_RetourPropreALaPlusConsommee(BarGaugeKind kind)
    {
        var config = ConfigFor("claude");
        var rig = await BuildAsync(config, Usage("claude"));
        rig.Bar.PickGauge(new BarGauge("", kind));

        config.ShowSystemLoad = false;
        rig.Usage.ApplyConfig();

        Assert.Null(rig.Bar.Load);
        Assert.Empty(rig.Bar.LoadRings);
        Assert.Equal(new BarGauge("claude", BarGaugeKind.Session), Some(rig.Bar.FeaturedGauge).Ring.Selection);
        Assert.Equal("62%", Some(rig.Bar.Reservoir).LevelText);
        Assert.Equal("62%", Some(rig.Bar.Rings).CenterText);
        Assert.NotNull(Some(rig.Bar.Rings).Inner);
        Assert.Contains(Some(rig.Bar.Reservoir).Dots, dot => dot.Selection == new BarGauge("claude", BarGaugeKind.Week));
    }

    [Fact]
    public async Task Jauge_FenetreDisparueDuReleve_OublieLeChoixEtRevientALaPlusConsommee()
    {
        var config = ConfigFor("claude");
        var provider = new StubProvider(Usage("claude"));
        var usage = new UsageViewModel(new UsageService([provider]), () => config, () => Now);
        await usage.RefreshAsync();
        var feed = new SystemLoadFeed(new FakeSystemLoadMonitor(), new ManualTimeProvider(), publishOn: null);
        using var bar = new UsageBarViewModel(usage, feed, () => Now);
        bar.PickGauge(new BarGauge("claude", BarGaugeKind.Week));

        provider.Snapshot = Usage("claude", 70, null);
        await usage.RefreshAsync();

        Assert.Equal("70%", Some(bar.FeaturedGauge).Ring.Text);
        Assert.Equal("70%", Some(bar.Reservoir).LevelText);
        Assert.DoesNotContain(Some(bar.Reservoir).Dots, dot => dot.Selection == new BarGauge("claude", BarGaugeKind.Session));
        provider.Snapshot = Usage("claude", 70, 20);
        await usage.RefreshAsync();
        Assert.Equal("70%", Some(bar.FeaturedGauge).Ring.Text);
        Assert.Contains(Some(bar.Reservoir).Dots, dot => dot.Selection == new BarGauge("claude", BarGaugeKind.Week));
    }

    [Fact]
    public async Task Charge_Mesure_GardeLesInstancesDesAnneaux()
    {
        var rig = await BuildAsync(ConfigFor("claude"), Usage("claude"));
        var cpu = rig.Bar.LoadRings[0];
        var memory = rig.Bar.LoadRings[1];

        rig.Monitor.Value = new SystemLoad(40, 62);
        rig.Time.Advance(TimeSpan.FromSeconds(5));
        await rig.Feed.PendingSample.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Same(cpu, rig.Bar.LoadRings[0]);
        Assert.Same(memory, rig.Bar.LoadRings[1]);
        Assert.Contains(cpu, Some(rig.Bar.Reservoir).Dots);
        Assert.Equal("40", cpu.NumberText);
        Assert.Equal("62", memory.NumberText);
    }

    [Theory]
    [InlineData("https://example.test/usage", true)]
    [InlineData("http://example.test/usage", true)]
    [InlineData("", false)]
    [InlineData("file:///C:/test", false)]
    [InlineData("not a url", false)]
    public async Task GrandLogo_CliquableSeulementAvecAdresseWeb(string url, bool canOpen)
    {
        var snapshot = Usage("claude", usageUrl: url);
        var rig = await BuildAsync(ConfigFor("claude"), snapshot);

        var featured = Some(rig.Bar.FeaturedGauge);
        Assert.Equal(canOpen, featured.CanOpenUsagePage);
        Assert.Equal(url, featured.UsageUrl);
    }

    [Fact]
    public async Task Jauge_ChargeSeule_EtSelectionInvalide()
    {
        var rig = await BuildAsync(ConfigFor("gemini"), Usage("gemini", null, null));
        rig.Bar.PickGauge(new BarGauge("", BarGaugeKind.Cpu));
        Assert.Equal("CPU", Some(rig.Bar.FeaturedGauge).Name);
        rig.Bar.PickGauge(new BarGauge("absent", BarGaugeKind.Week));
        Assert.Equal("CPU", Some(rig.Bar.FeaturedGauge).Name);
    }

    [Fact]
    public async Task Jauge_AncienneGrande_ResteParmiLesPetitesMalgreLesAutresQuotas()
    {
        var rig = await BuildAsync(ConfigFor("claude", "codex", "autre"),
            Usage("claude", 20, 10), Usage("codex", 80, 70), Usage("autre", 95, 90));

        rig.Bar.PickGauge(new BarGauge("codex", BarGaugeKind.Week));

        var previous = new BarGauge("claude", BarGaugeKind.Session);
        Assert.Contains(Some(rig.Bar.Reservoir).Dots, ring => ring.Selection == previous);
        Assert.Contains(Some(rig.Bar.Rings).Others, ring => ring.Selection == previous);
    }

    [Fact]
    public async Task Jauge_BasculeAutoChangeLIa_RetourALaPlusConsommee()
    {
        var config = ConfigFor("claude", "codex");
        var rig = await BuildAsync(config, Usage("claude"), Usage("codex", 90, 27));
        rig.Bar.PickGauge(new BarGauge("claude", BarGaugeKind.Week));

        config.FocusMode = UsageFocusMode.AutoClosestToLimit;
        rig.Usage.ApplyConfig();

        Assert.Equal("codex", rig.Usage.SoloProviderId);
        Assert.Equal("90%", Some(rig.Bar.FeaturedGauge).Ring.Text);
    }

    [Theory]
    [InlineData(BarGaugeKind.Cpu)]
    [InlineData(BarGaugeKind.Week)]
    public async Task Jauge_GestePendantUnReleve_EmpecheLaReactivationAuto(BarGaugeKind kind)
    {
        var config = ConfigFor("claude", "codex");
        var provider = new StubProvider(Usage("claude"));
        var usage = new UsageViewModel(new UsageService([provider, new StubProvider(Usage("codex"))]), () => config, () => Now);
        await usage.RefreshAsync();
        var feed = new SystemLoadFeed(new FakeSystemLoadMonitor(), new ManualTimeProvider(), publishOn: null);
        using var bar = new UsageBarViewModel(usage, feed, () => Now);
        var gate = new TaskCompletionSource();
        provider.PendingRead = gate.Task;
        config.FocusMode = UsageFocusMode.AutoClosestToLimit;
        var refresh = usage.RefreshAsync();

        var chosen = new BarGauge(kind == BarGaugeKind.Cpu ? "" : "claude", kind);
        bar.PickGauge(chosen);
        gate.SetResult();
        await refresh;

        Assert.False(bar.IsAuto);
        Assert.Equal(chosen, Some(bar.FeaturedGauge).Ring.Selection);
    }

    [Fact]
    public async Task Etat_Full_LoadOnly_Empty()
    {
        var full = await BuildAsync(ConfigFor("claude"), Usage("claude"));
        Assert.Equal(BarState.Full, full.Bar.State);

        var sansQuota = Usage("gemini", session: null, week: null);
        var loadOnly = await BuildAsync(ConfigFor("gemini"), sansQuota);
        Assert.Equal(BarState.LoadOnly, loadOnly.Bar.State);

        var config = ConfigFor("gemini");
        config.ShowSystemLoad = false;
        var empty = await BuildAsync(config, sansQuota);
        Assert.Equal(BarState.Empty, empty.Bar.State);
        Assert.Equal("Aucun quota à afficher", empty.Bar.EmptyText);
        Assert.Null(empty.Bar.Load);
    }

    [Fact]
    public async Task Logos_OrdreDuBandeau_EligiblesSeulement()
    {
        var rig = await BuildAsync(ConfigFor("codex", "gemini", "claude", "demo"),
            Usage("codex", 27, null), Usage("gemini", null, null), Usage("claude"), Usage("demo", demo: true));

        Assert.Equal(["codex", "claude"], rig.Bar.Logos.Select(logo => logo.ProviderId).ToList());
        Assert.Equal("X", rig.Bar.Logos[0].Glyph);
        Assert.Equal("#123456", rig.Bar.Logos[0].Color);
        Assert.True(rig.Bar.Logos[0].IsSelected);
        Assert.Contains("codex — 27 %", rig.Bar.Logos[0].Tooltip);
        Assert.NotEqual("", rig.Bar.Logos[0].AccessibleName);
    }

    [Fact]
    public async Task PointAlerte_NonSelectionneAuSeuil()
    {
        var rig = await BuildAsync(ConfigFor("claude", "codex"), Usage("claude"), Usage("codex", 90, null));

        Assert.False(rig.Bar.Logos[0].HasAlertDot);
        Assert.True(rig.Bar.Logos[1].HasAlertDot);

        rig.Bar.Pick("codex");

        Assert.True(rig.Bar.Logos[1].IsSelected);
        Assert.False(rig.Bar.Logos[1].HasAlertDot);
        Assert.False(rig.Bar.Logos[0].HasAlertDot);
    }

    [Fact]
    public async Task PointAlerte_ReleveAncien_Eteint()
    {
        var rig = await BuildAsync(ConfigFor("claude", "codex"),
            Usage("claude"), Usage("codex", 95, null, staleSession: true));

        Assert.False(rig.Bar.Logos[1].HasAlertDot);
    }

    [Fact]
    public async Task Gris_LEmporteSurAlerte()
    {
        var rig = await BuildAsync(ConfigFor("claude"), Usage("claude", 95, 90, staleSession: true));

        Assert.Equal(UsageBarViewModel.StaleColor, rig.Bar.Columns[0].Color);
        Assert.Equal(UsageFormat.Critical, rig.Bar.Columns[1].Color);
    }

    [Fact]
    public async Task Colonnes_TroisQuotasPuisProcesseurMemoire()
    {
        var rig = await BuildAsync(ConfigFor("claude", "codex"), Usage("claude"), Usage("codex", 27, null));

        Assert.Equal(["62%", "44%", "27%", "23%", "61%"], rig.Bar.Columns.Select(c => c.Text).ToList());
        Assert.Equal(["CPU", "RAM"], rig.Bar.Columns.Skip(3).Select(c => c.Label).ToList());
        Assert.Equal(0.62, rig.Bar.Columns[0].Fraction, 3);
    }

    [Fact]
    public async Task Colonnes_SansQuota_DeuxBarres()
    {
        var rig = await BuildAsync(ConfigFor("gemini"), Usage("gemini", null, null));

        Assert.Equal(["CPU", "RAM"], rig.Bar.Columns.Select(c => c.Label).ToList());
        Assert.Empty(rig.Bar.Logos);
    }

    [Fact]
    public async Task Heros_CarteDeLIaMiseEnAvant_EtLignesDesAutres()
    {
        var rig = await BuildAsync(ConfigFor("claude", "codex"),
            Usage("claude", sessionResetsAt: new DateTime(2026, 8, 20, 14, 0, 0)), Usage("codex", 27, null));

        var card = Some(rig.Bar.HeroCard);
        Assert.Equal("claude", card.ProviderId);
        Assert.Equal("62%", card.Text);
        Assert.Contains("14", card.ResetText);
        Assert.Equal("44%", Some(card.SecondWindow).Text);
        Assert.Equal(["codex"], rig.Bar.HeroRows.Select(row => row.ProviderId).ToList());
        Assert.Equal("27%", rig.Bar.HeroRows[0].Text);
    }

    [Fact]
    public async Task Heros_NoticeIndisponible_Avertissement()
    {
        var rig = await BuildAsync(ConfigFor("claude", "codex"),
            Usage("claude", null, null, notice: "Plafond atteint — HTTP 429"), Usage("codex", 27, null));

        var card = Some(rig.Bar.HeroCard);
        Assert.True(card.IsWarning);
        Assert.Equal("Plafond atteint", card.NoticeTitle);
        Assert.Contains("HTTP 429", card.Tooltip);
        Assert.Contains("Plafond atteint — HTTP 429", rig.Bar.Logos[0].Tooltip);
        Assert.Equal("27%", rig.Bar.HeroRows[0].Text);
    }

    [Fact]
    public async Task Anneaux_SessionDehorsSemaineDedans_MemeSiSemainePlusConsommee()
    {
        var rig = await BuildAsync(ConfigFor("claude"), Usage("claude", 30, 80));

        var rings = Some(rig.Bar.Rings);
        Assert.Equal(0.30, Some(rings.Outer).Fraction, 3);
        Assert.Equal(0.80, Some(rings.Inner).Fraction, 3);
        Assert.Equal("80%", rings.CenterText);
    }

    [Fact]
    public async Task Anneaux_DeuxHebdomadaires_OrdreDeLInstantane()
    {
        var rig = await BuildAsync(ConfigFor("antigravity"),
            Usage("antigravity", 38, 75, sessionLabel: "Gemini", weekLabel: "Claude · GPT"));

        var rings = Some(rig.Bar.Rings);
        var outer = Some(rings.Outer);
        var inner = Some(rings.Inner);
        Assert.Equal(0.38, outer.Fraction, 3);
        Assert.Contains("Gemini", outer.Tooltip);
        Assert.Equal(0.75, inner.Fraction, 3);
        Assert.Contains("Claude · GPT", inner.Tooltip);
    }

    [Fact]
    public async Task Anneaux_LegendeDeuxFenetres_InfobulleDesDeuxNomsComplets()
    {
        var rig = await BuildAsync(ConfigFor("antigravity"),
            Usage("antigravity", 38, 75, sessionLabel: "Gemini", weekLabel: "Claude · GPT"));

        var rings = Some(rig.Bar.Rings);
        Assert.Contains("…", rings.Legend);
        Assert.Contains("Gemini", rings.LegendTooltip);
        Assert.Contains("Claude · GPT", rings.LegendTooltip);
    }

    [Fact]
    public async Task Anneaux_LegendeUneFenetre_InfobulleDeCetteFenetre()
    {
        var rig = await BuildAsync(ConfigFor("codex"), Usage("codex", null, 55, weekLabel: "Hebdomadaire de l'équipe"));

        var rings = Some(rig.Bar.Rings);
        Assert.Equal(Some(rings.Outer).Tooltip, rings.LegendTooltip);
    }

    [Fact]
    public async Task Anneaux_UneSeuleFenetre_AnneauSimple()
    {
        var rig = await BuildAsync(ConfigFor("codex"), Usage("codex", null, 55));

        var rings = Some(rig.Bar.Rings);
        Assert.Equal(0.55, Some(rings.Outer).Fraction, 3);
        Assert.Null(rings.Inner);
        Assert.Equal("55%", rings.CenterText);
    }

    [Fact]
    public async Task Reservoir_PastilleVideSiFenetreManquante()
    {
        var rig = await BuildAsync(ConfigFor("codex"), Usage("codex", 40, null));

        var reservoir = Some(rig.Bar.Reservoir);
        Assert.True(reservoir.HasLevel);
        Assert.Equal(0.40, reservoir.Level, 3);
        Assert.Equal(4, reservoir.Dots.Count);
        Assert.True(reservoir.Dots[0].IsEmpty);
        Assert.True(reservoir.Dots[1].IsEmpty);
        Assert.False(reservoir.Dots[2].IsEmpty);
        Assert.Equal("23%", reservoir.Dots[2].Text);
        Assert.Equal("61%", reservoir.Dots[3].Text);
    }

    [Fact]
    public async Task Courbe_SeulementHerosEtCasesCochees()
    {
        var config = ConfigFor("claude");
        var rig = await BuildAsync(config, Usage("claude"));

        foreach (var style in Enum.GetValues<SideStripStyle>())
        {
            config.BarStyle = style;
            rig.Usage.ApplyConfig();
            Assert.Equal(style == SideStripStyle.HeroStack, rig.Bar.ShowCurve);
        }

        config.BarStyle = SideStripStyle.HeroStack;
        config.ShowLoadHistory = false;
        rig.Usage.ApplyConfig();
        Assert.False(rig.Bar.ShowCurve);

        config.ShowLoadHistory = true;
        config.ShowSystemLoad = false;
        rig.Usage.ApplyConfig();
        Assert.False(rig.Bar.ShowCurve);
        Assert.Null(rig.Bar.Load);
    }

    [Fact]
    public async Task Courbe_ProcesseurInconnu_Trou()
    {
        var rig = await BuildAsync(ConfigFor("claude"), new SystemLoad(null, 61), Usage("claude"));

        Assert.Equal("—", Some(rig.Bar.Load).CpuText);
        Assert.Equal(0, rig.Bar.Load.CpuFraction);
        Assert.Equal([null], rig.Bar.Load.CpuSeries);

        rig.Monitor.Value = new SystemLoad(40, 62);
        rig.Time.Advance(TimeSpan.FromSeconds(5));
        await rig.Feed.PendingSample.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal([null, 40], Some(rig.Bar.Load).CpuSeries);
        Assert.Equal([61, 62], rig.Bar.Load.MemorySeries);
        Assert.Equal("40%", rig.Bar.Load.CpuText);
    }

    [Fact]
    public async Task Auto_LibelleAffiche()
    {
        var config = ConfigFor("claude", "codex");
        config.FocusMode = UsageFocusMode.AutoClosestToLimit;
        var rig = await BuildAsync(config, Usage("claude"), Usage("codex", 80, null));

        Assert.True(rig.Bar.IsAuto);
        Assert.Equal("AUTO", rig.Bar.AutoLabel);
        Assert.True(rig.Bar.Logos[1].IsSelected);

        rig.Bar.Pick("claude");

        Assert.False(rig.Bar.IsAuto);
        Assert.Equal("", rig.Bar.AutoLabel);
        Assert.True(rig.Bar.Logos[0].IsSelected);
    }

    [Fact]
    public async Task RemiseAZeroInconnue_PasDansLInfobulle()
    {
        var sansReset = await BuildAsync(ConfigFor("claude"), Usage("claude"));
        Assert.Equal("", Some(sansReset.Bar.HeroCard).ResetText);
        Assert.DoesNotContain("Remise à zéro", sansReset.Bar.HeroCard.Tooltip);
        Assert.DoesNotContain("Remise à zéro", sansReset.Bar.Columns[0].Tooltip);

        var avecReset = await BuildAsync(ConfigFor("claude"),
            Usage("claude", sessionResetsAt: new DateTime(2026, 8, 20, 14, 0, 0)));
        Assert.Contains("Remise à zéro", Some(avecReset.Bar.HeroCard).Tooltip);
    }

    [Fact]
    public async Task NomsLongs_InfobulleComplete()
    {
        const string nom = "Fournisseur au nom beaucoup trop long";
        const string fenetre = "Fenêtre hebdomadaire très longue";
        var rig = await BuildAsync(ConfigFor("long"), Usage("long", 62, 44, name: nom, sessionLabel: fenetre));

        Assert.Contains(nom, rig.Bar.Logos[0].Tooltip);
        var card = Some(rig.Bar.HeroCard);
        Assert.True(card.Name.Length < nom.Length);
        Assert.EndsWith("…", card.Name);
        Assert.Contains(nom, card.Tooltip);
        Assert.True(card.Label.Length < fenetre.Length);
        Assert.Contains(fenetre, card.Tooltip);
        Assert.Contains(fenetre, rig.Bar.Columns[0].Tooltip);
    }

    [Fact]
    public async Task CanCycle_FauxSousDeuxEligibles()
    {
        var un = await BuildAsync(ConfigFor("claude", "gemini", "demo"),
            Usage("claude"), Usage("gemini", null, null), Usage("demo", demo: true));
        Assert.False(un.Bar.CanCycle);

        var deux = await BuildAsync(ConfigFor("claude", "codex"), Usage("claude"), Usage("codex", 27, null));
        Assert.True(deux.Bar.CanCycle);
    }

    [Theory]
    [InlineData("Plafond atteint — réessai dans 5 min", "Plafond atteint")]
    [InlineData("Quota coupé", "Quota coupé")]
    public async Task Notice_TitreCourtDansTousLesStyles(string notice, string titre)
    {
        var rig = await BuildAsync(ConfigFor("claude", "codex"),
            Usage("claude", null, null, notice: notice), Usage("codex", 27, null));

        Assert.Equal(titre, Some(rig.Bar.HeroCard).NoticeTitle);
        var column = rig.Bar.Columns[0];
        Assert.True(column.IsWarning);
        Assert.Equal("⚠", column.Text);
        Assert.Equal(titre, column.Label);
        var outer = Some(Some(rig.Bar.Rings).Outer);
        Assert.True(outer.IsWarning);
        Assert.Equal(titre, outer.Label);
        var reservoir = Some(rig.Bar.Reservoir);
        Assert.False(reservoir.HasLevel);
        Assert.Equal("⚠", reservoir.LevelText);
        Assert.Equal(titre, reservoir.Label);
        Assert.Contains(notice, reservoir.Tooltip);
    }

    [Fact]
    public async Task Pick_UnSeulEligible_RienNeChangeEtAutoConserve()
    {
        var config = ConfigFor("claude", "gemini");
        config.FocusMode = UsageFocusMode.AutoClosestToLimit;
        var rig = await BuildAsync(config, Usage("claude", 50, null), Usage("gemini", null, null));
        Assert.False(rig.Bar.CanCycle);
        Assert.True(rig.Bar.IsAuto);

        rig.Bar.Pick("claude");

        Assert.True(rig.Bar.IsAuto);
        Assert.Equal("AUTO", rig.Bar.AutoLabel);
    }

    [Fact]
    public async Task Cycle_SensEtDelegation()
    {
        var rig = await BuildAsync(ConfigFor("a", "b", "c"), Usage("a"), Usage("b"), Usage("c"));
        Assert.Equal("a", rig.Usage.SoloProviderId);

        rig.Bar.Cycle(+1);
        Assert.Equal(["b"], rig.Bar.Logos.Where(logo => logo.IsSelected).Select(logo => logo.ProviderId).ToList());
        Assert.Equal("b", rig.Usage.SoloProviderId);

        rig.Bar.Cycle(-1);
        Assert.Equal(["a"], rig.Bar.Logos.Where(logo => logo.IsSelected).Select(logo => logo.ProviderId).ToList());

        rig.Bar.Cycle(-1);
        Assert.Equal(["c"], rig.Bar.Logos.Where(logo => logo.IsSelected).Select(logo => logo.ProviderId).ToList());
    }

    [Fact]
    public async Task Cycle_SuspendLAuto()
    {
        var config = ConfigFor("a", "b", "c");
        config.FocusMode = UsageFocusMode.AutoClosestToLimit;
        var rig = await BuildAsync(config, Usage("a", 10, null), Usage("b", 80, null), Usage("c", 30, null));
        Assert.True(rig.Bar.IsAuto);
        Assert.Equal("b", rig.Usage.SoloProviderId);

        rig.Bar.Cycle(+1);

        Assert.False(rig.Bar.IsAuto);
        Assert.Equal("", rig.Bar.AutoLabel);
        Assert.Equal("c", rig.Usage.SoloProviderId);
    }

    [Fact]
    public async Task Charge_Mesure_RecalculeSeulementLaCharge()
    {
        var rig = await BuildAsync(ConfigFor("claude", "codex"), Usage("claude"), Usage("codex", 27, null));
        var logos = rig.Bar.Logos;
        var heroCard = rig.Bar.HeroCard;
        var heroRows = rig.Bar.HeroRows;
        var rings = rig.Bar.Rings;
        var quotaColumn = rig.Bar.Columns[0];
        var quotaDot = Some(rig.Bar.Reservoir).Dots[0];
        var notified = new System.Collections.Concurrent.ConcurrentQueue<string?>();
        rig.Bar.PropertyChanged += (_, e) => notified.Enqueue(e.PropertyName);

        rig.Monitor.Value = new SystemLoad(40, 62);
        rig.Time.Advance(TimeSpan.FromSeconds(5));
        await rig.Feed.PendingSample.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Same(logos, rig.Bar.Logos);
        Assert.Same(heroCard, rig.Bar.HeroCard);
        Assert.Same(heroRows, rig.Bar.HeroRows);
        Assert.Same(rings, rig.Bar.Rings);
        Assert.Same(quotaColumn, rig.Bar.Columns[0]);
        Assert.Same(quotaDot, Some(rig.Bar.Reservoir).Dots[0]);
        Assert.Equal("40%", rig.Bar.Columns[3].Text);
        Assert.Equal("62%", rig.Bar.Columns[4].Text);
        Assert.Equal("40%", Some(rig.Bar.Reservoir).Dots[2].Text);
        Assert.Equal(2, Some(rig.Bar.Load).CpuSeries.Count);

        var names = notified.ToList();
        Assert.Contains(nameof(UsageBarViewModel.Load), names);
        Assert.Contains(nameof(UsageBarViewModel.Columns), names);
        Assert.Contains(nameof(UsageBarViewModel.Reservoir), names);
        Assert.DoesNotContain(string.Empty, names);
        Assert.DoesNotContain(nameof(UsageBarViewModel.Logos), names);
        Assert.DoesNotContain(nameof(UsageBarViewModel.HeroCard), names);
        Assert.DoesNotContain(nameof(UsageBarViewModel.HeroRows), names);
        Assert.DoesNotContain(nameof(UsageBarViewModel.Rings), names);
    }

    private sealed class GatedProvider(AiUsage usage, TaskCompletionSource gate) : IUsageProvider
    {
        public string Id => usage.ProviderId;
        public string Name => Id;
        public AiProbe Probe() => new() { Available = true, DisplayName = Id };

        public async Task<AiUsage?> ReadAsync(CancellationToken ct)
        {
            await gate.Task;
            return usage;
        }
    }

    [Fact]
    public async Task IsLoading_SuitLeReleve_EtLaBarreRestePresente()
    {
        Loc.SetCulture(System.Globalization.CultureInfo.GetCultureInfo("fr"));
        var gate = new TaskCompletionSource();
        var service = new UsageService([new GatedProvider(Usage("claude"), gate)]);
        var usage = new UsageViewModel(service, () => ConfigFor("claude"), () => Now);
        var feed = new SystemLoadFeed(new FakeSystemLoadMonitor(), new ManualTimeProvider(), publishOn: null);
        var bar = new UsageBarViewModel(usage, feed, () => Now);
        var notified = new List<string?>();
        bar.PropertyChanged += (_, e) => notified.Add(e.PropertyName);
        Assert.False(bar.IsLoading);

        var refresh = usage.RefreshAsync();

        Assert.True(bar.IsLoading);
        Assert.Equal(BarState.LoadOnly, bar.State);
        Assert.Contains(nameof(UsageBarViewModel.IsLoading), notified);

        gate.SetResult();
        await refresh;

        Assert.False(bar.IsLoading);
        Assert.Equal(BarState.Full, bar.State);
    }

    [Fact]
    public async Task Colonnes_NombreSeul_ProduitParLeModele()
    {
        var rig = await BuildAsync(ConfigFor("claude", "codex"), Usage("claude"), Usage("codex", 27, null));

        Assert.Equal(["62", "44", "27", "23", "61"], rig.Bar.Columns.Select(c => c.NumberText).ToList());
        Assert.Equal(["", "", "", "Brush.Text", "Brush.Text"], rig.Bar.Columns.Select(c => c.NumberBrushKey).ToList());
        Assert.Equal(["23", "61"], Some(rig.Bar.Reservoir).Dots.Skip(2).Select(d => d.NumberText).ToList());
    }

    [Fact]
    public async Task Colonnes_ProcesseurInconnu_NombreEnTiret()
    {
        var rig = await BuildAsync(ConfigFor("claude"), new SystemLoad(null, 61), Usage("claude"));

        Assert.Equal("—", rig.Bar.Columns[^2].NumberText);
    }

    [Fact]
    public async Task Colonnes_LibelleDeTroisCaracteresAuPlus()
    {
        var rig = await BuildAsync(ConfigFor("claude"), Usage("claude", sessionLabel: "Claude · GPT"));

        Assert.All(rig.Bar.Columns, column => Assert.True(column.Label.Length <= 3, column.Label));
        Assert.Contains("Claude · GPT", rig.Bar.Columns[0].Tooltip);
    }

    [Fact]
    public async Task Heros_LigneDUnAutreFournisseur_PorteLeNomDuFournisseur()
    {
        var antigravity = Usage("antigravity", 88, 18, name: "Antigravity", sessionLabel: "Claude");
        var rig = await BuildAsync(ConfigFor("claude", "antigravity"), Usage("claude"), antigravity);

        var row = rig.Bar.HeroRows.Single();
        Assert.Equal("antigravity", row.ProviderId);
        Assert.StartsWith("Antig", row.Label);
        Assert.DoesNotContain("Claude", row.Label);
        Assert.Contains("Claude", row.Tooltip);
    }

    [Fact]
    public async Task Textes_SeparateursProduitsParLeModele()
    {
        var config = ConfigFor("claude");
        config.FocusMode = UsageFocusMode.AutoClosestToLimit;
        var rig = await BuildAsync(config, Usage("claude", sessionResetsAt: Now.AddHours(2)));

        Assert.Equal(" · AUTO", rig.Bar.AutoSuffix);
        Assert.StartsWith("↻ ", Some(rig.Bar.HeroCard).ResetLabel);
        Assert.Contains(" / ", Some(rig.Bar.Rings).Legend);
        Assert.Equal("CPU / RAM", Some(rig.Bar.Load).Legend);
    }

    [Fact]
    public async Task Charge_Mesure_GardeLesElementsDeQuotaDansLesCollectionsLiees()
    {
        var rig = await BuildAsync(ConfigFor("claude", "codex"), Usage("claude"), Usage("codex", 27, null));
        var columns = rig.Bar.Columns;
        var logos = rig.Bar.Logos;
        var dots = Some(rig.Bar.Reservoir).Dots;

        rig.Monitor.Value = new SystemLoad(40, 62);
        rig.Time.Advance(TimeSpan.FromSeconds(5));
        await rig.Feed.PendingSample.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Same(columns, rig.Bar.Columns);
        Assert.Same(logos, rig.Bar.Logos);
        Assert.Same(dots, Some(rig.Bar.Reservoir).Dots);
    }
}
