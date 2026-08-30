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
/// <b><see cref="Missing"/> n'est pas un troisième état</b>, et la nuance porte tout le sens : une
/// clé que le coffre ne connaît pas est une <i>donnée sur le coffre</i>, pas une panne. Le rendu
/// aboutit, le marqueur reste littéral dans le texte — il est sa propre trace, visible dans ce
/// qu'on colle — et la liste dit lesquels. <see cref="Failures"/> reste réservé à ce qui empêche
/// de produire quoi que ce soit : CLI absente, déverrouillage refusé, fichier illisible.
/// </para>
/// <para>
/// <b>Ce qui remplace la garantie perdue, c'est <see cref="Complete"/></b> : l'écran doit pouvoir
/// distinguer un rendu entier d'un rendu troué sans compter des éléments lui-même. La panne
/// d'origine n'était pas qu'un fichier soit partiel, c'est qu'il ait eu l'air complet.
/// </para>
/// <para>
/// Même raison d'être qu'<c>ActionResult</c> dans le projet, appliquée à un cas où le coût d'une
/// lecture distraite est un secret manquant dans un fichier déployé.
/// </para>
/// </remarks>
public sealed class SecretRenderResult
{
    private readonly string? _text;

    private SecretRenderResult(string? text, int markerCount, int itemCount,
        IReadOnlyList<string> failures, IReadOnlyList<string> missing)
    {
        _text = text;
        MarkerCount = markerCount;
        ItemCount = itemCount;
        Failures = failures;
        Missing = missing;
    }

    /// <summary>Vrai si le rendu a abouti — la seule condition pour que <see cref="Text"/> réponde.</summary>
    public bool Ok => _text is not null;

    /// <summary>Nombre de marqueurs remplacés.</summary>
    public int MarkerCount { get; }

    /// <summary>Nombre d'items de coffre distincts consultés.</summary>
    public int ItemCount { get; }

    /// <summary>Ce qui a échoué, vide si le rendu a abouti.</summary>
    public IReadOnlyList<string> Failures { get; }

    /// <summary>
    /// Ce que le coffre n'a pas su rendre — nommé, parce que ça vient du fichier source.
    /// </summary>
    /// <remarks>
    /// La distinction entre ce qu'on <b>nomme</b> et ce qu'on <b>compte</b> ne bouge pas : une clé
    /// absente vient du gabarit, la nommer est sans danger et c'est tout l'intérêt. Un
    /// <c>{{ … }}</c> survivant venu d'une <i>valeur du coffre</i>, lui, reste compté et jamais
    /// recopié — ce serait un morceau de secret à l'écran.
    /// </remarks>
    public IReadOnlyList<string> Missing { get; }

    /// <summary>Le rendu a abouti <b>et</b> ne manque de rien : le seul cas qui a droit au vert.</summary>
    public bool Complete => Ok && Missing.Count == 0;

    /// <summary>Le texte rendu. Lève si le rendu a échoué : il n'y a rien de partiel à lire.</summary>
    public string Text => _text
        ?? throw new InvalidOperationException("No text on a failed render: partial output is the failure mode this type exists to prevent.");

    public static SecretRenderResult Rendered(string text, int markerCount, int itemCount,
        IReadOnlyList<string>? missing = null) =>
        new(text, markerCount, itemCount, [], missing ?? []);

    public static SecretRenderResult Failed(IReadOnlyList<string> failures) =>
        failures.Count == 0
            ? throw new ArgumentException("A failure must say what failed.", nameof(failures))
            : new(null, 0, 0, failures, []);
}
