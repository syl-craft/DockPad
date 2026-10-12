namespace DockPad.Models;

public class TerminalInfo
{
    /// <summary>
    /// Identifiant stable enregistré dans <c>settings.json</c> : <c>wt</c>, <c>pwsh</c> ou <c>powershell</c>.
    /// </summary>
    public string Id          { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ExePath     { get; set; } = "";

    public bool SupportsNewTab => Id == Services.TerminalDetectionService.WindowsTerminalId;

    public override string ToString() => DisplayName;
}
