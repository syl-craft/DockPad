using System.Globalization;
using DockPad.Models;
using DockPad.Services.Localization;
using DockPad.Services.StreamDock;
using DockPad.Services.Usage;

namespace DockPad.Tests.StreamDock;

/// <summary>
/// Sélection et ordre des quotas, relevés anciens, identité et prochaine remise à zéro.
/// Libellés traduits ou fournis, seuil d'alerte et adaptation au nombre d'écrans.
/// </summary>
public class UsageDisplayPlannerTests
{
    public UsageDisplayPlannerTests()
    {
        Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));
    }

    [Fact]
    public void UnFournisseurDeuxFenetres_FenetresPuisIdentite()
    {
        var sessionReset = new DateTime(2026, 10, 26, 18, 0, 0);
        var weekReset = sessionReset.AddHours(-2);
        var usage = Usage("claude", name: "Claude Code",
            session: new UsageWindow { UsedPct = 20, ResetsAt = sessionReset },
            week: new UsageWindow { UsedPct = 90, ResetsAt = weekReset });

        var displays = UsageDisplayPlanner.Plan([usage], new UsageConfig(), displayCount: 3);

        Assert.Equal(3, displays.Count);
        Assert.Equal(new UsageDisplay.Gauge("claude", "5 h", 20, UsageFormat.Ok, IsStale: false), displays[0]);
        Assert.Equal(new UsageDisplay.Gauge("claude", "Semaine", 90, UsageFormat.Critical, IsStale: false), displays[1]);
        Assert.Equal(new UsageDisplay.Identity("claude", "Claude Code", weekReset), displays[2]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnFournisseurUneFenetre_FenetreIdentiteNoir(bool session)
    {
        var reset = new DateTime(2026, 10, 26, 18, 0, 0);
        var window = new UsageWindow { UsedPct = 62, ResetsAt = reset };
        var usage = Usage("codex", name: "Codex", session: session ? window : null, week: session ? null : window);

        var displays = UsageDisplayPlanner.Plan([usage], new UsageConfig(), displayCount: 3);

        Assert.Equal(3, displays.Count);
        Assert.Equal(new UsageDisplay.Gauge("codex", session ? "5 h" : "Semaine", 62, UsageFormat.High, IsStale: false), displays[0]);
        Assert.Equal(new UsageDisplay.Identity("codex", "Codex", reset), displays[1]);
        Assert.Null(displays[2]);
    }

    [Fact]
    public void Plusieurs_TriesParConsommation()
    {
        AiUsage[] usages =
        [
            Usage("claude", session: new UsageWindow { UsedPct = 20 }, week: new UsageWindow { UsedPct = 80 }),
            Usage("codex", session: new UsageWindow { UsedPct = 92 }, week: new UsageWindow { UsedPct = 30 }),
        ];

        var displays = UsageDisplayPlanner.Plan(usages, new UsageConfig(), displayCount: 3);

        Assert.Equal(3, displays.Count);
        Assert.Equal(new UsageDisplay.Gauge("codex", "5 h", 92, UsageFormat.Critical, IsStale: false), displays[0]);
        Assert.Equal(new UsageDisplay.Gauge("claude", "Semaine", 80, UsageFormat.High, IsStale: false), displays[1]);
        Assert.Null(displays[2]);
    }

    [Fact]
    public void PlusDeTrois_LesTroisPlusCharges()
    {
        AiUsage[] usages =
        [
            Usage("a", session: new UsageWindow { UsedPct = 30 }),
            Usage("b", session: new UsageWindow { UsedPct = 70 }),
            Usage("c", session: new UsageWindow { UsedPct = 40 }),
            Usage("d", week: new UsageWindow { UsedPct = 95 }),
        ];

        var displays = UsageDisplayPlanner.Plan(usages, new UsageConfig(), displayCount: 3);

        Assert.Equal(new[] { "d", "b", "c" }, displays.Select(d => Assert.IsType<UsageDisplay.Gauge>(d).ProviderId));
    }

    [Fact]
    public void Egalite_OrdreDuBandeau()
    {
        AiUsage[] usages =
        [
            Usage("z", week: new UsageWindow { UsedPct = 70 }),
            Usage("a", session: new UsageWindow { UsedPct = 70 }),
            Usage("m", session: new UsageWindow { UsedPct = 70 }),
            Usage("b", session: new UsageWindow { UsedPct = 70 }),
        ];

        var displays = UsageDisplayPlanner.Plan(usages, new UsageConfig(), displayCount: 3);

        Assert.Equal(new[] { "z", "a", "m" }, displays.Select(d => Assert.IsType<UsageDisplay.Gauge>(d).ProviderId));
    }

    [Fact]
    public void FenetreAncienne_GriseeEtCompteDansLeTri()
    {
        var observed = new DateTime(2026, 10, 25, 12, 0, 0);
        AiUsage[] usages =
        [
            Usage("claude", session: new UsageWindow { UsedPct = 40, ObservedAt = observed },
                week: new UsageWindow { UsedPct = 70 }),
            Usage("codex", session: new UsageWindow { UsedPct = 20 },
                week: new UsageWindow { UsedPct = 95, ObservedAt = observed }),
        ];

        var displays = UsageDisplayPlanner.Plan(usages, new UsageConfig(), displayCount: 3);

        Assert.Equal(new UsageDisplay.Gauge("codex", "Semaine", 95, UsageFormat.Critical, IsStale: true), displays[0]);
        Assert.Equal(new UsageDisplay.Gauge("claude", "Semaine", 70, UsageFormat.High, IsStale: false), displays[1]);
        Assert.Null(displays[2]);
    }

    [Fact]
    public void SansQuota_Ignore()
    {
        var usage = Usage("claude", session: new UsageWindow { UsedPct = 25 });

        var displays = UsageDisplayPlanner.Plan([Usage("copilot"), usage], new UsageConfig(), displayCount: 3);

        Assert.Equal(3, displays.Count);
        Assert.Equal("claude", Assert.IsType<UsageDisplay.Gauge>(displays[0]).ProviderId);
        Assert.Equal(new UsageDisplay.Identity("claude", "claude", null), displays[1]);
        Assert.Null(displays[2]);
    }

    [Fact]
    public void Demo_Ignore()
    {
        var demo = Usage("simulation", session: new UsageWindow { UsedPct = 100 }, demo: true);
        var usage = Usage("claude", week: new UsageWindow { UsedPct = 25 });

        var displays = UsageDisplayPlanner.Plan([demo, usage], new UsageConfig(), displayCount: 3);

        Assert.Equal(3, displays.Count);
        Assert.Equal("claude", Assert.IsType<UsageDisplay.Gauge>(displays[0]).ProviderId);
        Assert.IsType<UsageDisplay.Identity>(displays[1]);
        Assert.Null(displays[2]);
    }

    [Fact]
    public void BandeauDesactive_ToutNoir()
    {
        var usage = Usage("claude", session: new UsageWindow { UsedPct = 80 }, week: new UsageWindow { UsedPct = 30 });

        var displays = UsageDisplayPlanner.Plan([usage], new UsageConfig { Enabled = false }, displayCount: 3);

        Assert.Equal(3, displays.Count);
        Assert.All(displays, display => Assert.Null(display));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LibelleDeFenetre_PrioritaireSurLeNomParDefaut(bool plusieurs)
    {
        var usage = Usage("antigravity", session: new UsageWindow { UsedPct = 38, Label = "Gemini" },
            week: new UsageWindow { UsedPct = 75, Label = "Claude · GPT" });
        AiUsage[] usages = plusieurs
            ? [usage, Usage("codex", week: new UsageWindow { UsedPct = 10 })]
            : [usage];

        var displays = UsageDisplayPlanner.Plan(usages, new UsageConfig(), displayCount: 3);

        Assert.Equal(plusieurs ? "Claude · GPT" : "Gemini", Assert.IsType<UsageDisplay.Gauge>(displays[0]).Label);
        if (plusieurs == false)
            Assert.Equal("Claude · GPT", Assert.IsType<UsageDisplay.Gauge>(displays[1]).Label);
    }

    [Theory]
    [InlineData("fr", "5 h", "Semaine")]
    [InlineData("en", "5 h", "Week")]
    [InlineData("qps-Ploc", "5 h", "53m41n3")]
    public void LibellesParDefaut_SuiventLaLangue(string langue, string session, string week)
    {
        Loc.SetCulture(CultureInfo.GetCultureInfo(langue));
        var usage = Usage("claude", session: new UsageWindow { UsedPct = 10 }, week: new UsageWindow { UsedPct = 20 });

        var displays = UsageDisplayPlanner.Plan([usage], new UsageConfig(), displayCount: 3);

        Assert.Equal(session, Assert.IsType<UsageDisplay.Gauge>(displays[0]).Label);
        Assert.Equal(week, Assert.IsType<UsageDisplay.Gauge>(displays[1]).Label);
    }

    [Theory]
    [InlineData(2, 1, 1)]
    [InlineData(1, 2, 1)]
    [InlineData(null, 2, 2)]
    [InlineData(1, null, 1)]
    [InlineData(null, null, null)]
    public void Identite_ProchaineRemiseAZeroLaPlusProche(int? sessionHours, int? weekHours, int? expectedHours)
    {
        var date = new DateTime(2026, 10, 26, 12, 0, 0);
        var usage = Usage("claude",
            session: new UsageWindow { UsedPct = 20, ResetsAt = sessionHours == null ? null : date.AddHours(sessionHours.Value) },
            week: new UsageWindow { UsedPct = 30, ResetsAt = weekHours == null ? null : date.AddHours(weekHours.Value) });

        var displays = UsageDisplayPlanner.Plan([usage], new UsageConfig(), displayCount: 3);

        DateTime? expected = expectedHours == null ? null : date.AddHours(expectedHours.Value);
        Assert.Equal(expected, Assert.IsType<UsageDisplay.Identity>(displays[2]).NextReset);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AucunFournisseurRetenu_ToutNoir(int scenario)
    {
        AiUsage[] usages = scenario switch
        {
            1 => [Usage("copilot")],
            2 => [Usage("demo", session: new UsageWindow { UsedPct = 50 }, demo: true)],
            _ => [],
        };

        var displays = UsageDisplayPlanner.Plan(usages, new UsageConfig(), displayCount: 3);

        Assert.Equal(3, displays.Count);
        Assert.All(displays, display => Assert.Null(display));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Couleur_RespecteLeSeuilConfigure(bool plusieurs)
    {
        var usage = Usage("claude", session: new UsageWindow { UsedPct = 50 }, week: new UsageWindow { UsedPct = 20 });
        AiUsage[] usages = plusieurs
            ? [usage, Usage("codex", week: new UsageWindow { UsedPct = 15 })]
            : [usage];

        var displays = UsageDisplayPlanner.Plan(usages, new UsageConfig { AlertThreshold = 50 }, displayCount: 3);

        Assert.Equal(UsageFormat.Critical, Assert.IsType<UsageDisplay.Gauge>(displays[0]).Color);
    }

    [Fact]
    public void UnFournisseurAncien_EtatPropreAChaqueFenetre()
    {
        var usage = Usage("codex", session: new UsageWindow { UsedPct = 15, ObservedAt = new DateTime(2026, 10, 25) },
            week: new UsageWindow { UsedPct = 30 });

        var displays = UsageDisplayPlanner.Plan([usage], new UsageConfig(), displayCount: 3);

        Assert.True(Assert.IsType<UsageDisplay.Gauge>(displays[0]).IsStale);
        Assert.False(Assert.IsType<UsageDisplay.Gauge>(displays[1]).IsStale);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(4, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(4, true)]
    public void NombreDEcrans_TronqueOuCompleteAvecDuNoir(int displayCount, bool plusieurs)
    {
        var usage = Usage("claude", session: new UsageWindow { UsedPct = 80 }, week: new UsageWindow { UsedPct = 20 });
        AiUsage[] usages = plusieurs
            ? [usage, Usage("codex", week: new UsageWindow { UsedPct = 70 }),
                Usage("antigravity", week: new UsageWindow { UsedPct = 60 }), Usage("a", week: new UsageWindow { UsedPct = 50 })]
            : [usage];

        var displays = UsageDisplayPlanner.Plan(usages, new UsageConfig(), displayCount);

        Assert.Equal(displayCount, displays.Count);
        if (displayCount > 0)
            Assert.Equal("claude", Assert.IsType<UsageDisplay.Gauge>(displays[0]).ProviderId);
        if (displayCount > 1)
            Assert.Equal(plusieurs ? "codex" : "claude", Assert.IsType<UsageDisplay.Gauge>(displays[1]).ProviderId);
        if (displayCount > 2)
        {
            if (plusieurs)
                Assert.Equal("antigravity", Assert.IsType<UsageDisplay.Gauge>(displays[2]).ProviderId);
            else
                Assert.IsType<UsageDisplay.Identity>(displays[2]);
        }
        if (displayCount > 3)
            Assert.Null(displays[3]);
    }

    private static AiUsage Usage(string id, UsageWindow? session = null, UsageWindow? week = null,
        string? name = null, bool demo = false) => new()
    {
        ProviderId = id,
        Name = name ?? id,
        Glyph = "X",
        AccentColor = "#123456",
        Session = session,
        Week = week,
        IsDemo = demo,
    };
}
