using System.Globalization;
using DockPad.Services.Usage;

namespace DockPad.Tests;

public class UsageFormatTests
{
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
    public void Tokens_FormateEnCompact(long valeur, string attendu)
    {
        Assert.Equal(attendu, UsageFormat.Tokens(valeur));
    }

    [Fact]
    public void Tokens_SousCultureAllemande_GardeLaVirguleDecimale()
    {
        // Le rendu ne doit pas dépendre de la culture de la machine : en de-DE le séparateur
        // décimal natif est aussi la virgule, mais en en-US ce serait un point. On fige fr-FR.
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
        var now = new DateTime(2026, 8, 20, 11, 30, 0);
        Assert.Equal("14h00", UsageFormat.Reset(new DateTime(2026, 8, 20, 14, 0, 0), now));
    }

    [Fact]
    public void Reset_AutreJour_DonneLeJourAbrege()
    {
        var now = new DateTime(2026, 8, 20, 11, 30, 0);   // jeudi
        Assert.Equal("lun. 00h", UsageFormat.Reset(new DateTime(2026, 8, 24, 0, 0, 0), now));
    }

    [Fact]
    public void Reset_Null_DonneChaineVide()
    {
        Assert.Equal("", UsageFormat.Reset(null, new DateTime(2026, 8, 20)));
    }

    [Fact]
    public void Reset_SousCultureAnglaise_GardeLeFormatFrancais()
    {
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
