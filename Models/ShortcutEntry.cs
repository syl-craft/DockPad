using System.Text.Json.Serialization;

namespace WinContextMenuManager.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShortcutType
{
    RunCommand,    // Lancer une commande (exe, script...)
    OpenFolder,    // Ouvrir un dossier dans l'Explorateur
    OpenUrl,       // Ouvrir une URL dans le navigateur par défaut
    OpenTerminal,  // Ouvrir un terminal dans un dossier (wt → pwsh → cmd)
}

public class ShortcutEntry
{
    public int Row { get; set; }
    public int Col { get; set; }
    public string Name { get; set; } = "";
    public ShortcutType Type { get; set; } = ShortcutType.RunCommand;
    public string Command { get; set; } = "";
    public string IconPath { get; set; } = "";
}
