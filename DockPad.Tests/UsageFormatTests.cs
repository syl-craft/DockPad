using System.Globalization;
using DockPad.Services.Localization;
using DockPad.Services.Usage;

namespace DockPad.Tests;

/// <summary>
/// Formatage des jetons et des heures de reset.
/// </summary>
/// <remarks>
/// Chaque test pose la langue explicitement. Le rendu dépend d'elle par conception depuis
/// l'internationalisation — « 12,4k » en français, « 12.4k » en anglais — et un test qui hérite de
/// la langue laissée par un autre passerait ou casserait selon l'ordre d'exécution.
/// </remarks>
public class UsageFormatTests
{
    private static void Francais() => Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));
    private static void Anglais() => Loc.SetCulture(CultureInfo.GetCultureInfo("en"));

    private const string Rouge = "#E5484D";
    private const string Ambre = "#F5A623";
    private const string Vert  = "#34A853";

    // --- GaugeColor : le seuil porte sur le RESTANT, la bascule ambre sur le CONSOMMÉ.

    [Fact]
    public void GaugeColor_RestantEgalAuSeuil_EstRouge()
    {
        // 85 % consommé → 15 % restant, seuil 15 : la frontière est incluse, sinon l'alerte
        // ne se déclenche jamais pile au seuil configuré.
        Assert.Equal(Rouge, UsageFormat.GaugeColor(usedPct: 85, thresholdPct: 15));
    }

    [Fact]
    public void GaugeColor_RestantJusteAuDessusDuSeuil_NEstPasRouge()
    {
        Assert.Equal(Ambre, UsageFormat.GaugeColor(usedPct: 84, thresholdPct: 15));
    }

    [Fact]
    public void GaugeColor_ConsommeExactement60_EstAmbre()
    {
        Assert.Equal(Ambre, UsageFormat.GaugeColor(usedPct: 60, thresholdPct: 15));
    }

    [Fact]
    public void GaugeColor_ConsommeJusteSous60_EstVert()
    {
        Assert.Equal(Vert, UsageFormat.GaugeColor(usedPct: 59, thresholdPct: 15));
    }

    [Fact]
    public void GaugeColor_SeuilEleve_DomineLaBasculeAmbre()
    {
        // Seuil 50 % : dès 50 % consommé le restant atteint le seuil → rouge, même si 50 < 60.
        Assert.Equal(Rouge, UsageFormat.GaugeColor(usedPct: 50, thresholdPct: 50));
        Assert.Equal(Rouge, UsageFormat.GaugeColor(usedPct: 95, thresholdPct: 50));
    }

    [Fact]
    public void GaugeColor_RienDeConsomme_EstVert()
    {
        Assert.Equal(Vert, UsageFormat.GaugeColor(usedPct: 0, thresholdPct: 15));
    }

    // --- Tokens

    [Theory]
    [InlineData(0L, "0")]
    [InlineData(987L, "987")]
    [InlineData(999L, "999")]
    [InlineData(1_000L, "1k")]
    [InlineData(1_050L, "1,1k")]
    [InlineData(12_400L, "12,4k")]
    [InlineData(999_000L, "999k")]
    [InlineData(1_200_000L, "1,2M")]
    [InlineData(190_612_940L, "190,6M")]
    [InlineData(999_000_000L, "999M")]
    [InlineData(1_000_000_000L, "1 Md")]
    [InlineData(2_741_932_310L, "2,7 Md")]
    // Frontières d'arrondi : 999 999 / 1000 vaut 999,999, qui s'arrondit à 1000. Sans promotion
    // d'unité, ces trois valeurs affichaient « 1000k », « 1000M » et « 1000 Md ».
    [InlineData(999_949L, "999,9k")]
    [InlineData(999_999L, "1M")]
    [InlineData(999_999_999L, "1 Md")]
    public void Tokens_FormateEnCompact(long valeur, string attendu)
    {
        Francais();

        Assert.Equal(attendu, UsageFormat.Tokens(valeur));
    }

    [Theory]
    [InlineData(999L, "999")]
    [InlineData(1_050L, "1.1k")]
    [InlineData(12_400L, "12.4k")]
    [InlineData(1_200_000L, "1.2M")]
    [InlineData(2_741_932_310L, "2.7B")]
    public void Tokens_EnAnglais_PointDecimalEtSuffixeB(long valeur, string attendu)
    {
        // Le séparateur décimal vient de la culture, mais le suffixe vient des ressources : « Md »
        // est français, l'anglais dit « B », et l'espace qui précède l'un et pas l'autre est un
        // choix typographique de la langue, qu'aucune CultureInfo ne connaît.
        Anglais();

        Assert.Equal(attendu, UsageFormat.Tokens(valeur));
    }

    [Fact]
    public void Tokens_SuitLaLangueChoisieEtNonLaCultureDuThread()
    {
        // Le rendu suit la langue de l'application, pas une culture de thread posée par ailleurs :
        // c'est Loc qui décide. Sans cette garantie, un Task.Run parti avant une bascule
        // afficherait des nombres dans l'ancienne langue.
        var precedente = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Assert.Equal("12,4k", UsageFormat.Tokens(12_400));
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("12,4k", UsageFormat.Tokens(12_400));
        }
        finally { CultureInfo.CurrentCulture = precedente; }
    }

    // --- Reset

    [Fact]
    public void Reset_MemeJour_DonneLHeure()
    {
        Francais();
        var now = new DateTime(2026, 8, 20, 11, 30, 0);
        Assert.Equal("14h00", UsageFormat.Reset(new DateTime(2026, 8, 20, 14, 0, 0), now));
    }

    [Fact]
    public void Reset_EnAnglais_SeparateurDeuxPoints()
    {
        // Le « h » de « 14h00 » est une convention française écrite en dur dans le gabarit : aucune
        // CultureInfo ne la corrige, d'où un gabarit par langue dans les ressources.
        Anglais();
        var now = new DateTime(2026, 8, 20, 11, 30, 0);

        Assert.Equal("14:00", UsageFormat.Reset(new DateTime(2026, 8, 20, 14, 0, 0), now));
    }

    [Fact]
    public void Reset_AutreJour_DonneLeJourAbrege()
    {
        Francais();
        var now = new DateTime(2026, 8, 20, 11, 30, 0);   // jeudi
        Assert.Equal("lun. 00h", UsageFormat.Reset(new DateTime(2026, 8, 24, 0, 0, 0), now));
    }

    [Fact]
    public void Reset_Null_DonneChaineVide()
    {
        Assert.Equal("", UsageFormat.Reset(null, new DateTime(2026, 8, 20)));
    }

    [Fact]
    public void Reset_SuitLaLangueChoisieEtNonLaCultureDuThread()
    {
        Francais();
        var precedente = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            var now = new DateTime(2026, 8, 20, 11, 30, 0);

            Assert.Equal("14h00", UsageFormat.Reset(new DateTime(2026, 8, 20, 14, 0, 0), now));
            Assert.Equal("lun. 00h", UsageFormat.Reset(new DateTime(2026, 8, 24, 0, 0, 0), now));
        }
        finally { CultureInfo.CurrentCulture = precedente; }
    }
}
