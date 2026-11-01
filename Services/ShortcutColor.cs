using DockPad.Models;

namespace DockPad.Services;

/// <summary>Couleur initiale d'une nouvelle tuile, commune au dialogue, au dépôt et au MCP.</summary>
public static class ShortcutColor
{
    /// <summary>Une valeur explicite gagne, y compris vide pour garder la couleur du type.</summary>
    public static string? ForNewShortcut(ShortcutType type, string command, string? color = null)
    {
        if (color != null) return color.Length == 0 ? null : color.ToUpperInvariant();
        if (type != ShortcutType.OpenFolder || string.IsNullOrWhiteSpace(command)) return null;

        command = command.Trim();
        if (command.Length >= 2 && command.StartsWith('"') && command.EndsWith('"'))
            command = command[1..^1];

        return TerminalDetectionService.TabColor(TerminalDetectionService.FolderName(command));
    }
}
