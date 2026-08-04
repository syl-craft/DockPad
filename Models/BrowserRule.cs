namespace DockPad.Models;

/// <summary>Règle « toujours ouvrir ce domaine avec ce navigateur » (host exact ou sous-domaine).</summary>
public class BrowserRule
{
    public string Host { get; set; } = "";
    public string BrowserId { get; set; } = "";
}
