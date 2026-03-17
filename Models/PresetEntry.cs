namespace WinContextMenuManager.Models;

public enum PresetStatus { NotInstalled, UpToDate, UpdateAvailable }

public class PresetEntry
{
    public string DisplayName { get; set; } = "";
    public string RegistryKey { get; set; } = "";
    public string Command { get; set; } = "";
    public string IconPath { get; set; } = "";
    public ContextMenuTarget Target { get; set; }
    public string Description { get; set; } = "";
    public bool IsSelected { get; set; }
    public PresetStatus Status { get; set; } = PresetStatus.NotInstalled;

    public bool IsUpToDate => Status == PresetStatus.UpToDate;
    public bool CanSelect => Status != PresetStatus.UpToDate;
    public double Opacity => Status == PresetStatus.UpToDate ? 0.45 : 1.0;

    public string BadgeText => Status switch
    {
        PresetStatus.UpToDate => "Déjà installé",
        PresetStatus.UpdateAvailable => "Mise à jour disponible",
        _ => ""
    };
    public string BadgeBackground => Status switch
    {
        PresetStatus.UpToDate => "#E8F5E9",
        PresetStatus.UpdateAvailable => "#FFF3E0",
        _ => "Transparent"
    };
    public string BadgeForeground => Status switch
    {
        PresetStatus.UpToDate => "#2E7D32",
        PresetStatus.UpdateAvailable => "#E65100",
        _ => "Transparent"
    };
    public bool ShowBadge => Status != PresetStatus.NotInstalled;
}
