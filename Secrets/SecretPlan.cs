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

    /// <summary>Les deux à la fois : refus.</summary>
    Ambiguous,
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
/// <b>Les deux à la fois sont un refus</b>, jamais un choix implicite : les deux modes produisent
/// des choses différentes, à des endroits différents, et deviner serait pire que demander.
/// </para>
/// </remarks>
public static class SecretPlan
{
    public static SecretMode Of(string content)
    {
        var hasMarkers = SecretTemplate.FindMarkers(content).Count > 0;
        var hasAnnotations = ComposeSecrets.Extract(content).HasAnnotations;

        return (hasMarkers, hasAnnotations) switch
        {
            (true, true) => SecretMode.Ambiguous,
            (true, false) => SecretMode.Clipboard,
            (false, true) => SecretMode.Files,
            _ => SecretMode.None,
        };
    }
}
