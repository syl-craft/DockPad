using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DockPad.Tests;

/// <summary>
/// Garde anti-régression côté C# : plus de texte français d'interface écrit en dur.
/// </summary>
/// <remarks>
/// <para>
/// <c>XamlLiteralGuardTests</c> ne couvre que les XAML, et le balayage manuel qui a servi à la
/// migration cherchait des <b>accents</b> — il est donc passé à côté de « Tous les navigateurs »,
/// « La page est pleine », « Nouveau navigateur » et « Chemin du dossier * », qui n'en ont aucun.
/// Quatre libellés restés français dans une interface anglaise, dont un trouvé par l'utilisateur.
/// </para>
/// <para>
/// Le critère est la présence d'un <b>mot-outil français</b> : une chaîne d'interface anglaise n'en
/// contient pas, une chaîne technique non plus. C'est un signal plus fiable que l'accent, qui
/// manquait un mot français sur deux.
/// </para>
/// </remarks>
public class FrenchLiteralGuardTests
{
    /// <summary>Mots-outils et noms courants du domaine : leur présence trahit une phrase française.</summary>
    private static readonly string[] FrenchWords =
    [
        "le", "la", "les", "un", "une", "des", "du", "de", "dans", "pour", "avec", "sans",
        "aucun", "aucune", "tous", "toutes", "cette", "votre", "vous", "est", "sont", "par",
        "sur", "aux", "ou", "ne", "pas", "plus", "son", "ses", "leur",
        "dossier", "fichier", "navigateur", "raccourci", "tuile", "page", "chemin", "nom",
    ];

    private static readonly Regex FrenchWord =
        new("(?<![A-Za-zÀ-ÿ])(" + string.Join("|", FrenchWords) + ")(?![A-Za-zÀ-ÿ])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Literal =
        new("\"((?:[^\"\\\\]|\\\\.){4,}?)\"", RegexOptions.Compiled);

    /// <summary>Forme d'une clé de ressource : ce n'est pas du texte affiché.</summary>
    private static readonly Regex KeyShape =
        new(@"^[A-Z][A-Za-z0-9]*_[A-Za-z0-9_]+$", RegexOptions.Compiled);

    /// <summary>Dossiers de l'application qui portent du texte affiché.</summary>
    /// <remarks>
    /// <c>Secrets</c> en fait partie comme les autres : un dossier neuf qui échapperait au balayage
    /// serait le premier à réintroduire ce que ce test existe pour empêcher.
    /// </remarks>
    private static readonly string[] Scanned = ["Views", "Dialogs", "Models", "Services", "Secrets"];

    /// <summary>
    /// Fichiers dont le français est <b>voulu</b> : les messages renvoyés au serveur MCP. Leur
    /// lecteur est un modèle, pas un humain — décision documentée dans <c>CLAUDE.md</c>.
    /// </summary>
    private static readonly string[] McpFacing =
    [
        "ShortcutActionService.cs", "PageActionService.cs", "BrowserActionService.cs",
        "McpDispatcher.cs", "McpLogService.cs", "McpPipeService.cs",
    ];

    [Fact]
    public void AucunTexteFrancaisDInterfaceNEstEcritEnDurEnCSharp()
    {
        var fautifs = new List<string>();

        foreach (var (file, line, number) in Lines())
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;

            // Les messages de journal restent en français : ils sont pour le développeur, et un log
            // qui change de langue selon le poste n'est plus grep-able.
            if (line.Contains("LogService.", StringComparison.Ordinal)) continue;

            // Les messages d'exception aussi : ils partent au journal, et l'utilisateur ne les voit
            // que derrière une phrase déjà traduite qui porte le sens (« Impossible d'écrire dans le
            // registre : … »). Les traduire rendrait le journal dépendant de la langue du poste
            // pour ne rien ajouter à l'écran.
            if (line.Contains("throw new", StringComparison.Ordinal)) continue;

            foreach (Match match in Literal.Matches(line))
            {
                var value = match.Groups[1].Value;
                if (KeyShape.IsMatch(value)) continue;
                if (!FrenchWord.IsMatch(value)) continue;
                fautifs.Add($"{Path.GetFileName(file)}:{number} → {value}");
            }
        }

        Assert.Empty(fautifs);
    }

    private static IEnumerable<(string File, string Line, int Number)> Lines()
    {
        var root = RepoRoot();

        foreach (var folder in Scanned)
        {
            var path = Path.Combine(root, folder);
            if (!Directory.Exists(path)) continue;

            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                if (McpFacing.Contains(Path.GetFileName(file))) continue;

                var number = 0;
                foreach (var line in File.ReadLines(file))
                    yield return (file, line, ++number);
            }
        }
    }

    private static string RepoRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, ".."));
}
