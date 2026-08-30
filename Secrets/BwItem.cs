namespace DockPad.Secrets;

/// <summary>
/// Un item de coffre, tel que <c>bw list items</c> le rend.
/// </summary>
/// <remarks>
/// Volontairement partiel : seuls les champs que la résolution d'un marqueur peut atteindre sont
/// déclarés. Tout le reste de la fiche — dates, dossiers, historique de mots de passe, pièces
/// jointes — est de la matière secrète qu'on n'a aucune raison de faire entrer en mémoire.
/// </remarks>
public sealed class BwItem
{
    public string Name { get; set; } = "";

    public string? Notes { get; set; }

    public BwLogin? Login { get; set; }

    public List<BwField>? Fields { get; set; }
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
