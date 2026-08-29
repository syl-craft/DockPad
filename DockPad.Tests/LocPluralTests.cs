using System.Globalization;
using DockPad.Services.Localization;

namespace DockPad.Tests;

/// <summary>
/// Pluriel et intégrité des gabarits.
/// </summary>
/// <remarks>
/// Le français et l'anglais n'ont que deux formes mais ne basculent pas au même endroit : anglais
/// <c>one ⟺ n = 1</c>, français <c>one ⟺ 0 ≤ n &lt; 2</c>. Les deux raccourcis qu'on écrit
/// d'instinct sont donc faux — <c>n &gt; 1</c> donne « 0 rule », <c>n == 1</c> donne « 0 règles ».
/// La règle appartient à la langue, jamais au site d'appel : c'est ce que ces tests verrouillent.
/// </remarks>
public class LocPluralTests
{
    [Theory]
    [InlineData(0, "0 rules")]
    [InlineData(1, "1 rule")]
    [InlineData(2, "2 rules")]
    [InlineData(12, "12 rules")]
    public void Pluriel_Anglais(int n, string attendu)
    {
        Loc.SetCulture(CultureInfo.GetCultureInfo("en"));

        Assert.Equal(attendu, Loc.F("Browsers_RuleCount", n));
    }

    [Theory]
    [InlineData(0, "0 règle")]
    [InlineData(1, "1 règle")]
    [InlineData(2, "2 règles")]
    [InlineData(12, "12 règles")]
    public void Pluriel_Francais(int n, string attendu)
    {
        Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));

        Assert.Equal(attendu, Loc.F("Browsers_RuleCount", n));
    }

    [Fact]
    public void ToutesLesValeursDeRessourcesSontDesGabaritsValides()
    {
        // Mettre de la syntaxe SmartFormat dans les valeurs ajoute un mode de panne à l'exécution.
        // Ce test le ramène à la suite de tests : une accolade non fermée échoue ici, pas à l'écran.
        var fautives = new List<string>();

        // qps-Ploc incluse : ses valeurs sont engendrees par substitution de glyphes, et c'est
        // exactement le genre de traitement qui peut abimer une accolade sans qu'on le voie.
        foreach (var langue in new[] { "en", "fr", "qps-Ploc" })
        {
            foreach (var (key, value) in Loc.AllEntries(CultureInfo.GetCultureInfo(langue)))
            {
                if (!value.Contains('{')) continue;
                try
                {
                    Loc.Formatter.Parser.ParseFormat(value);
                }
                catch (Exception ex)
                {
                    fautives.Add($"{langue}/{key} : {ex.GetType().Name}");
                }
            }
        }

        Assert.Empty(fautives);
    }
}
