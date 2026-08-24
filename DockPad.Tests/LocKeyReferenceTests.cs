using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DockPad.Services.Localization;

namespace DockPad.Tests;

/// <summary>
/// Toute clé citée dans le code ou dans un XAML existe dans les ressources.
/// </summary>
/// <remarks>
/// C'est le pendant de la garde anti-régression des XAML. <c>Loc.T</c> rend <c>[Clé]</c> plutôt que
/// de lever — un choix délibéré, pour qu'une clé oubliée n'empêche pas une fenêtre de s'ouvrir — mais
/// du coup une faute de frappe ne se voit qu'à l'écran, et seulement sur l'écran concerné. Ce test la
/// transforme en échec de suite de tests, en citant le fichier et la clé.
/// </remarks>
public class LocKeyReferenceTests
{
    /// <summary>Appels <c>Loc.T("…")</c> et <c>Loc.F("…", …)</c> dans le code C#.</summary>
    private static readonly Regex CodeCall =
        new(@"Loc\.(?:T|F)\(""(?<key>[A-Za-z0-9_]+)""", RegexOptions.Compiled);

    /// <summary>Extension de balisage <c>{loc:T Clé}</c> dans les XAML.</summary>
    private static readonly Regex MarkupCall =
        new(@"\{loc:T\s+(?<key>[A-Za-z0-9_]+)\s*\}", RegexOptions.Compiled);

    /// <summary>Clés construites à l'exécution : leur préfixe est vérifié à la place.</summary>
    private static readonly Regex Interpolated =
        new(@"Loc\.T\(\$""(?<prefix>[A-Za-z0-9_]+)_\{", RegexOptions.Compiled);

    /// <summary>
    /// Clés volontairement absentes du magasin : <c>LocTests</c> en a besoin d'une pour vérifier
    /// qu'une clé manquante s'affiche <c>[Clé]</c> au lieu de lever.
    /// </summary>
    private static readonly HashSet<string> DeliberatelyAbsent = new(StringComparer.Ordinal)
    {
        "Nope_Missing",
    };

    [Fact]
    public void ToutesLesClesCiteesExistentDansLesRessources()
    {
        var known = Loc.AllEntries(CultureInfo.GetCultureInfo("en")).Select(e => e.Key).ToHashSet();
        var manquantes = new List<string>();

        foreach (var (file, text) in Sources())
        {
            var regex = file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ? MarkupCall : CodeCall;
            foreach (Match match in regex.Matches(text))
            {
                var key = match.Groups["key"].Value;
                if (known.Contains(key) || DeliberatelyAbsent.Contains(key)) continue;
                manquantes.Add($"{Path.GetFileName(file)} → {key}");
            }
        }

        Assert.Empty(manquantes);
    }

    [Fact]
    public void LesClesConstruitesOntAuMoinsUneRessourceAvecLeurPrefixe()
    {
        // HotkeyService fabrique « Key_Space » depuis un identifiant : on ne peut pas vérifier la clé
        // exacte, mais on peut vérifier que la famille existe. Sans ça, renommer le préfixe des
        // touches passerait inaperçu jusqu'à ce qu'un utilisateur ouvre les Options.
        var known = Loc.AllEntries(CultureInfo.GetCultureInfo("en")).Select(e => e.Key).ToList();
        var orphelins = new List<string>();

        foreach (var (file, text) in Sources())
        {
            foreach (Match match in Interpolated.Matches(text))
            {
                var prefix = match.Groups["prefix"].Value + "_";
                if (!known.Any(k => k.StartsWith(prefix, StringComparison.Ordinal)))
                    orphelins.Add($"{Path.GetFileName(file)} → {prefix}*");
            }
        }

        Assert.Empty(orphelins);
    }

    private static IEnumerable<(string File, string Text)> Sources()
    {
        foreach (var pattern in new[] { "*.cs", "*.xaml" })
        {
            foreach (var file in Directory.EnumerateFiles(RepoRoot(), pattern, SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    continue;
                yield return (file, File.ReadAllText(file));
            }
        }
    }

    private static string RepoRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, ".."));
}
