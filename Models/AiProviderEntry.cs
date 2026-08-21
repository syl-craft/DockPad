namespace DockPad.Models;

/// <summary>
/// Un fournisseur IA tel que <c>usage.json</c> le mémorise : ce que l'utilisateur en a décidé
/// (nom, visibilité, ordre) et ce que la dernière détection en a vu.
/// </summary>
/// <remarks>
/// <see cref="Id"/> n'est pas <c>required</c>, contrairement aux modèles construits en code : ce
/// type est désérialisé, et un <c>required</c> ferait échouer la lecture du fichier entier à cause
/// d'une seule entrée mal formée. <c>UsageConfigService</c> écarte les entrées sans id et garde le
/// reste — perdre les masquages et l'ordre de tous les fournisseurs pour une entrée abîmée serait
/// une punition disproportionnée.
/// </remarks>
public class AiProviderEntry
{
    /// <summary>Clé de fusion, égale à <c>IUsageProvider.Id</c>.</summary>
    public string Id { get; set; } = "";

    /// <summary>Nom affiché, personnalisable dans DockPad.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Nom vu à la dernière détection. Sert à distinguer un nom personnalisé d'un nom hérité :
    /// tant que <see cref="Name"/> lui est égal, il suit l'outil ; dès qu'il en diffère, il ne bouge plus.
    /// </summary>
    public string DetectedName { get; set; } = "";

    /// <summary>Masqué : absent du bandeau, conservé dans la config.</summary>
    public bool Hidden { get; set; }

    /// <summary>Rang d'affichage.</summary>
    public int Order { get; set; }

    /// <summary>Dossier de données repéré par la sonde, affiché en gris dans la fenêtre de config.</summary>
    public string DataPath { get; set; } = "";

    /// <summary>Présent à la dernière détection. Faux = conservé mais signalé « non détecté ».</summary>
    public bool Detected { get; set; }
}
