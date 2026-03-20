using System.Text.Json.Serialization;

namespace DockPad.Models;


[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShortcutType
{
    RunCommand,      // Lancer une commande (exe, script...)
    OpenFolder,      // Ouvrir un dossier dans l'Explorateur
    OpenUrl,         // Ouvrir une URL dans le navigateur par défaut
    OpenTerminal,    // Ouvrir un terminal dans un dossier (wt → pwsh → cmd)
    SwitchToProcess, // Basculer vers un processus existant ou le lancer
}

public class ShortcutEntry
{
    public int Page { get; set; } = 0;
    public int Row  { get; set; }
    public int Col  { get; set; }
    public string Name { get; set; } = "";
    public ShortcutType Type { get; set; } = ShortcutType.RunCommand;
    public string Command { get; set; } = "";
    public string IconPath { get; set; } = "";

    /// <summary>Chemin dans le dossier de profil (%APPDATA%\DockPad\icons\). Prioritaire pour l'affichage.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IconProfilePath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TerminalConfig? Terminal { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProcessSwitchConfig? ProcessSwitch { get; set; }
}
