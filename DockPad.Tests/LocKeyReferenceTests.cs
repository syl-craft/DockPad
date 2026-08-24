using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DockPad.Services.Localization;

namespace DockPad.Tests;

/// <summary>
/// Cohérence entre les clés citées par le code et le contenu du magasin, dans les deux sens.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sens 1 — toute clé citée existe.</b> <c>Loc.T</c> rend <c>[Clé]</c> plutôt que de lever, choix
/// délibéré pour qu'une clé oubliée n'empêche pas une fenêtre de s'ouvrir. Du coup une faute de
/// frappe ne se voit qu'à l'écran, et seulement sur l'écran concerné.
/// </para>
/// <para>
/// <b>Sens 2 — toute clé du magasin sert.</b> La parité vérifie que les deux langues portent les
/// mêmes clés, pas qu'elles servent : deux doublons morts s'étaient glissés dans le magasin, avec la
/// même valeur qu'une clé existante. À la modification suivante, « laquelle fait foi ? » n'aurait pas
/// eu de réponse.
/// </para>
/// <para>
/// Le scanner ne se contente pas de <c>Loc.T("Clé")</c> : les clés voyagent aussi dans un ternaire —
/// <c>Loc.T(cond ? "A" : "B")</c> — et un détecteur qui ne verrait que la forme directe déclarerait
/// ces clés orphelines.
/// </para>
/// </remarks>
public class LocKeyReferenceTests
{
    /// <summary>Début d'un appel de localisation, en C# comme en XAML.</summary>
    private static readonly Regex CallStart =
        new(@"Loc\.(?:T|F)\(|\{loc:T\s+", RegexOptions.Compiled);

    /// <summary>Forme d'une clé : <c>Zone_Element</c>.</summary>
    private static readonly Regex KeyShape =
        new(@"[A-Z][A-Za-z0-9]*_[A-Za-z0-9_]+", RegexOptions.Compiled);

    /// <summary>
    /// Clés volontairement absentes du magasin : <c>LocTests</c> en a besoin d'une pour vérifier
    /// qu'une clé manquante s'affiche <c>[Clé]</c> au lieu de lever.
    /// </summary>
    private static readonly HashSet<string> DeliberatelyAbsent =
        new(StringComparer.Ordinal) { "Nope_Missing" };

    /// <summary>
    /// Familles composées à l'exécution (<c>Loc.T($"Key_{name}")</c>), donc introuvables par
    /// recherche textuelle. Un test de préfixe les couvre.
    /// </summary>
    private static readonly string[] DynamicFamilies = ["Key_"];

    [Fact]
    public void ToutesLesClesCiteesExistentDansLesRessources()
    {
        var known = Keys();
        var manquantes = Cited()
            .Where(c => !known.Contains(c.Key) && !DeliberatelyAbsent.Contains(c.Key))
            .Select(c => $"{c.File} → {c.Key}")
            .Distinct()
            .ToList();

        Assert.Empty(manquantes);
    }

    [Fact]
    public void AucuneCleDuMagasinNEstOrpheline()
    {
        var cited = Cited().Select(c => c.Key).ToHashSet(StringComparer.Ordinal);

        var orphelines = Keys()
            .Where(k => !cited.Contains(k))
            .Where(k => !DynamicFamilies.Any(f => k.StartsWith(f, StringComparison.Ordinal)))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(orphelines);
    }

    [Fact]
    public void LesFamillesComposeesALExecutionExistentDansLeMagasin()
    {
        // Renommer le préfixe des touches ne casserait aucun appel : « Key_Space » est fabriqué par
        // interpolation. Ce test attrape le renommage.
        var known = Keys();

        foreach (var family in DynamicFamilies)
            Assert.Contains(known, k => k.StartsWith(family, StringComparison.Ordinal));
    }

    private static HashSet<string> Keys() =>
        Loc.AllEntries(CultureInfo.GetCultureInfo("en")).Select(e => e.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Clés citées dans les sources : pour chaque appel de localisation, les clés trouvées dans ses
    /// arguments. La fenêtre s'arrête à la fin de l'appel — parenthèse fermante en C#, accolade en
    /// XAML — pour ne pas ramasser la ligne suivante.
    /// </summary>
    private static IEnumerable<(string File, string Key)> Cited()
    {
        foreach (var (file, text) in Sources())
        {
            foreach (Match call in CallStart.Matches(text))
            {
                var start = call.Index + call.Length;
                var end = text.IndexOfAny([')', '}'], start);
                if (end < 0) end = Math.Min(text.Length, start + 200);

                foreach (Match key in KeyShape.Matches(text[start..end]))
                    yield return (Path.GetFileName(file), key.Value);
            }
        }
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
