using System.Text.Json.Serialization;

namespace DockPad.Models;

/// <summary>Un navigateur proposé dans la popup de choix.</summary>
public class BrowserEntry
{
    public string Id { get; set; } = NewId();
    public string Name { get; set; } = "";
    public string ExePath { get; set; } = "";

    /// <summary>Arguments optionnels. "%1" est substitué par l'URL, sinon l'URL est ajoutée en fin.</summary>
    public string Arguments { get; set; } = "";

    public string IconPath { get; set; } = "";

    /// <summary>
    /// Id du navigateur parent quand cette entrée est un profil, null pour un navigateur.
    /// Sert au regroupement d'affichage et au déplacement en bloc.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentId { get; set; }

    /// <summary>
    /// Dossier du profil dans le « User Data » du navigateur (ex. "Default", "Profile 1").
    /// Null = navigateur nu, lancé sans --profile-directory (dernier profil utilisé).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProfileDirectory { get; set; }

    /// <summary>
    /// Nom lu dans le navigateur à la dernière détection. Permet de distinguer un nom
    /// personnalisé dans DockPad (préservé) d'un nom encore automatique (mis à jour).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DetectedName { get; set; }

    /// <summary>Chemin dans le dossier de profil (%APPDATA%\DockPad\icons\). Prioritaire pour l'affichage.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IconProfilePath { get; set; }

    /// <summary>Masqué = absent de la popup mais conservé dans la config.</summary>
    public bool Hidden { get; set; }

    public int Order { get; set; }

    /// <summary>Identifiant stable (8 hex) référencé par les règles de domaine.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N")[..8];
}
