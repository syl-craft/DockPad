namespace DockPad.Models;

/// <summary>Contenu de %APPDATA%\DockPad\browsers.json.</summary>
public class BrowsersConfig
{
    public List<BrowserEntry> Browsers { get; set; } = [];
    public List<BrowserRule> Rules { get; set; } = [];

    /// <summary>Délai en secondes avant ouverture automatique avec le navigateur n°1 (0 = désactivé).</summary>
    public int AutoOpenSeconds { get; set; }
}
