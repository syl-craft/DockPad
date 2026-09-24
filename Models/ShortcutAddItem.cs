namespace DockPad.Models;

/// <summary>Item du lot dockpad_shortcut_add. Position optionnelle (sinon première case libre).</summary>
public class ShortcutAddItem
{
    public string Name { get; set; } = "";
    public ShortcutType Type { get; set; } = ShortcutType.RunCommand;
    public string Command { get; set; } = "";
    public int? Page { get; set; }
    public int? Row { get; set; }
    public int? Col { get; set; }
    /// <summary>Sous-case d'une tuile groupée déjà présente en (page, row, col). null = la case entière.</summary>
    public int? Slot { get; set; }
    public string? IconPath { get; set; }
    public TerminalConfig? Terminal { get; set; }
    public ProcessSwitchConfig? ProcessSwitch { get; set; }
}
