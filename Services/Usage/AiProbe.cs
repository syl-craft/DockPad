namespace DockPad.Services.Usage;

/// <summary>Résultat d'une détection de fournisseur.</summary>
public sealed class AiProbe
{
    /// <summary>L'outil est installé et exploitable.</summary>
    public required bool Available { get; init; }

    /// <summary>Nom vu chez l'outil, qui alimente <c>AiProviderEntry.DetectedName</c>.</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>Dossier de données repéré, affiché en gris dans la fenêtre de config.</summary>
    public string DataPath { get; init; } = "";

    /// <summary>Précision d'affichage : version détectée, « aucune donnée », etc.</summary>
    public string Detail { get; init; } = "";

    /// <summary>Fournisseur de démonstration : l'affichage doit le signaler.</summary>
    public bool IsDemo { get; init; }

    /// <summary>
    /// Le fournisseur doit être masqué à sa <b>découverte</b>. N'agit que sur l'entrée créée : une
    /// redétection ne remasque jamais un fournisseur que l'utilisateur a affiché.
    /// </summary>
    public bool HiddenByDefault { get; init; }
}
