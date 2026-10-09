using System.Text.RegularExpressions;

namespace DockPad.Secrets;

/// <summary>Ce qu'un inventaire alimente côté GitHub Actions.</summary>
public enum GitHubTargetKind { Secrets, Variables }

/// <summary>Une ligne <c>NOM={{ bw:… }}</c>, variables déjà substituées.</summary>
/// <param name="Template">La référence au coffre, jamais une valeur.</param>
public sealed record GitHubEntry(string Name, string Template);

/// <summary>Une ligne rendue : le nom GitHub et sa valeur, prête à partir par l'entrée standard.</summary>
public sealed record GitHubValue(string Name, string Value);

/// <summary>Un secret ou une variable tel que <c>gh … list</c> le rend : un nom et une date, jamais une valeur.</summary>
public sealed record GitHubRemoteEntry(string Name, DateTimeOffset? UpdatedAt);

/// <summary>L'inventaire comparé à GitHub, sans rien déverrouiller.</summary>
/// <param name="Missing">Dans l'inventaire, absents de GitHub : ils seront créés.</param>
/// <param name="Extra">Sur GitHub, absents de l'inventaire : signalés avec leur âge, jamais supprimés.</param>
/// <param name="Present">Des deux côtés : ils seront écrasés, et leur âge se lit ici.</param>
public sealed record GitHubCheck(
    IReadOnlyList<string> Missing, IReadOnlyList<GitHubRemoteEntry> Extra, IReadOnlyList<GitHubRemoteEntry> Present);

