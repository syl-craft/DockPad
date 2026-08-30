using System.Text.RegularExpressions;

namespace DockPad.Secrets;

/// <summary>
/// Le cœur du rendu : trouver les marqueurs, substituer, et refuser tout ce qui n'est pas complet.
/// </summary>
/// <remarks>
/// <para>
/// Entièrement pur — aucun accès au coffre, aucune IO, aucun WPF. Le coffre entre par un
/// <see cref="Func{T, TResult}"/>, ce qui permet de vérifier les deux filets sans jamais lancer la
/// CLI Bitwarden.
/// </para>
/// <para>
/// <b>Deux filets, et le second ne fait pas confiance au premier.</b> Le premier collecte les
/// marqueurs que le coffre n'a pas résolus ; le second regarde le texte produit et rejette tout
/// <c>{{ … }}</c> ou <c>REMPLACER</c> survivant, quelle qu'en soit l'origine — y compris une valeur
/// du coffre qui en porterait un. Un rendu partiel est pire que pas de rendu : c'est très
/// exactement la panne que cette fonctionnalité existe pour rendre impossible.
/// </para>
/// </remarks>
public static class SecretTemplate
{
    /// <summary>
    /// Syntaxe d'un marqueur, reprise telle quelle du script PowerShell où elle a été éprouvée.
    /// </summary>
    /// <remarks>
    /// <b>Le nom d'item accepte les espaces</b>, le nom de champ non. Le script d'origine les
    /// excluait des deux, et un marqueur comme <c>{{ bw:NAS QNAP:token }}</c> n'était alors pas vu
    /// <i>du tout</i> : l'utilisateur s'entendait répondre « aucun marqueur » devant un fichier qui
    /// n'en manquait pas. Les espaces sont la norme dans les noms d'items de coffre, et
    /// <see cref="SecretVault"/> les résout très bien. Le <c>:</c> et le <c>}}</c> délimitent
    /// suffisamment ; le nom est simplement rogné de ses espaces de bord.
    /// </remarks>
    /// <remarks>
    /// <b>Le <c>(?&lt;!\\)</c> de tête est l'échappement</b> : un antislash devant les accolades dit
    /// « montre ce marqueur, ne le résous pas ». Un fichier qui documente la syntaxe — un
    /// <c>CLAUDE.md</c>, un README — pouvait sinon passer pour un fichier à secrets. Posé sur ce
    /// motif seul, il couvre du même coup la détection, la substitution, le compteur de marqueurs
    /// et <see cref="SecretPlan"/> : un fichier entièrement échappé n'a « rien à produire ».
    /// </remarks>
    private static readonly Regex MarkerPattern =
        new(@"(?<!\\)\{\{\s*bw:([^:}]+):([^}\s]+)\s*\}\}", RegexOptions.Compiled);

    /// <summary>Ce qui retire l'antislash, une fois tout le reste vérifié.</summary>
    private const string Escaped = @"\{{";
    private const string Unescaped = "{{";

    /// <summary>
    /// Le second filet. <c>REMPLACER</c> en fait partie : c'est le marqueur manuel qui a causé la
    /// panne d'origine, et rien ne garantit qu'il ne traîne pas encore dans un fichier.
    /// </summary>
    /// <remarks>
    /// Le même <c>(?&lt;!\\)</c> : un marqueur échappé produit un <c>{{ … }}</c> littéral, et ce filet
    /// le rejetterait — donc l'échappement se ferait refuser par la garde censée nous protéger.
    /// <c>REMPLACER</c>, lui, reste inconditionnel : c'est une sentinelle manuelle, rien ne demande
    /// à l'échapper.
    /// </remarks>
    /// <summary>Le marqueur manuel du script d'origine — un littéral, donc nommable sans risque.</summary>
    private const string ManualMarker = "REMPLACER";

    private static readonly Regex LeftoverPattern =
        new(@"(?<!\\)\{\{[^}]*\}\}|REMPLACER", RegexOptions.Compiled);

    /// <summary>Les marqueurs du texte, dans l'ordre où ils apparaissent, doublons compris.</summary>
    public static IReadOnlyList<SecretMarker> FindMarkers(string content) =>
        MarkerPattern.Matches(content)
            .Select(m => new SecretMarker(m.Groups[1].Value.Trim(), m.Groups[2].Value))
            .ToList();

