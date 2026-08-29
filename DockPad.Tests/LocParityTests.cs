using System.Globalization;
using DockPad.Services.Localization;

namespace DockPad.Tests;

/// <summary>
/// Parité des clés entre toutes les langues.
/// </summary>
/// <remarks>
/// C'est le filet qui remplace la sécurité perdue en mettant du gabarit dans les valeurs plutôt que
/// dans du code généré : sur ~500 clés, une traduction oubliée ne se voit pas à la relecture. Une
/// clé présente d'un seul côté s'afficherait en anglais au milieu d'une interface française, ou en
/// <c>[Clé]</c> si c'est le neutre qui manque.
/// </remarks>
public class LocParityTests
{
    /// <summary>
    /// Les langues du magasin. <c>en</c> est la référence : c'est la langue neutre, celle vers
    /// laquelle le <c>ResourceManager</c> se replie.
    /// </summary>
    /// <remarks>
    /// Une liste plutôt qu'une paire : la pseudo-langue <c>qps-Ploc</c> est <b>générée</b> depuis le
    /// français, mais rien ne la dispense des mêmes exigences — et ces gardes sont ce qui tient lieu
    /// de test à son générateur. Une accolade abîmée par la substitution de glyphes échoue ici.
    /// </remarks>
    private static readonly string[] Langues = ["en", "fr", "qps-Ploc"];

    private const string Reference = "en";

    private static HashSet<string> Keys(string langue) =>
        Loc.AllEntries(CultureInfo.GetCultureInfo(langue)).Select(e => e.Key).ToHashSet();

    [Fact]
    public void ToutesLesLanguesPortentExactementLesMemesCles()
    {
        var reference = Keys(Reference);
        var ecarts = new List<string>();

        foreach (var langue in Langues.Where(l => l != Reference))
        {
            var keys = Keys(langue);
            ecarts.AddRange(reference.Except(keys).Select(k => $"{langue} : {k} manquante"));
            ecarts.AddRange(keys.Except(reference).Select(k => $"{langue} : {k} orpheline"));
        }

        Assert.Empty(ecarts);
    }

    [Fact]
    public void AucuneValeurNEstVide()
    {
        // Une valeur vide passe la parité et donne un libellé invisible à l'écran.
        var vides = new List<string>();

        foreach (var langue in Langues)
            foreach (var (key, value) in Loc.AllEntries(CultureInfo.GetCultureInfo(langue)))
                if (string.IsNullOrWhiteSpace(value))
                    vides.Add($"{langue}/{key}");

        Assert.Empty(vides);
    }

    [Fact]
    public void LesGabaritsOntLesMemesPlaceholdersDansToutesLesLangues()
    {
        // Un {0} perdu à la traduction donne une phrase amputée de son nombre ou de son nom de
        // fichier. Le test compare les index de placeholders, pas leur ordre : une langue peut
        // légitimement les intervertir.
        var reference = Loc.AllEntries(CultureInfo.GetCultureInfo(Reference))
                           .ToDictionary(e => e.Key, e => e.Value);
        var divergentes = new List<string>();

        foreach (var langue in Langues.Where(l => l != Reference))
        {
            var entries = Loc.AllEntries(CultureInfo.GetCultureInfo(langue))
                             .ToDictionary(e => e.Key, e => e.Value);

            foreach (var (key, expected) in reference)
                if (entries.TryGetValue(key, out var value)
                    && !Placeholders(expected).SetEquals(Placeholders(value)))
                    divergentes.Add($"{langue}/{key}");
        }

        Assert.Empty(divergentes);
    }

    private static HashSet<string> Placeholders(string template) =>
        System.Text.RegularExpressions.Regex.Matches(template, @"\{(\d+)")
              .Select(m => m.Groups[1].Value).ToHashSet();
}
