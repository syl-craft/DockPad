namespace WinContextMenuManager.Models;

public class TerminalInfo
{
    public string DisplayName { get; set; } = "";
    public string ExePath     { get; set; } = "";
    public bool SupportsNewTab { get; set; }

    public override string ToString() => DisplayName;
}
