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
    private static readonly Regex MarkerPattern =
        new(@"\{\{\s*bw:([^:}]+):([^}\s]+)\s*\}\}", RegexOptions.Compiled);

    /// <summary>
    /// Le second filet. <c>REMPLACER</c> en fait partie : c'est le marqueur manuel qui a causé la
    /// panne d'origine, et rien ne garantit qu'il ne traîne pas encore dans un fichier.
    /// </summary>
    private static readonly Regex LeftoverPattern =
        new(@"\{\{[^}]*\}\}|REMPLACER", RegexOptions.Compiled);

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
    /// Substitue les marqueurs, ou refuse. <paramref name="lookup"/> interroge le coffre.
    /// </summary>
    public static SecretRenderResult Render(string content, Func<SecretMarker, SecretLookup> lookup)
    {
        var markers = FindMarkers(content);
        if (markers.Count == 0)
            return SecretRenderResult.Failed([Loc.T("Inject_Error_NoMarkers")]);

        var failures = new List<string>();
        var resolved = new Dictionary<SecretMarker, string>();

        foreach (var marker in markers.Distinct())
        {
            var found = lookup(marker);

            // Une valeur vide n'est pas une valeur : le champ existe mais ne porte rien, ce qui
            // produirait une ligne syntaxiquement valide et fonctionnellement fausse.
            if (string.IsNullOrEmpty(found.Value))
            {
                failures.Add(found.Failure
                    ?? Loc.F("Inject_Error_EmptyField", marker.Item, marker.Field));
                continue;
            }

            resolved[marker] = found.Value;
        }

        if (failures.Count > 0) return SecretRenderResult.Failed(Unique(failures));

        var rendered = MarkerPattern.Replace(content,
            m => resolved[new SecretMarker(m.Groups[1].Value.Trim(), m.Groups[2].Value)]);

        // On compte, on ne recopie pas. Le balayage porte sur le texte SUBSTITUÉ : une valeur du
        // coffre qui contiendrait elle-même des accolades verrait ce fragment interpolé dans le
        // message et affiché à l'écran. Partout ailleurs le périmètre ne sort que des noms et des
        // nombres ; ici il sortait un morceau de secret.
        var leftovers = FindLeftovers(rendered);
        if (leftovers.Count > 0)
            return SecretRenderResult.Failed([Loc.F("Inject_Error_Leftovers", leftovers.Count)]);

        return SecretRenderResult.Rendered(rendered, markers.Count,
            markers.Select(m => m.Item).Distinct(StringComparer.Ordinal).Count());
    }

    private static List<string> Unique(List<string> failures) =>
        failures.Distinct(StringComparer.Ordinal).ToList();
}
