using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DockPad.Tests.Secrets;

/// <summary>
/// Les trois gardes du dossier <c>Secrets/</c>.
/// </summary>
/// <remarks>
/// <para>
/// L'invariant : <b>tout ce qui voit un secret vit dans <c>Secrets/</c>, et rien hors du dossier
/// n'en voit</b>. Le dossier n'est pas du rangement, c'est un périmètre d'audit — le relire en
/// entier, c'est avoir vu tout le code qui manipule un secret.
/// </para>
/// <para>
/// DockPad étant un assembly unique, <c>internal</c> n'achète rien : la frontière ne peut pas être
/// posée par un modificateur d'accès. Ce sont donc ces tests qui la tiennent, dans l'idiome des
/// gardes existants (<c>XamlLiteralGuardTests</c>, <c>FrenchLiteralGuardTests</c>).
/// </para>
/// </remarks>
public class SecretBoundaryGuardTests
{
    /// <summary>
    /// La surface d'entrée : les seuls fichiers hors du dossier qui ont le droit de le nommer.
    /// </summary>
    /// <remarks>
    /// Cette liste est le contrat. L'allonger doit être un geste délibéré, visible en revue — c'est
    /// tout l'objet du test : la surface ne grandit pas en douce.
    /// </remarks>
    private static readonly string[] EntryPoints =
        ["App.xaml.cs", "SettingsDialog.xaml.cs", "QuickAccessWindow.xaml.cs"];

    /// <summary>Écritures de fichier, sous toutes leurs formes.</summary>
    private static readonly Regex DiskWrite = new(
        @"\bFile\.(Write|AppendAll|AppendText|Create|Copy|Move|Replace)|new\s+StreamWriter|new\s+FileStream",
        RegexOptions.Compiled);

    /// <summary>
    /// Les deux façons dont la CLI Bitwarden accepte un secret <b>en clair sur la ligne de
    /// commande</b>. <c>--passwordenv</c> n'en fait pas partie : il reçoit un <i>nom</i> de variable.
    /// </summary>
    private static readonly Regex SecretOnCommandLine =
        new(@"--session|--password(?!env)", RegexOptions.Compiled);

    /// <summary>
    /// Les seuls littéraux portant « password » ou « session » qui peuvent être un argument : ce
    /// sont des <b>noms</b>, pas des valeurs.
    /// </summary>
    private static readonly string[] AllowedArguments = ["--passwordenv", "BW_PASSWORD", "BW_SESSION"];

