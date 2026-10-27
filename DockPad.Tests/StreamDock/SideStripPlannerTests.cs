using System.Globalization;
using DockPad.Models;
using DockPad.Services.Localization;
using DockPad.Services.StreamDock;

namespace DockPad.Tests.StreamDock;

/// <summary>
/// Toutes les fenêtres triées, exclusions et charge du PC avec ou sans quotas.
/// Noms affichés, libellés traduits, remises à zéro, seuil d'alerte et relevés anciens par fenêtre.
/// </summary>
public class SideStripPlannerTests
{
    public SideStripPlannerTests()
    {
        Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));
    }

    [Fact]
    public void Plan_ToutesLesFenetres_TrieesDeLaPlusConsommee()
    {
        var sessionReset = new DateTime(2026, 10, 27, 18, 0, 0);
        var weekReset = sessionReset.AddDays(3);
        AiUsage[] usages =
        [
            Usage("claude", name: "Claude Code",
                session: new UsageWindow { UsedPct = 70, ResetsAt = sessionReset },
                week: new UsageWindow { UsedPct = 70, ResetsAt = weekReset }),
            Usage("codex", name: "Codex",
                session: new UsageWindow { UsedPct = 90 }, week: new UsageWindow { UsedPct = 70 }),
            Usage("antigravity", name: "Antigravity",
                session: new UsageWindow { UsedPct = 70 }, week: new UsageWindow { UsedPct = 10 }),
        ];

        var content = SideStripPlanner.Plan(usages, new UsageConfig(), new SystemLoad(CpuPct: null, MemoryPct: 61));

        QuotaGauge[] expected =
        [
            new("codex", "Codex", "5 h", 90, IsAlert: true, IsStale: false, ResetsAt: null, WindowKind: UsageWindowKind.Session),
            new("claude", "Claude Code", "5 h", 70, IsAlert: false, IsStale: false, ResetsAt: sessionReset, WindowKind: UsageWindowKind.Session),
            new("claude", "Claude Code", "Semaine", 70, IsAlert: false, IsStale: false, ResetsAt: weekReset, WindowKind: UsageWindowKind.Week),
            new("codex", "Codex", "Semaine", 70, IsAlert: false, IsStale: false, ResetsAt: null, WindowKind: UsageWindowKind.Week),
            new("antigravity", "Antigravity", "5 h", 70, IsAlert: false, IsStale: false, ResetsAt: null, WindowKind: UsageWindowKind.Session),
            new("antigravity", "Antigravity", "Semaine", 10, IsAlert: false, IsStale: false, ResetsAt: null, WindowKind: UsageWindowKind.Week),
        ];
        Assert.Equal(expected, content.Quotas);
        Assert.Null(content.CpuPct);
        Assert.Equal(61, content.MemoryPct);
    }

    [Fact]
    public void Plan_Exclut_DemoEtSansQuota()
    {
        AiUsage[] usages =
        [
            Usage("demo", session: new UsageWindow { UsedPct = 100 },
                week: new UsageWindow { UsedPct = 100 }, demo: true),
            Usage("copilot"),
            Usage("gemini"),
            Usage("codex", name: "Mon Codex", week: new UsageWindow { UsedPct = 25 }),
        ];

        var content = SideStripPlanner.Plan(usages, new UsageConfig(), new SystemLoad(CpuPct: 42, MemoryPct: 61));

        var quota = Assert.Single(content.Quotas);
        Assert.Equal(new QuotaGauge("codex", "Mon Codex", "Semaine", 25,
            IsAlert: false, IsStale: false, ResetsAt: null, WindowKind: UsageWindowKind.Week), quota);
        Assert.Equal(42, content.CpuPct);
        Assert.Equal(61, content.MemoryPct);
    }

    [Theory]
    [InlineData(42, 61)]
    [InlineData(null, 61)]
    [InlineData(42, null)]
    [InlineData(null, null)]
    public void Plan_BandeauDesactive_SeulementProcesseurEtMemoire(int? cpuPct, int? memoryPct)
    {
        var usage = Usage("claude", session: new UsageWindow { UsedPct = 90 },
            week: new UsageWindow { UsedPct = 70 });

        var content = SideStripPlanner.Plan([usage], new UsageConfig { Enabled = false },
            new SystemLoad(cpuPct, memoryPct));

        Assert.Empty(content.Quotas);
        Assert.Equal(cpuPct, content.CpuPct);
        Assert.Equal(memoryPct, content.MemoryPct);
    }

    [Theory]
    [InlineData("fr", "5 h", "Semaine")]
    [InlineData("en", "5 h", "Week")]
    [InlineData("qps-Ploc", "5 h", "53m41n3")]
    public void Plan_NomDeFenetre_LabelSinon5hOuSemaine(string langue, string session, string week)
    {
        Loc.SetCulture(CultureInfo.GetCultureInfo(langue));
        AiUsage[] usages =
        [
            Usage("claude", session: new UsageWindow { UsedPct = 20 },
                week: new UsageWindow { UsedPct = 10, Label = "" }),
            Usage("antigravity", session: new UsageWindow { UsedPct = 40, Label = "Gemini" },
                week: new UsageWindow { UsedPct = 30, Label = "Claude · GPT" }),
        ];

        var content = SideStripPlanner.Plan(usages, new UsageConfig(), new SystemLoad(CpuPct: 42, MemoryPct: 61));

        Assert.Equal(new[] { "Gemini", "Claude · GPT", session, week },
            content.Quotas.Select(quota => quota.WindowLabel));
        Assert.Equal(new[] { UsageWindowKind.Session, UsageWindowKind.Week, UsageWindowKind.Session, UsageWindowKind.Week },
            content.Quotas.Select(quota => quota.WindowKind));
    }

    [Theory]
    [InlineData(80, 20, true)]
    [InlineData(79, 20, false)]
    [InlineData(50, 50, true)]
    [InlineData(49, 50, false)]
    [InlineData(100, 0, true)]
    [InlineData(99, 0, false)]
    [InlineData(0, 100, true)]
    public void Plan_Alerte_RegleDuBandeau(int usedPct, int threshold, bool expectedAlert)
    {
        var usage = Usage("claude", session: new UsageWindow { UsedPct = usedPct },
            week: new UsageWindow { UsedPct = usedPct });

        var content = SideStripPlanner.Plan([usage], new UsageConfig { AlertThreshold = threshold },
            new SystemLoad(CpuPct: 42, MemoryPct: 61));

        Assert.Equal(2, content.Quotas.Count);
        Assert.All(content.Quotas, quota => Assert.Equal(expectedAlert, quota.IsAlert));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Plan_ReleveAncien_Grise(bool sessionAncienne)
    {
        var observed = new DateTime(2026, 10, 26, 12, 0, 0);
        var usage = Usage("codex",
            session: new UsageWindow { UsedPct = 95, ObservedAt = sessionAncienne ? observed : null },
            week: new UsageWindow { UsedPct = 30, ObservedAt = sessionAncienne ? null : observed });
        var autre = Usage("claude", session: new UsageWindow { UsedPct = 80 });

        var content = SideStripPlanner.Plan([autre, usage], new UsageConfig { AlertThreshold = 20 },
            new SystemLoad(CpuPct: 42, MemoryPct: 61));

        QuotaGauge[] expected =
        [
            new("codex", "codex", "5 h", 95, IsAlert: true, IsStale: sessionAncienne, ResetsAt: null, WindowKind: UsageWindowKind.Session),
            new("claude", "claude", "5 h", 80, IsAlert: true, IsStale: false, ResetsAt: null, WindowKind: UsageWindowKind.Session),
            new("codex", "codex", "Semaine", 30, IsAlert: false, IsStale: sessionAncienne == false, ResetsAt: null, WindowKind: UsageWindowKind.Week),
        ];
        Assert.Equal(expected, content.Quotas);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Plan_SansQuota_ConserveLesMesures(int scenario)
    {
        AiUsage[] usages = scenario switch
        {
            1 => [Usage("copilot")],
            2 => [Usage("demo", session: new UsageWindow { UsedPct = 50 }, demo: true)],
            _ => [],
        };

        var content = SideStripPlanner.Plan(usages, new UsageConfig(), new SystemLoad(CpuPct: 42, MemoryPct: null));

        Assert.Empty(content.Quotas);
        Assert.Equal(42, content.CpuPct);
        Assert.Null(content.MemoryPct);
    }

    [Fact]
    public void Plan_TempsRestant_SeulementPourUneRemiseAZeroFuture()
    {
        var now = new DateTime(2026, 10, 27, 15, 20, 0);
        AiUsage[] usages =
        [
            Usage("claude", session: new UsageWindow { UsedPct = 70, ResetsAt = now.AddMinutes(158) },
                week: new UsageWindow { UsedPct = 60, ResetsAt = now.AddMinutes(-1) }),
            Usage("codex", session: new UsageWindow { UsedPct = 50 }),
        ];

        var content = SideStripPlanner.Plan(usages, new UsageConfig(), new SystemLoad(23, 61), now);

        Assert.Equal(new TimeSpan?[] { TimeSpan.FromMinutes(158), null, null },
            content.Quotas.Select(quota => quota.ResetIn));
        Assert.All(SideStripPlanner.Plan(usages, new UsageConfig(), new SystemLoad(23, 61)).Quotas,
            quota => Assert.Null(quota.ResetIn));
    }

    [Theory]
    [InlineData("codex", "", new[] { "codex", "codex", "claude", "claude" })]
    [InlineData("CODEX", "claude", new[] { "codex", "codex", "claude", "claude" })]
    [InlineData("", "codex", new[] { "codex", "codex", "claude", "claude" })]
    [InlineData("", "", new[] { "claude", "claude", "codex", "codex" })]
    [InlineData("gemini", "", new[] { "claude", "claude", "codex", "codex" })]
    public void Plan_IaPrincipale_SesFenetresEnTete(string principal, string defaultProvider, string[] expected)
    {
        AiUsage[] usages =
        [
            Usage("claude", session: new UsageWindow { UsedPct = 90 }, week: new UsageWindow { UsedPct = 60 }),
            Usage("codex", session: new UsageWindow { UsedPct = 10 }, week: new UsageWindow { UsedPct = 30 }),
        ];

        var content = SideStripPlanner.Plan(usages, new UsageConfig { DefaultProviderId = defaultProvider },
            new SystemLoad(23, 61), principalProviderId: principal);

        Assert.Equal(expected, content.Quotas.Select(quota => quota.ProviderId));
        Assert.True(content.Quotas[0].UsedPct >= content.Quotas[1].UsedPct);
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
