namespace WinContextMenuManager.Models;

public class ShortcutEntry
{
    public int Row { get; set; }
    public int Col { get; set; }
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string IconPath { get; set; } = "";
}
