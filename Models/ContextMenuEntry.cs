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

    /// <summary>
    /// Libellé de la cible, traduit. Il est lu à chaque accès et non mis en cache : la propriété est
    /// consultée à la construction des lignes de la liste, donc dans la langue du moment.
    /// </summary>
    public string TargetLabel => Target switch
    {
        ContextMenuTarget.Files => Loc.T("CtxMenu_Filter_Files"),
        ContextMenuTarget.Folders => Loc.T("CtxMenu_Filter_Folders"),
        ContextMenuTarget.FolderBackground => Loc.T("CtxMenu_Filter_Background"),
        ContextMenuTarget.Drives => Loc.T("CtxMenu_Target_Drives"),
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
