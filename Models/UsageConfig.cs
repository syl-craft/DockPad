namespace DockPad.Models;

/// <summary>Contenu de %APPDATA%\DockPad\usage.json.</summary>
public class UsageConfig
{
    /// <summary>Affiche le bandeau dans QuickAccessWindow.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Pourcentage restant sous lequel une jauge passe au rouge.</summary>
    public int AlertThreshold { get; set; } = 15;

    /// <summary>Affiche la colonne « Coût est. ».</summary>
    public bool ShowCost { get; set; } = true;

    /// <summary>
    /// Fournisseur affiché à l'ouverture. Vide = le premier visible. Un clic sur un onglet ne
    /// modifie pas ce réglage : la sélection est propre à la session, ce qui évite une écriture
    /// dans le fichier à chaque clic — donc une course avec la fenêtre de réglages ouverte.
    /// </summary>
    public string DefaultProviderId { get; set; } = "";

    public List<AiProviderEntry> Providers { get; set; } = new();
}
