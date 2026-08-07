namespace DockPad.Models;

/// <summary>Champs modifiables par dockpad_browser_update. null = inchangé.</summary>
public class BrowserUpdate
{
    public string? Name { get; set; }
    public string? ExePath { get; set; }
    public string? Arguments { get; set; }
    public bool? Hidden { get; set; }
    public int? Order { get; set; }
}
