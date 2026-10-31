using DockPad.Models;
using DockPad.Services.Usage;

namespace DockPad.Tests;

/// <summary>
/// Éligibilité d'un fournisseur à la barre et choix du plus proche de sa limite : quota ou notice
/// sans démonstration, fenêtres anciennes écartées, égalités départagées par l'ordre du bandeau.
/// </summary>
public class UsageEligibilityTests
{
    private static AiUsage Usage(string id, int? session = null, int? week = null, string notice = "",
                                 bool demo = false, bool staleSession = false) => new()
    {
        ProviderId = id,
        Name = id,
        Glyph = "X",
        AccentColor = "#123456",
        QuotaNotice = notice,
        Session = session is null
            ? null
            : new UsageWindow { UsedPct = session.Value, ObservedAt = staleSession ? new DateTime(2026, 8, 20, 9, 0, 0) : null },
        Week = week is null ? null : new UsageWindow { UsedPct = week.Value },
        IsDemo = demo,
    };

    [Fact]
    public void Eligible_QuotaOuNotice_PasDemoNiGeminiCopilot()
    {
        Assert.True(UsageEligibility.IsEligible(Usage("claude", session: 62, week: 44)));
        Assert.True(UsageEligibility.IsEligible(Usage("semaine", week: 10)));
        Assert.True(UsageEligibility.IsEligible(Usage("codex", notice: "Quota indisponible")));

        // Gemini et Copilot n'exposent aucun quota.
        Assert.False(UsageEligibility.IsEligible(Usage("gemini")));
        Assert.False(UsageEligibility.IsEligible(Usage("copilot")));
        Assert.False(UsageEligibility.IsEligible(Usage("demo", session: 62, week: 44, demo: true)));
    }

    [Fact]
    public void ClosestToLimit_FenetreLaPlusConsommee()
    {
        var usages = new[] { Usage("a", session: 30, week: 40), Usage("b", session: 20, week: 85), Usage("c", week: 70) };

        Assert.Equal("b", UsageEligibility.ClosestToLimit(usages));
    }

    [Fact]
    public void ClosestToLimit_Egalite_PremierDansLOrdre()
    {
        var usages = new[] { Usage("b", session: 60), Usage("a", week: 60) };

        Assert.Equal("b", UsageEligibility.ClosestToLimit(usages));
    }

    [Fact]
    public void ClosestToLimit_FenetreAncienneEtDemoIgnorees()
    {
        var usages = new[]
        {
            Usage("a", session: 95, week: 10, staleSession: true),
            Usage("demo", session: 99, demo: true),
            Usage("b", session: 50),
        };

        Assert.Equal("b", UsageEligibility.ClosestToLimit(usages));
    }

    [Fact]
    public void ClosestToLimit_AucuneFenetre_Null()
    {
        var usages = new[] { Usage("codex", notice: "Quota indisponible"), Usage("gemini"), Usage("a", session: 90, staleSession: true) };

        Assert.Null(UsageEligibility.ClosestToLimit(usages));
    }
}
