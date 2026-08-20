using System.Globalization;
using DockPad.Services.Usage;

namespace DockPad.Tests;

public class ClaudePricingTests
{
    private const long M = 1_000_000;

    [Fact]
    public void Cost_Opus_FactureEntreeEtSortieAuxTarifsPublies()
    {
        // Opus 5 : 5 $ / Mtok en entrée, 25 $ / Mtok en sortie.
        var cost = ClaudePricing.Cost("claude-opus-5", input: M, output: M, cacheWrite: 0, cacheRead: 0);
        Assert.Equal(30m, cost);
    }

    [Fact]
    public void Cost_Sonnet_MoinsCherQueOpus()
    {
        var opus   = ClaudePricing.Cost("claude-opus-5",   M, M, 0, 0);
        var sonnet = ClaudePricing.Cost("claude-sonnet-5", M, M, 0, 0);
        Assert.True(sonnet < opus);
    }

    [Fact]
    public void Cost_EcritureDeCache_FactureeAUnQuartDePlusQueLEntree()
    {
        var input = ClaudePricing.Cost("claude-opus-5", input: M, output: 0, cacheWrite: 0, cacheRead: 0);
        var write = ClaudePricing.Cost("claude-opus-5", input: 0, output: 0, cacheWrite: M, cacheRead: 0);
        Assert.Equal(input * 1.25m, write);
    }

    [Fact]
    public void Cost_LectureDeCache_FactureeAUnDixiemeDeLEntree()
    {
        var input = ClaudePricing.Cost("claude-opus-5", input: M, output: 0, cacheWrite: 0, cacheRead: 0);
        var read  = ClaudePricing.Cost("claude-opus-5", input: 0, output: 0, cacheWrite: 0, cacheRead: M);
        Assert.Equal(input * 0.1m, read);
    }

    [Fact]
    public void Cost_ModeleAvecSuffixeDate_ReconnuParPrefixe()
    {
        // Les transcripts portent des identifiants datés : claude-sonnet-4-5-20250929.
        var date = ClaudePricing.Cost("claude-sonnet-4-6-20251114", M, 0, 0, 0);
        var nu   = ClaudePricing.Cost("claude-sonnet-4-6",          M, 0, 0, 0);
        Assert.Equal(nu, date);
    }

    [Fact]
    public void Cost_ModeleInconnu_RetombeSurLeTarifSonnet()
    {
        var inconnu = ClaudePricing.Cost("un-modele-jamais-vu", M, M, 0, 0);
        var sonnet  = ClaudePricing.Cost("claude-sonnet-4-6",   M, M, 0, 0);
        Assert.Equal(sonnet, inconnu);
        Assert.True(inconnu > 0m);   // surtout pas zéro : un coût nul se lit comme « gratuit »
    }

    [Fact]
    public void Cost_ModeleVide_RetombeSurLeTarifSonnet()
    {
        Assert.Equal(ClaudePricing.Cost("claude-sonnet-4-6", M, 0, 0, 0),
                     ClaudePricing.Cost("", M, 0, 0, 0));
    }

    [Fact]
    public void Cost_AucunJeton_EstNul()
    {
        Assert.Equal(0m, ClaudePricing.Cost("claude-opus-5", 0, 0, 0, 0));
    }

    [Fact]
    public void Format_DonneDesDollarsADeuxDecimales()
    {
        Assert.Equal("$3.80", ClaudePricing.Format(3.8m));
        Assert.Equal("$0.00", ClaudePricing.Format(0m));
        Assert.Equal("$12.34", ClaudePricing.Format(12.344m));
    }

    [Fact]
    public void Format_SousCultureFrancaise_GardeLePointDecimal()
    {
        // La devise est celle de la source (USD) : « 3,80 $ » suggérerait une conversion.
        var precedente = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            Assert.Equal("$3.80", ClaudePricing.Format(3.8m));
        }
        finally { CultureInfo.CurrentCulture = precedente; }
    }
}
