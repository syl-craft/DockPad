namespace DockPad.Models;

/// <summary>Champs modifiables par dockpad_shortcut_update. null = inchangé.</summary>
public class ShortcutUpdate
{
    public string? Name { get; set; }
    public ShortcutType? Type { get; set; }
    public string? Command { get; set; }
    public string? IconPath { get; set; }
    public TerminalConfig? Terminal { get; set; }
    public ProcessSwitchConfig? ProcessSwitch { get; set; }
}
