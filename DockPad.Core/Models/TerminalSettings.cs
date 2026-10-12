namespace DockPad.Models;

/// <summary>
/// Un terminal tel qu'on le règle : son programme, et ses arguments où <c>{folder}</c>,
/// <c>{command}</c> et <c>{command-ps}</c> sont remplacés au lancement.
/// </summary>
public class TerminalSettings
{
    /// <summary>
    /// Programme d'un terminal ajouté ; les variables d'environnement (<c>%LocalAppData%</c>) sont admises.
    /// </summary>
    public string ExePath { get; set; } = "";

    /// <summary>
    /// Arguments pour ouvrir un dossier, sans commande à lancer.
    /// </summary>
    public string FolderArgs { get; set; } = "";

    /// <summary>
    /// Arguments pour ouvrir un dossier et y lancer une commande.
    /// </summary>
    public string CommandArgs { get; set; } = "";

    public TerminalSettings Copy() => new() { ExePath = ExePath, FolderArgs = FolderArgs, CommandArgs = CommandArgs };
}