    private static readonly Regex SecretishIdentifier =
        new(@"password|session", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Une ligne qui construit des arguments de processus : un ajout à <c>ArgumentList</c>, ou une
    /// collection portant un drapeau de ligne de commande.
    /// </summary>
    /// <remarks>
    /// Le second motif exige le <c>"--</c> et non la seule accolade : sans lui, un initialiseur de
    /// dictionnaire — <c>["BW_PASSWORD"] = motDePasse</c>, qui est <b>précisément</b> la bonne façon
    /// de faire — serait signalé comme une violation, et la garde pousserait à écrire le code
    /// dangereux pour se taire.
    /// </remarks>
    private static readonly Regex ArgumentShape =
        new(@"ArgumentList|\[[^\]]*""--", RegexOptions.Compiled);

    // ───────────── Garde 1 : la frontière ─────────────

    [Fact]
    public void RienHorsDuDossierNeNommeLesTypesDeSecrets()
    {
        var types = SecretTypeNames();
        Assert.NotEmpty(types);

        var word = new Regex(@"(?<![A-Za-z0-9_])(" + string.Join("|", types) + @")(?![A-Za-z0-9_])",
            RegexOptions.Compiled);

        var fautifs = new List<string>();

        foreach (var file in AppFilesOutsideSecrets())
        {
            if (EntryPoints.Contains(Path.GetFileName(file))) continue;

            var number = 0;
            foreach (var line in File.ReadLines(file))
            {
                number++;
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                if (word.IsMatch(line)) fautifs.Add($"{Path.GetFileName(file)}:{number} → {line.Trim()}");
            }
        }

        Assert.Empty(fautifs);
    }

    // ───────────── Garde 2 : rien sur disque ─────────────

    /// <summary>
    /// Le seul fichier autorisé à écrire — celui qui produit les fichiers de secrets Compose.
    /// </summary>
    /// <remarks>
    /// La garde n'a pas été supprimée quand l'écriture est devenue nécessaire : elle a été
    /// <b>restreinte</b>. La question « où un secret peut-il toucher le disque ? » garde ainsi une
    /// réponse d'un seul mot, et l'ajouter à cette liste reste un geste délibéré, visible en revue.
    /// </remarks>
    private const string DiskWriter = "SecretFileWriter.cs";

    [Fact]
    public void SeulLeRedacteurDeFichiersEcritSurLeDisque()
    {
        // Le rendu presse-papier, lui, ne doit jamais atterrir dans un fichier : le script
        // d'origine avait une option -Out, elle n'est pas portée, et ce test fait de ce choix une
        // propriété du code plutôt qu'une intention.
        var fautifs = new List<string>();

        foreach (var (file, line, number) in SecretLines())
        {
            if (Path.GetFileName(file) == DiskWriter) continue;
            if (DiskWrite.IsMatch(line))
                fautifs.Add($"{Path.GetFileName(file)}:{number} → {line.Trim()}");
        }

        Assert.Empty(fautifs);
    }

    [Fact]
    public void LeRedacteurDeFichiersExisteToujours()
    {
        // Sans lui, l'exception ci-dessus deviendrait une porte ouverte que plus rien ne referme :
        // un fichier renommé ou supprimé laisserait la liste blanche pointer dans le vide.
        Assert.True(File.Exists(Path.Combine(SecretsFolder(), DiskWriter)));
    }

    // ───────────── Garde 3 : rien en ligne de commande ─────────────

    [Fact]
    public void AucunSecretNePasseParLaLigneDeCommande()
    {
        // Une ligne de commande est lisible par tout autre processus de la machine — DockPad
        // lui-même en lit par WMI pour SwitchToProcess. Le script PowerShell d'origine passait
        // « --session $env:BW_SESSION » en argument.
        var fautifs = new List<string>();

        // On raisonne sur l'INSTRUCTION, pas sur la ligne : coupée en deux, la même violation
        // n'était détectable sur aucune des deux moitiés — ni le motif d'argument ni l'identifiant
        // suspect ne s'y trouvaient ensemble. C'est un formatage banal, pas une ruse.
        foreach (var (file, statement, number) in SecretStatements())
        {
            if (SecretOnCommandLine.IsMatch(statement))
                fautifs.Add($"{Path.GetFileName(file)}:{number} → {statement.Trim()}");

            if (!ArgumentShape.IsMatch(statement)) continue;
            if (!SecretishIdentifier.IsMatch(Strip(statement))) continue;

            fautifs.Add($"{Path.GetFileName(file)}:{number} → {statement.Trim()}");
        }

        Assert.Empty(fautifs);
    }

    /// <summary>La ligne privée des littéraux qui sont des noms de variables, pas des valeurs.</summary>
    private static string Strip(string line)
    {
        foreach (var allowed in AllowedArguments)
            line = line.Replace($"\"{allowed}\"", "", StringComparison.Ordinal);
        return line;
    }

    // ───────────── Parcours ─────────────

    /// <summary>Les types déclarés dans le dossier — ce que la frontière protège.</summary>
    private static List<string> SecretTypeNames()
    {
        // `record struct` et `record class` d'abord : sans eux, l'alternative « record » capture
        // le mot-clé suivant comme nom de type, et tout `record struct` du dépôt devient une
        // violation de frontière.
        var declaration = new Regex(
            @"\b(?:record\s+struct|record\s+class|class|record|struct|enum|interface)\s+([A-Za-z_][A-Za-z0-9_]*)",
            RegexOptions.Compiled);

        return Directory
            .EnumerateFiles(SecretsFolder(), "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => declaration.Matches(WithoutComments(File.ReadLines(f))).Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Le code seul, sans les commentaires.
    /// </summary>
    /// <remarks>
    /// <b>Une prose française déclenchait la garde.</b> Le motif de déclaration cherche
    /// <c>interface</c>, <c>class</c> ou <c>record</c> suivis d'un mot — et une phrase comme
    /// « l'interface ne fait que nommer une frontière » lui donnait un type nommé <c>ne</c>. La
    /// garde signalait alors toute ligne française du dépôt qui contient ce mot, c'est-à-dire
    /// presque toutes.
    /// </remarks>
    /// <remarks>
    /// Ne lire que le code rend la garde <b>plus juste</b>, pas plus permissive : une déclaration de
    /// type ne vit jamais dans un commentaire. Vérifié par mutation après le changement.
    /// </remarks>
    private static string WithoutComments(IEnumerable<string> lines) =>
        string.Join('\n', lines.Where(l =>
        {
            var trimmed = l.TrimStart();
            return !trimmed.StartsWith("//", StringComparison.Ordinal)
                && !trimmed.StartsWith("*", StringComparison.Ordinal);
        }));

    /// <summary>
    /// Les instructions du dossier : les lignes agrégées jusqu'au point-virgule.
    /// </summary>
    /// <remarks>
    /// Une garde qui raisonne ligne à ligne rate <c>ArgumentList.Add(</c> suivi de son argument à la
    /// ligne suivante — soit la même violation, écrite comme un formateur automatique l'écrirait.
    /// </remarks>
    private static IEnumerable<(string File, string Statement, int Number)> SecretStatements()
    {
        foreach (var group in SecretLines().GroupBy(l => l.File))
        {
            var buffer = "";
            var start = 0;

            foreach (var (file, line, number) in group)
            {
                if (buffer.Length == 0) start = number;
                buffer += " " + line.Trim();

                if (!line.TrimEnd().EndsWith(';') && !line.TrimEnd().EndsWith('{')) continue;

                yield return (file, buffer, start);
                buffer = "";
            }

            if (buffer.Length > 0) yield return (group.Key, buffer, start);
        }
    }

    private static IEnumerable<(string File, string Line, int Number)> SecretLines()
    {
        foreach (var file in Directory.EnumerateFiles(SecretsFolder(), "*.cs", SearchOption.AllDirectories))
        {
            var number = 0;
            foreach (var line in File.ReadLines(file))
            {
                number++;
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                yield return (file, line, number);
            }
        }
    }

    /// <summary>Les sources de l'application, hors du dossier et hors des projets frères.</summary>
    private static IEnumerable<string> AppFilesOutsideSecrets()
    {
        var root = RepoRoot();
        string[] skipped = ["Secrets", "DockPad.Tests", "tools", "bin", "obj", ".git", "docs"];

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            var first = relative.Split(Path.DirectorySeparatorChar)[0];
            if (skipped.Contains(first)) continue;
            yield return file;
        }
    }

    private static string SecretsFolder() => Path.Combine(RepoRoot(), "Secrets");

    private static string RepoRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", ".."));
}
