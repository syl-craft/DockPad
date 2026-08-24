using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DockPad.Tests;

/// <summary>
/// Garde anti-régression : aucun texte visible ne doit revenir en dur dans un XAML.
/// </summary>
/// <remarks>
/// <para>
/// Cinq cents chaînes ont été sorties vers les ressources ; rien n'empêche la prochaine fenêtre
/// d'écrire <c>Text="Enregistrer"</c>. Ce test échoue alors avec le fichier et le texte fautifs,
/// plutôt que de laisser une fenêtre à moitié traduite se découvrir à la capture.
/// </para>
/// <para>
/// La liste blanche ne contient que ce qui <b>ne se traduit pas</b> : glyphes, symboles, noms de
/// touches, valeurs de propriétés WPF qui portent le même nom qu'un attribut de texte
/// (<c>SizeToContent="Height"</c>).
/// </para>
/// </remarks>
public class XamlLiteralGuardTests
{
    /// <summary>Attributs qui portent du texte affiché.</summary>
    private static readonly Regex Literal =
        new(@"(?<attr>Text|Content|ToolTip|Header|Title)=""(?<value>[^""{][^""]*)""", RegexOptions.Compiled);

    /// <summary>
    /// Valeurs qui ne se traduisent pas par nature, et dont la liste doit rester courte : noms de
    /// touches et de modificateurs, valeurs de propriétés WPF homonymes d'un attribut de texte
    /// (<c>SizeToContent="Height"</c>), nom du produit, et lignes de commande — une commande
    /// s'écrit pareil dans toutes les langues, la traduire la casserait.
    /// </summary>
    private static readonly HashSet<string> NeverTranslated = new(StringComparer.Ordinal)
    {
        "Ctrl", "Alt", "Shift", "Win",
        "Height", "Width",
        "DockPad",
        "claude mcp remove dockpad",
    };

    [Fact]
    public void AucunTexteVisibleNEstEcritEnDurDansUnXaml()
    {
        var fautifs = new List<string>();

        foreach (var file in Directory.EnumerateFiles(RepoRoot(), "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            foreach (Match match in Literal.Matches(File.ReadAllText(file)))
            {
                var value = match.Groups["value"].Value;
                if (IsNotTranslatable(value)) continue;
                fautifs.Add($"{Path.GetFileName(file)} → {match.Groups["attr"].Value}=\"{value}\"");
            }
        }

        Assert.Empty(fautifs);
    }

    /// <summary>
    /// Vrai pour ce qui n'a pas de traduction : un glyphe, un symbole, un nombre, un nom de touche.
    /// Le critère est l'absence de lettre au-delà de l'ASCII de base, ou une valeur d'un seul mot
    /// technique — pas une liste de chaînes exactes, qui serait à maintenir à chaque libellé.
    /// </summary>
    private static bool IsNotTranslatable(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return true;
        if (NeverTranslated.Contains(trimmed)) return true;

        // Un glyphe ou une ponctuation seuls : « ─ », « ⬇ », « ▲ », « … », « % », « + ».
        // Deux lettres latines suffisent à faire un mot ; en dessous, il n'y a rien à traduire.
        var letters = trimmed.Count(char.IsLetter);
        return letters < 2;
    }

    /// <summary>
    /// Racine du dépôt. <see cref="CallerFilePathAttribute"/> plutôt qu'un chemin relatif à
    /// l'assembly : les tests s'exécutent depuis <c>bin\Debug\net8.0-windows</c>, dont la distance à
    /// la racine dépend de la configuration.
    /// </summary>
    private static string RepoRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, ".."));
}
