namespace DockPad.Models;

public class TerminalInfo
{
    /// <summary>
    /// Identifiant stable enregistré dans <c>settings.json</c> : <c>wt</c>, <c>pwsh</c>, <c>powershell</c> ou <c>custom</c>.
    /// </summary>
    public string Id          { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ExePath     { get; set; } = "";

    /// <summary>
    /// Arguments pour ouvrir un dossier, sans commande à lancer.
    /// </summary>
    public string FolderArgs  { get; set; } = "";

    /// <summary>
    /// Arguments pour ouvrir un dossier et y lancer une commande.
    /// </summary>
    public string CommandArgs { get; set; } = "";

    public override string ToString() => DisplayName;
}
