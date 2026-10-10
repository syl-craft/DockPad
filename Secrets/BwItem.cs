using System.Text.Json.Serialization;

namespace DockPad.Secrets;

/// <summary>
/// Un item de coffre, tel que <c>bw list items</c> le rend.
/// </summary>
/// <remarks>
/// Volontairement partiel : seuls les champs que la résolution d'un marqueur peut atteindre sont
/// déclarés. Tout le reste de la fiche — dates, dossiers, historique de mots de passe — est de la
/// matière secrète qu'on n'a aucune raison de faire entrer en mémoire. Des pièces jointes, seules
/// les métadonnées sont lues ici ; le contenu ne l'est qu'à la demande d'un marqueur.
/// </remarks>
public sealed class BwItem
{
    /// <summary>
    /// L'identifiant de la fiche — nécessaire pour la relire en entier puis la réécrire
    /// (<c>bw get item</c>, <c>bw edit item</c>). Ce n'est pas un secret.
    /// </summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string? Notes { get; set; }

    public BwLogin? Login { get; set; }

    public List<BwField>? Fields { get; set; }

    public List<BwAttachment>? Attachments { get; set; }
}

/// <summary>
/// Les métadonnées d'une pièce jointe, jamais son contenu.
/// </summary>
public sealed class BwAttachment
{
    public string Id { get; set; } = "";

    public string? FileName { get; set; }

    /// <summary>
    /// Taille en octets. La CLI l'écrit en chaîne (<c>"2345"</c>), d'où la lecture tolérante.
    /// </summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? Size { get; set; }
}

/// <summary>Les champs de connexion d'une fiche.</summary>
public sealed class BwLogin
{
    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? Totp { get; set; }
}

/// <summary>Un champ personnalisé, celui que les marqueurs visent en premier.</summary>
public sealed class BwField
{
    public string? Name { get; set; }

    public string? Value { get; set; }
}
