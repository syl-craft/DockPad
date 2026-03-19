namespace DockPad.Models;

public enum ContextMenuTarget
{
    Files,          // HKCR\*\shell
    Folders,        // HKCR\Directory\shell
    FolderBackground, // HKCR\Directory\Background\shell
    Drives          // HKCR\Drive\shell
}

public class ContextMenuEntry
{
    public string RegistryKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Command { get; set; } = "";
    public string IconPath { get; set; } = "";
    public ContextMenuTarget Target { get; set; } = ContextMenuTarget.Files;

    public string TargetLabel => Target switch
    {
        ContextMenuTarget.Files => "Fichiers",
        ContextMenuTarget.Folders => "Dossiers",
        ContextMenuTarget.FolderBackground => "Fond de dossier",
        ContextMenuTarget.Drives => "Lecteurs",
        _ => ""
    };

    public static string GetRegistryPath(ContextMenuTarget target) => target switch
    {
        ContextMenuTarget.Files => @"*\shell",
        ContextMenuTarget.Folders => @"Directory\shell",
        ContextMenuTarget.FolderBackground => @"Directory\Background\shell",
        ContextMenuTarget.Drives => @"Drive\shell",
        _ => @"*\shell"
    };
}
