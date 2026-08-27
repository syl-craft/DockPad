using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DockPad.Tests;

/// <summary>
/// Tout style de bouton doit décider de sa couleur de texte.
/// </summary>
/// <remarks>
/// <para>
/// <b>Le mode de panne que ce garde ramène dans la suite de tests.</b> Un <c>Button</c> est un
/// <c>Control</c> : son style de <i>thème</i> pose un <c>Foreground</c> noir, et un setter de style
/// bat une valeur héritée. Un style de bouton qui n'en pose pas laisse donc son contenu en noir —
/// invisible en thème sombre, et parfaitement normal en thème clair, donc invisible aussi à la
/// relecture.
/// </para>
/// <para>
/// C'est ainsi que le nom des tuiles est resté noir sur fond sombre : ni <c>TileButton</c>, ni
/// <c>DangerButton</c>, ni <c>ProviderTab</c> ne posaient de couleur. Mesuré à <c>#000000</c> sur un
/// fond à <c>#2B2B2B</c> — quatre allers-retours avant de le voir, parce qu'un texte noir sur fond
/// sombre se lit comme un texte gris à l'échelle d'une capture réduite.
/// </para>
/// <para>
/// Les <c>RepeatButton</c> sont exclus : ceux du projet sont les boutons de pagination d'une barre
/// de défilement, sans contenu textuel.
/// </para>
/// </remarks>
public class ForegroundGuardTests
{
    [Fact]
    public void ToutStyleDeBouton_DecideDeSaCouleurDeTexte()
    {
        var faults = new List<string>();

        foreach (var file in XamlFiles())
        {
            var xaml = File.ReadAllText(file);

            foreach (var match in Regex.Matches(
                         xaml,
                         """<Style ([^>]*?TargetType="(?:Button|ToggleButton)"[^>]*?)>(.*?)</Style>""",
                         RegexOptions.Singleline).Cast<Match>())
            {
                var head = match.Groups[1].Value;
                var body = match.Groups[2].Value;

                // Un style dérivé hérite de la couleur du style parent.
                if (head.Contains("BasedOn")) continue;

                // Seuls les setters de premier niveau comptent : ceux d'un ControlTemplate visent
                // un élément nommé, pas le bouton.
                var topLevel = body.Split("""<Setter Property="Template">""")[0];
                if (Regex.IsMatch(topLevel, """<Setter\s+Property="Foreground""")) continue;

                var key = Regex.Match(head, "x:Key=\"([^\"]+)\"");
                faults.Add($"{Path.GetFileName(file)} : {(key.Success ? key.Groups[1].Value : "(implicite)")}");
            }
        }

        Assert.True(faults.Count == 0,
            "Styles de bouton sans couleur de texte (leur contenu sera noir en thème sombre) :\n  "
            + string.Join("\n  ", faults));
    }

    private static IEnumerable<string> XamlFiles() =>
        Directory.EnumerateFiles(RepoRoot(), "*.xaml", SearchOption.AllDirectories)
                 .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                          && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string RepoRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, ".."));
}
