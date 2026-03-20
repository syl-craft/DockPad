namespace DockPad.Models;

using System.Text.Json.Serialization;

public class PageConfig
{
    public int    Index    { get; set; }
    public string IconPath { get; set; } = "";

    /// <summary>Chemin relatif au profil (%APPDATA%\DockPad\). Prioritaire pour l'affichage.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IconProfilePath { get; set; }
}
