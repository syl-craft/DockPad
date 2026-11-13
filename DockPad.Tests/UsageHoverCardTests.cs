using System.Globalization;
using DockPad.Models;
using DockPad.Services.Localization;
using DockPad.Services.Usage;

namespace DockPad.Tests;

public class UsageHoverCardTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 12, 0, 0);

    public UsageHoverCardTests() => Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));

    private static AiUsage Usage(UsageWindow? session = null, UsageWindow? week = null,
        string url = "https://example.com/usage", string notice = "") => new()
    {
        ProviderId = "codex", Name = "Codex", Glyph = "X", AccentColor = "#123456",
        Session = session, Week = week, UsageUrl = url, QuotaNotice = notice,
        QuotaNoticeNote = notice.Length > 0 ? "HTTP 429" : "",
    };

    [Fact]
    public void Carte_QuotaNormal_IdentiteConsommeRestantEtReset()
    {
        var window = new UsageWindow { UsedPct = 22, ResetsAt = Now.AddMinutes(38) };
        var card = UsageHoverCard.Create(Usage(window), window, 15, Now);

        Assert.Equal("codex", card.ProviderId);
        Assert.Equal("Codex · session 5 h", card.Header);
        Assert.Equal("22%", card.PercentText);
        Assert.Equal("78 % restants", card.RemainingText);
        Assert.Equal(0.22, card.Fraction);
        Assert.Equal("#123456", card.Color);
        Assert.Contains("12h38", card.ResetText);
        Assert.Equal("dans 38 min", card.ResetRelative);
        Assert.Equal("", card.Note);
        Assert.Null(card.OtherWindow);
        Assert.True(card.HasUsageUrl);
        Assert.Contains("22 %", card.AccessibleText);
    }

    [Fact]
    public void Carte_DeuxFenetres_LibellesDuFournisseurEtAutreFenetre()
    {
        var session = new UsageWindow { UsedPct = 22, Label = "Gemini" };
        var week = new UsageWindow { UsedPct = 15, Label = "Claude", ResetsAt = Now.AddDays(2) };
        var usage = Usage(session, week);
        var card = UsageHoverCard.Create(usage, session, 15, Now);
        Assert.Equal("Codex · Gemini", card.Header);
        Assert.Contains("Claude", card.OtherWindow ?? "");
        Assert.Contains("15 %", card.OtherWindow ?? "");
        Assert.Equal("", card.ResetText);
        Assert.Equal("", card.ResetRelative);
        Assert.Contains("Gemini", UsageHoverCard.Create(usage, week, 15, Now).OtherWindow ?? "");
    }

    [Fact]
    public void Carte_SeuilExact_Alerte()
    {
        var window = new UsageWindow { UsedPct = 85 };
        var card = UsageHoverCard.Create(Usage(window), window, 15, Now);
        Assert.True(card.IsAlert);
        Assert.Equal(UsageFormat.Critical, card.Color);
        Assert.Equal("Seuil d’alerte atteint", card.Note);
    }

    [Theory]
    [InlineData(100, "0 % restant")]
    [InlineData(99, "1 % restant")]
    [InlineData(98, "2 % restants")]
    public void Carte_Restant_PlurielFrancais(int usedPct, string expected)
    {
        var window = new UsageWindow { UsedPct = usedPct };
        Assert.Equal(expected, UsageHoverCard.Create(Usage(window), window, 15, Now).RemainingText);
    }

    [Fact]
    public void Carte_AncienAuSeuil_GrisPrioritaireEtDateExacte()
    {
        var window = new UsageWindow { UsedPct = 95, ObservedAt = new DateTime(2026, 11, 7, 22, 23, 0) };
        var card = UsageHoverCard.Create(Usage(window), window, 15, Now);
        Assert.True(card.IsStale);
        Assert.False(card.IsAlert);
        Assert.Equal(UsageBarViewModel.StaleColor, card.Color);
        Assert.Contains("7 nov. 22h23", card.Note);
        Assert.Contains("peut sous-estimer", card.Note);
    }

    [Fact]
    public void Carte_NoticeSansQuota_TitreDetailEtLien()
    {
        var card = UsageHoverCard.Create(Usage(notice: "Quota indisponible — réessai"), null, 15, Now);
        Assert.False(card.HasQuota);
        Assert.Equal("Quota indisponible", card.NoticeTitle);
        Assert.Contains("HTTP 429", card.NoticeDetail);
        Assert.Contains("réessai", card.NoticeDetail);
        Assert.True(card.HasUsageUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("file:///C:/test.exe")]
    [InlineData("mailto:test@example.com")]
    [InlineData("/usage")]
    public void Carte_AdresseNonWeb_LienAbsent(string url)
    {
        var card = UsageHoverCard.Create(Usage(url: url), null, 15, Now);
        Assert.False(card.HasUsageUrl);
        Assert.Equal("", card.UsageUrl);
    }

    [Theory]
    [InlineData(-1, "dans 0 min")]
    [InlineData(0, "dans 0 min")]
    [InlineData(38, "dans 38 min")]
    [InlineData(125, "dans 2 h 05")]
    [InlineData(7140, "dans 4 j 23 h")]
    public void Duree_MinutesHeuresJoursEtPasse(int minutes, string expected) =>
        Assert.Equal(expected, UsageHoverCard.RelativeReset(Now.AddMinutes(minutes), Now));

    [Theory]
    [InlineData("en", "in 2 h 05 min")]
    [InlineData("fr", "dans 2 h 05")]
    public void Duree_Localisee(string culture, string expected)
    {
        Loc.SetCulture(CultureInfo.GetCultureInfo(culture));
        Assert.Equal(expected, UsageHoverCard.RelativeReset(Now.AddMinutes(125), Now));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("qps-Ploc")]
    public void Carte_Traductions_CompletesEtGabaritsValides(string culture)
    {
        Loc.SetCulture(CultureInfo.GetCultureInfo(culture));
        var window = new UsageWindow { UsedPct = 90, ResetsAt = Now.AddDays(3), ObservedAt = Now.AddDays(-1) };
        var card = UsageHoverCard.Create(Usage(window, new UsageWindow { UsedPct = 10 }), window, 15, Now);
        Assert.DoesNotContain("[Usage", card.AccessibleText);
        Assert.DoesNotContain("[Usage", Loc.T("UsageHover_OpenPage"));
        Assert.DoesNotContain("[Usage", UsageHoverCard.RelativeReset(Now.AddMinutes(10), Now));
        Assert.DoesNotContain("[Usage", UsageHoverCard.RelativeReset(Now.AddHours(10), Now));
    }
}