/// <summary>
/// PUR — un inventaire <c>.vault</c> : la cible GitHub, et pour chaque nom une référence au coffre.
/// </summary>
/// <remarks>
/// <para>
/// <b>Le dépôt versionne des références, jamais des valeurs.</b> Une ligne sans marqueur est un refus,
/// même pour une valeur publique : un inventaire qui en tolère une finit par en porter une qui ne
/// l'est pas.
/// </para>
/// <para>
/// <b>Les variables (<c>@nom = valeur</c>) sont des littéraux</b>, cités par <c>${nom}</c> dans
/// l'en-tête et dans les lignes. Elles évitent de répéter le dépôt ou le nom d'item, et ne partent
/// jamais vers GitHub. Une variable ne cite que celles définies avant elle, et ne porte pas de
/// marqueur : sinon elle deviendrait une seconde façon d'écrire une valeur du coffre.
/// </para>
/// <para>
/// <b>Tout ce qui est mal formé refuse le fichier entier</b>, avant le mot de passe maître : un
/// inventaire à moitié compris enverrait à moitié, et c'est la panne que le rendu strict existe pour
/// empêcher.
/// </para>
/// </remarks>
public sealed class GitHubInventory
{
    /// <summary>
    /// <c># github-secrets …</c> ou <c># github-variables …</c> — et leurs fautes de frappe au
    /// singulier, reconnues pour être <b>refusées</b>.
    /// </summary>
    /// <remarks>
    /// L'en-tête nomme la cible et non l'outil : DockPad n'est qu'un des outils qui savent lire ce
    /// fichier. Le motif reste étroit pour qu'un commentaire ordinaire — <c># github-token du CI</c> —
    /// ne transforme pas un <c>.env</c> en inventaire.
    /// </remarks>
    private static readonly Regex Header = new(@"^\s*#\s*(?<kind>github-(?:secrets?|variables?|vars?))(?=\s|$)(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Variable = new(@"^\s*@(?<name>[A-Za-z_][A-Za-z0-9_-]*)\s*=\s*(?<value>.*?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex Entry = new(@"^\s*(?<name>[^=]*?)\s*=\s*(?<value>.*?)\s*$", RegexOptions.Compiled);

    private static readonly Regex Reference = new(@"\$\{(?<name>[A-Za-z_][A-Za-z0-9_-]*)\}", RegexOptions.Compiled);

    /// <summary>Les noms qu'accepte GitHub Actions — lettres, chiffres, souligné, sans chiffre en tête.</summary>
    private static readonly Regex GitHubName = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private static readonly Regex RepoShape = new(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

    private GitHubInventory(GitHubTargetKind kind, string repo, string? environment, IReadOnlyList<GitHubEntry> entries)
    {
        Kind = kind;
        Repo = repo;
        Environment = environment;
        Entries = entries;
    }

    public GitHubTargetKind Kind { get; }

    /// <summary><c>propriétaire/dépôt</c>.</summary>
    public string Repo { get; }

    /// <summary>L'environnement GitHub, ou <c>null</c> pour les secrets et variables du dépôt.</summary>
    public string? Environment { get; }

    public IReadOnlyList<GitHubEntry> Entries { get; }

    /// <summary>
    /// Les références seules, une par ligne : ce que le coffre doit résoudre, et ce que le formulaire
    /// de création propose quand il manque quelque chose.
    /// </summary>
    public string MarkersText => string.Join("\n", Entries.Select(e => e.Template));

    /// <summary>Le fichier se déclare-t-il inventaire ? Une ligne <c># github-secrets</c> ou <c># github-variables</c> suffit.</summary>
    /// <remarks>
    /// Le type n'est pas vérifié ici : une faute de frappe (<c>github-secret</c>) doit être refusée
    /// par <see cref="Parse"/>, pas faire retomber le fichier — qui porte des marqueurs — vers le
    /// presse-papier sans un mot.
    /// </remarks>
    public static bool Declares(string content) => Lines(content).Where(l => Header.IsMatch(l)).Any();

    /// <summary>L'inventaire, ou ce qui l'empêche d'en être un. Jamais les deux.</summary>
    public static (GitHubInventory? Inventory, IReadOnlyList<string> Failures) Parse(string content)
    {
        var failures = new List<string>();
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        var rawEntries = new List<(string Name, string Value, int Line)>();
        Match? header = null;

        var number = 0;
        foreach (var line in Lines(content))
        {
            number++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (Header.Match(line) is { Success: true } declared)
            {
                // Deux en-têtes, deux cibles possibles : garder l'un en silence enverrait peut-être
                // au mauvais dépôt.
                if (header != null) failures.Add(Loc.T("GitHub_Error_SecondHeader"));
                header ??= declared;
                continue;
            }

            if (line.TrimStart().StartsWith('#')) continue;

            if (Variable.Match(line) is { Success: true } variable)
            {
                DefineVariable(variable.Groups["name"].Value, variable.Groups["value"].Value, number, variables, failures);
                continue;
            }

            if (Entry.Match(line) is { Success: true } entry)
            {
                rawEntries.Add((entry.Groups["name"].Value, entry.Groups["value"].Value, number));
                continue;
            }

            failures.Add(Loc.F("GitHub_Error_Unreadable", number));
        }

        if (header is null) return (null, [Loc.T("GitHub_Error_NoHeader")]);

        var (kind, repo, environment) = ReadHeader(header, variables, failures);
        var entries = ReadEntries(rawEntries, variables, failures);

        if (entries.Count == 0 && failures.Count == 0) failures.Add(Loc.T("GitHub_Error_NoEntries"));

        if (failures.Count > 0 || kind is null || repo is null)
            return (null, failures.Distinct(StringComparer.Ordinal).ToList());

        return (new GitHubInventory(kind.Value, repo, environment, entries), []);
    }

    /// <summary>
    /// Rend chaque ligne, <b>tout ou rien</b> : un seul marqueur non résolu, et rien ne part.
    /// </summary>
    /// <remarks>
    /// Même règle que les fichiers de secrets, pour la même raison : personne ne relit ce qui part
    /// sur GitHub, et un workflow qui publie sur trois stores avec une clé manquante échouerait loin
    /// d'ici, sans dire laquelle.
    /// </remarks>
    /// <remarks>
    /// <b>Le second filet est repassé sur chaque valeur rendue</b> : <c>RenderStrict</c> ne vérifie
    /// que les marqueurs qu'il a trouvés, et une valeur du coffre qui porterait un <c>{{ … }}</c>
    /// partirait telle quelle. Le reste est compté, jamais recopié — ce serait un morceau de secret.
    /// </remarks>
    public (IReadOnlyList<GitHubValue>? Values, IReadOnlyList<string> Missing) Render(
        Func<SecretMarker, SecretLookup> lookup)
    {
        var values = new List<GitHubValue>();
        var missing = new List<string>();

        foreach (var entry in Entries)
        {
            var (text, entryMissing) = SecretTemplate.RenderStrict(entry.Template, lookup);

            if (text is null)
            {
                missing.AddRange(entryMissing);
                continue;
            }

            var leftovers = SecretTemplate.FindLeftovers(text).Count;
            if (leftovers > 0)
            {
                missing.Add(Loc.F("GitHub_Error_Leftovers", entry.Name, leftovers));
                continue;
            }

            values.Add(new GitHubValue(entry.Name, text));
        }

        return missing.Count > 0
            ? (null, missing.Distinct(StringComparer.Ordinal).ToList())
            : (values, []);
    }

    /// <summary>L'inventaire face à ce que GitHub porte déjà. Les noms se comparent sans égard à la casse.</summary>
    public GitHubCheck Compare(IReadOnlyList<GitHubRemoteEntry> remote)
    {
        var declared = Entries.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var onGitHub = remote.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = Entries
            .Where(e => !onGitHub.Contains(e.Name))
            .Select(e => e.Name)
            .ToList();

        var extra = remote
            .Where(r => !declared.Contains(r.Name))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var present = Entries
            .Where(e => onGitHub.Contains(e.Name))
            .Select(e => new GitHubRemoteEntry(e.Name, remote
                .Where(r => string.Equals(r.Name, e.Name, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.UpdatedAt)
                .FirstOrDefault()))
            .ToList();

        return new GitHubCheck(missing, extra, present);
    }

    /// <summary>L'âge en jours entiers d'une mise à jour, ou <c>null</c> si GitHub ne l'a pas donnée.</summary>
    /// <remarks>Sert à voir venir une clé qui expire — celle d'Edge dure environ soixante-dix jours.</remarks>
    public static int? AgeInDays(DateTimeOffset? updatedAt, DateTimeOffset now) =>
        updatedAt == null ? null : Math.Max(0, (int)(now - updatedAt.Value).TotalDays);

    // ───────────── Analyse ─────────────

    private static void DefineVariable(string name, string rawValue, int line,
        Dictionary<string, string> variables, List<string> failures)
    {
        if (variables.ContainsKey(name))
        {
            failures.Add(Loc.F("GitHub_Error_VariableRedefined", name));
            return;
        }

        var value = Expand(rawValue, line, variables, failures);
        if (value.Contains("{{", StringComparison.Ordinal))
        {
            failures.Add(Loc.F("GitHub_Error_VariableMarker", name));
            return;
        }

        variables[name] = value;
    }

    private static (GitHubTargetKind? Kind, string? Repo, string? Environment) ReadHeader(
        Match header, Dictionary<string, string> variables, List<string> failures)
    {
        var declaredKind = header.Groups["kind"].Value;
        GitHubTargetKind? kind = declaredKind.ToLowerInvariant() switch
        {
            "github-secrets" => GitHubTargetKind.Secrets,
            "github-variables" => GitHubTargetKind.Variables,
            _ => null,
        };

        if (kind is null) failures.Add(Loc.F("GitHub_Error_UnknownKind", declaredKind));

        string? repo = null;
        string? environment = null;
        var given = new HashSet<string>(StringComparer.Ordinal);

        foreach (var token in header.Groups["rest"].Value.Split(' ', '\t').Where(t => t.Length > 0))
        {
            var separator = token.IndexOf('=');
            var key = (separator < 0 ? token : token[..separator]).ToLowerInvariant();
            var value = separator < 0 ? "" : Expand(token[(separator + 1)..], line: 1, variables, failures);

            if (key is not ("repo" or "environment"))
            {
                failures.Add(Loc.F("GitHub_Error_UnknownParameter", key));
                continue;
            }

            // Répété, il changerait de cible selon la dernière valeur ; vide, environment= ne dirait
            // pas s'il vise le dépôt par intention ou par oubli. L'absence du paramètre, elle, est claire.
            if (!given.Add(key) || value.Length == 0)
            {
                failures.Add(Loc.F("GitHub_Error_AmbiguousParameter", key));
                continue;
            }

            if (key == "repo") repo = value;
            else environment = value;
        }

        if (repo is null)
        {
            failures.Add(Loc.T("GitHub_Error_RepoMissing"));
            return (kind, null, environment);
        }

        if (!RepoShape.IsMatch(repo))
        {
            failures.Add(Loc.F("GitHub_Error_RepoInvalid", repo));
            return (kind, null, environment);
        }

        return (kind, repo, environment);
    }

    private static List<GitHubEntry> ReadEntries(
        List<(string Name, string Value, int Line)> rawEntries,
        Dictionary<string, string> variables, List<string> failures)
    {
        var entries = new List<GitHubEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, rawValue, line) in rawEntries)
        {
            if (!GitHubName.IsMatch(name))
            {
                failures.Add(Loc.F("GitHub_Error_BadName", name, line));
                continue;
            }

            // GitHub refuse ce préfixe : autant le dire ici que de le découvrir à la moitié de l'envoi.
            if (name.StartsWith("GITHUB_", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(Loc.F("GitHub_Error_ReservedName", name));
                continue;
            }

            if (!seen.Add(name))
            {
                failures.Add(Loc.F("GitHub_Error_DuplicateName", name));
                continue;
            }

            // Un marqueur, et rien d'autre : du texte autour serait une valeur en clair qui part sur
            // GitHub, qu'il vienne de la ligne ou d'une variable substituée.
            var template = Expand(rawValue, line, variables, failures);
            if (!SecretTemplate.IsSingleMarker(template))
            {
                failures.Add(Loc.F("GitHub_Error_PlainValue", name));
                continue;
            }

            entries.Add(new GitHubEntry(name, template));
        }

        return entries;
    }

    /// <summary>Substitue les <c>${nom}</c> ; une variable inconnue est nommée, jamais remplacée par du vide.</summary>
    private static string Expand(string text, int line, Dictionary<string, string> variables, List<string> failures) =>
        Reference.Replace(text, m =>
        {
            var name = m.Groups["name"].Value;
            if (variables.TryGetValue(name, out var value)) return value;

            failures.Add(Loc.F("GitHub_Error_UnknownVariable", name, line));
            return m.Value;
        });

    private static IEnumerable<string> Lines(string content) =>
        content.Replace("\r\n", "\n").Split('\n');
}
