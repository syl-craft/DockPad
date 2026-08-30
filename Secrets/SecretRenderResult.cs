namespace DockPad.Secrets;

/// <summary>
/// Le résultat d'un rendu : <b>soit</b> un texte, <b>soit</b> une liste d'échecs. Jamais les deux.
/// </summary>
/// <remarks>
/// <para>
/// C'est la garantie centrale de la fonctionnalité, portée par le type plutôt que par la discipline
/// de l'appelant. Un objet qui porterait à la fois <c>Text</c> et <c>Failures</c> laisserait
/// quelqu'un, un jour, lire le texte sans regarder la liste — soit un rendu partiel qu'on croit
/// complet, exactement la panne que la fonctionnalité existe pour rendre impossible.
/// </para>
/// <para>
/// Même raison d'être qu'<c>ActionResult</c> dans le projet, appliquée à un cas où le coût d'une
/// lecture distraite est un secret manquant dans un fichier déployé.
/// </para>
/// </remarks>
public sealed class SecretRenderResult
{
    private readonly string? _text;

    private SecretRenderResult(string? text, int markerCount, int itemCount, IReadOnlyList<string> failures)
    {
        _text = text;
        MarkerCount = markerCount;
        ItemCount = itemCount;
        Failures = failures;
    }

    /// <summary>Vrai si le rendu a abouti — la seule condition pour que <see cref="Text"/> réponde.</summary>
    public bool Ok => _text is not null;

    /// <summary>Nombre de marqueurs remplacés.</summary>
    public int MarkerCount { get; }

    /// <summary>Nombre d'items de coffre distincts consultés.</summary>
    public int ItemCount { get; }

    /// <summary>Ce qui a échoué, vide si le rendu a abouti.</summary>
    public IReadOnlyList<string> Failures { get; }

    /// <summary>Le texte rendu. Lève si le rendu a échoué : il n'y a rien de partiel à lire.</summary>
    public string Text => _text
        ?? throw new InvalidOperationException("No text on a failed render: partial output is the failure mode this type exists to prevent.");

    public static SecretRenderResult Rendered(string text, int markerCount, int itemCount) =>
        new(text, markerCount, itemCount, []);

    public static SecretRenderResult Failed(IReadOnlyList<string> failures) =>
        failures.Count == 0
            ? throw new ArgumentException("A failure must say what failed.", nameof(failures))
            : new(null, 0, 0, failures);
}