    /// <summary>Ce qui ressemble encore à un marqueur après rendu — le second filet.</summary>
    public static IReadOnlyList<string> FindLeftovers(string rendered) =>
        LeftoverPattern.Matches(rendered)
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Substitue ce que le coffre sait rendre, et dit ce qu'il ne savait pas.
    /// <paramref name="lookup"/> interroge le coffre.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Une clé absente n'annule plus le rendu.</b> C'est un renversement assumé de la règle
    /// d'origine : le coffre qui ne connaît pas un item est un fait <i>sur le coffre</i>, pas une
    /// panne du rendu, et bloquer les quatre secrets présents à cause du cinquième coûtait plus
    /// qu'il ne protégeait. Le marqueur non résolu <b>reste littéral</b> dans le texte : il est sa
    /// propre trace, visible dans ce qu'on colle, et le réécrire en <c>&lt;&lt;MANQUANT&gt;&gt;</c>
    /// ferait perdre l'information de ce qu'il fallait y mettre.
    /// </para>
    /// <para>
    /// <b>Ce qui reste bloquant</b> : n'avoir rien résolu du tout. Un texte où aucun marqueur n'a
    /// été remplacé n'est pas un rendu, c'est le fichier de départ — l'annoncer comme un succès
    /// partiel serait un mensonge poli.
    /// </para>
    /// </remarks>
    public static SecretRenderResult Render(string content, Func<SecretMarker, SecretLookup> lookup)
    {
        var markers = FindMarkers(content);
        if (markers.Count == 0)
            return SecretRenderResult.Failed([Loc.T("Inject_Error_NoMarkers")]);

        var missing = new List<string>();
        var unresolved = new HashSet<SecretMarker>();
        var resolved = new Dictionary<SecretMarker, string>();

        foreach (var marker in markers.Distinct())
        {
            var found = lookup(marker);

            // Une valeur vide n'est pas une valeur : le champ existe mais ne porte rien, ce qui
            // produirait une ligne syntaxiquement valide et fonctionnellement fausse.
            if (string.IsNullOrEmpty(found.Value))
            {
                missing.Add(found.Failure
                    ?? Loc.F("Inject_Error_EmptyField", marker.Item, marker.Field));
                unresolved.Add(marker);
                continue;
            }

            resolved[marker] = found.Value;
        }

        if (resolved.Count == 0) return SecretRenderResult.Failed(Unique(missing));

        var replaced = 0;
        var rendered = MarkerPattern.Replace(content, m =>
        {
            var marker = new SecretMarker(m.Groups[1].Value.Trim(), m.Groups[2].Value);
            if (!resolved.TryGetValue(marker, out var value)) return m.Value;

            replaced++;
            return value;
        });

        // Le second filet ne veto plus, mais il n'a rien perdu de son role : il rapporte ce qu'il
        // ne CONNAIT pas. Un marqueur qu'on a soi-meme laisse en place est deja nomme dans la
        // liste ; ce qui survit en plus vient d'ailleurs — d'une valeur du coffre, ou d'un
        // REMPLACER oublie dans le fichier.
        var foreign = FindLeftovers(rendered).Where(v => !IsKnown(v, unresolved)).ToList();

        // REMPLACER se nomme : c'est un litteral du fichier source, connu, et c'est la panne
        // d'origine — celle d'une stack deployee avec ses marqueurs manuels jamais remplaces.
        if (foreign.Remove(ManualMarker)) missing.Add(Loc.T("Inject_Missing_Replacer"));

        // Le reste se COMPTE et ne se recopie pas : un « {{ … }} » venu d'une valeur du coffre
        // afficherait un morceau de secret a l'ecran. Cette regle-la ne bouge pas.
        if (foreign.Count > 0) missing.Add(Loc.F("Inject_Error_Leftovers", foreign.Count));

        return SecretRenderResult.Rendered(
            rendered.Replace(Escaped, Unescaped),
            replaced,
            resolved.Keys.Select(m => m.Item).Distinct(StringComparer.Ordinal).Count(),
            Unique(missing));
    }

    /// <summary>Ce reste est-il un marqueur qu'on a soi-même laissé en place ?</summary>
    private static bool IsKnown(string leftover, HashSet<SecretMarker> unresolved)
    {
        var m = MarkerPattern.Match(leftover);
        return m.Success
            && unresolved.Contains(new SecretMarker(m.Groups[1].Value.Trim(), m.Groups[2].Value));
    }

    private static List<string> Unique(List<string> failures) =>
        failures.Distinct(StringComparer.Ordinal).ToList();
}
