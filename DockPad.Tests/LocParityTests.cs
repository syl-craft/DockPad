using System.Globalization;
using DockPad.Services.Localization;

namespace DockPad.Tests;

/// <summary>
/// Parité des clés entre les deux langues.
/// </summary>
/// <remarks>
/// C'est le filet qui remplace la sécurité perdue en mettant du gabarit dans les valeurs plutôt que
/// dans du code généré : sur ~500 clés, une traduction oubliée ne se voit pas à la relecture. Une
/// clé présente d'un seul côté s'afficherait en anglais au milieu d'une interface française, ou en
/// <c>[Clé]</c> si c'est le neutre qui manque.
/// </remarks>
public class LocParityTests
{
    private static HashSet<string> Keys(string langue) =>
        Loc.AllEntries(CultureInfo.GetCultureInfo(langue)).Select(e => e.Key).ToHashSet();

    [Fact]
    public void LesDeuxLanguesPortentExactementLesMemesCles()
    {
        var en = Keys("en");
        var fr = Keys("fr");

        Assert.Empty(en.Except(fr));   // traduite en anglais, oubliée en français
        Assert.Empty(fr.Except(en));   // l'inverse : une clé orpheline, invisible au repli
    }

    [Fact]
    public void AucuneValeurNEstVide()
    {
        // Une valeur vide passe la parité et donne un libellé invisible à l'écran.
        var vides = new List<string>();

        foreach (var langue in new[] { "en", "fr" })
            foreach (var (key, value) in Loc.AllEntries(CultureInfo.GetCultureInfo(langue)))
                if (string.IsNullOrWhiteSpace(value))
                    vides.Add($"{langue}/{key}");

        Assert.Empty(vides);
    }

    [Fact]
    public void LesGabaritsOntLesMemesPlaceholdersDansLesDeuxLangues()
    {
        // Un {0} perdu à la traduction donne une phrase amputée de son nombre ou de son nom de
        // fichier. Le test compare les index de placeholders, pas leur ordre : une langue peut
        // légitimement les intervertir.
        var en = Loc.AllEntries(CultureInfo.GetCultureInfo("en")).ToDictionary(e => e.Key, e => e.Value);
        var fr = Loc.AllEntries(CultureInfo.GetCultureInfo("fr")).ToDictionary(e => e.Key, e => e.Value);
        var divergentes = new List<string>();

        foreach (var (key, valueEn) in en)
        {
            if (!fr.TryGetValue(key, out var valueFr)) continue;
            if (!Placeholders(valueEn).SetEquals(Placeholders(valueFr))) divergentes.Add(key);
        }

        Assert.Empty(divergentes);
    }

    private static HashSet<string> Placeholders(string template) =>
        System.Text.RegularExpressions.Regex.Matches(template, @"\{(\d+)")
              .Select(m => m.Groups[1].Value).ToHashSet();
}
