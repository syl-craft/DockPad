namespace DockPad.Models;

/// <summary>Contenu de %APPDATA%\DockPad\mcp.json.</summary>
public class McpConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>Autorise dockpad_shortcut_delete / dockpad_page_delete / dockpad_rule_delete.</summary>
    public bool AllowDelete { get; set; } = false;
}
