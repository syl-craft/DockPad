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

    /// <summary>Chemin dans le dossier de profil (%APPDATA%\DockPad\icons\). Prioritaire pour l'affichage.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IconProfilePath { get; set; }

    /// <summary>Masqué = absent de la popup mais conservé dans la config.</summary>
    public bool Hidden { get; set; }

    public int Order { get; set; }

    /// <summary>Identifiant stable (8 hex) référencé par les règles de domaine.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N")[..8];
}
