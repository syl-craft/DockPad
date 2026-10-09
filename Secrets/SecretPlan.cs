namespace DockPad.Secrets;

/// <summary>Ce que le fichier demande qu'on fasse de lui.</summary>
public enum SecretMode
{
    /// <summary>Ni marqueur ni annotation : il n'y a rien à faire.</summary>
    None,

    /// <summary>Des marqueurs <c>{{ bw:… }}</c> : rendu dans le presse-papier.</summary>
    Clipboard,

    /// <summary>Des annotations <c>x-bw</c> : écriture des fichiers de secrets.</summary>
    Files,

    /// <summary>Les deux à la fois : on produit les deux.</summary>
    Both,

    /// <summary>
    /// Un inventaire <c>.vault</c> (en-tête <c># github-secrets</c> ou <c># github-variables</c>) : secrets ou variables
    /// envoyés à GitHub Actions.
    /// </summary>
    GitHub,
}

/// <summary>
/// Décide, à la seule lecture du contenu, lequel des deux modes s'applique.
/// </summary>
/// <remarks>
/// <para>
/// Une seule entrée de menu, et le fichier dit lui-même ce qu'il veut — il n'y a rien à choisir au
/// moment du clic, et rien à se rappeler.
/// </para>
/// <para>
/// <b>Les marqueurs tranchent en premier.</b> Un <c>.env</c> porte des marqueurs et n'est pas du
/// YAML : laisser le parseur décider d'abord ferait basculer ce cas courant vers un message
/// d'erreur YAML sans rapport avec ce que l'utilisateur essaie de faire.
/// </para>
/// <para>
/// <b>Les deux à la fois produisent les deux.</b> C'était un refus — « deviner serait pire que
/// demander » — mais ce raisonnement supposait qu'il fallait <i>choisir</i>. Faire les deux ne
/// devine rien : chaque source produit sa sortie, à sa place, en un seul déverrouillage du coffre.
/// </para>
/// </remarks>
public static class SecretPlan
{
    public static SecretMode Of(string content)
    {
        // L'en-tête tranche avant tout : un inventaire porte des marqueurs, et sans lui il partirait
        // dans le presse-papier.
        if (GitHubInventory.Declares(content)) return SecretMode.GitHub;

        var hasMarkers = SecretTemplate.FindMarkers(content).Count > 0;
        var hasAnnotations = ComposeSecrets.Extract(content).HasAnnotations;

        return (hasMarkers, hasAnnotations) switch
        {
            (true, true) => SecretMode.Both,
            (true, false) => SecretMode.Clipboard,
            (false, true) => SecretMode.Files,
            _ => SecretMode.None,
        };
    }
}
