using System.IO;
using YamlDotNet.RepresentationModel;

namespace DockPad.Secrets;

/// <summary>Un secret Compose annoté : la clé, le fichier à produire, et où trouver la valeur.</summary>
/// <param name="Key">La clé du bloc <c>secrets:</c>, pour nommer les échecs.</param>
/// <param name="FileName">
/// Le <b>nom de base</b> de <c>file:</c>. Le chemin complet vise le NAS et n'est pas exploitable
/// ici ; et il diffère de <paramref name="Key"/> du préfixe <c>vw-</c>, les confondre produirait un
/// fichier que Compose ne trouverait pas.
/// </param>
public sealed record ComposeSecret(string Key, string FileName, SecretMarker Marker);

/// <summary>Ce qu'un document a livré : des secrets annotés, des annotations fautives, ou rien.</summary>
/// <param name="Failures">Annotations incomplètes — elles <b>prouvent</b> qu'on est sur un compose à générer.</param>
/// <param name="YamlError">
/// Le document ne se lit pas comme du YAML. Rapporté <b>à part</b> des annotations fautives, parce
/// que les deux ne disent pas la même chose : un <c>.env</c> ou un <c>.png</c> n'est pas du YAML, et
/// ce n'est pas une panne — le prendre pour une intention ferait répondre « pas du YAML lisible » à
/// qui a simplement visé le mauvais fichier.
/// </param>
public sealed record ComposeScan(
    IReadOnlyList<ComposeSecret> Entries,
    IReadOnlyList<string> Failures,
    string? YamlError)
{
    /// <summary>Le document porte-t-il une intention de générer des fichiers ?</summary>
    public bool HasAnnotations => Entries.Count > 0 || Failures.Count > 0;
}

/// <summary>
/// Lit les annotations <c>x-bw</c> du bloc <c>secrets:</c> d'un docker-compose.
/// </summary>
/// <remarks>
/// <para>
/// <c>x-</c> est le mécanisme d'extension prévu par la spécification Compose : Compose ignore ces
/// champs, DockPad les lit. Rien à faire côté déploiement pour que l'annotation cohabite.
/// </para>
/// <para>
/// <b>Un vrai parseur, et non un balayage textuel.</b> Le compose de référence porte un
/// <c>entrypoint</c> de quatre-vingt-dix lignes en scalaire bloc, avec des <c>$$</c>, des listes
/// imbriquées et des accolades — un scanner maison s'y casse. Et surtout, ce même fichier
/// <b>documente <c>x-bw</c> dans un commentaire</b> : une détection textuelle basculerait en mode
/// fichiers sur un document qui ne porte aucune annotation.
/// </para>
/// </remarks>
public static class ComposeSecrets
{
    /// <summary>Les secrets annotés, et ce qui n'a pas pu être lu.</summary>
    public static ComposeScan Extract(string yaml)
    {
        var entries = new List<ComposeSecret>();
        var failures = new List<string>();

        YamlMappingNode? root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            root = stream.Documents.Count == 0 ? null : stream.Documents[0].RootNode as YamlMappingNode;
        }
        catch (Exception ex)
        {
            return new ComposeScan([], [], Loc.F("Inject_Error_YamlUnreadable", ex.GetType().Name));
        }

        if (Child(root, "secrets") is not YamlMappingNode secrets) return new ComposeScan([], [], null);

        foreach (var (nameNode, definition) in secrets.Children)
        {
            if (definition is not YamlMappingNode secret) continue;
            if (Child(secret, "x-bw") is not YamlMappingNode annotation) continue;

            var key = Scalar(nameNode) ?? "";
            var item = Scalar(Child(annotation, "item"));
            var field = Scalar(Child(annotation, "field"));

            if (string.IsNullOrWhiteSpace(item) || string.IsNullOrWhiteSpace(field))
            {
                failures.Add(Loc.F("Inject_Error_SecretIncomplete", key));
                continue;
            }

            var file = Scalar(Child(secret, "file"));
            if (string.IsNullOrWhiteSpace(file))
            {
                // Sans `file:`, aucun nom de fichier à produire. Le taire laisserait croire que ce
                // secret a été traité.
                failures.Add(Loc.F("Inject_Error_SecretNoFile", key));
                continue;
            }

            entries.Add(new ComposeSecret(key, BaseName(file), new SecretMarker(item, field)));
        }

        return new ComposeScan(entries, failures, null);
    }

    /// <summary>
    /// Le nom de base d'un chemin <b>POSIX</b> : <c>file:</c> vise le NAS, pas Windows.
    /// </summary>
    /// <remarks>
    /// <c>Path.GetFileName</c> conviendrait sous Windows, où il coupe aussi sur <c>/</c> ; on
    /// découpe explicitement sur les deux séparateurs pour que la règle ne dépende pas de la
    /// plateforme qui exécute les tests.
    /// </remarks>
    private static string BaseName(string path) =>
        path.Split('/', '\\')[^1];

    private static YamlNode? Child(YamlNode? node, string key) =>
        node is YamlMappingNode map && map.Children.TryGetValue(new YamlScalarNode(key), out var child)
            ? child
            : null;

    private static string? Scalar(YamlNode? node) => (node as YamlScalarNode)?.Value;
}
