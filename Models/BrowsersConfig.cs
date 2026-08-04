namespace DockPad.Models;

/// <summary>Contenu de %APPDATA%\DockPad\browsers.json.</summary>
public class BrowsersConfig
{
    public List<BrowserEntry> Browsers { get; set; } = [];
    public List<BrowserRule> Rules { get; set; } = [];
}
