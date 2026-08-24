using System;
using System.IO;
using System.Windows.Media.Imaging;

using DockPad.Services;

namespace DockPad.Models;

public class ContextMenuEntryViewModel
{
    public string RegistryKey { get; set; }
    public string DisplayName { get; set; }
    public string Command { get; set; }
    public string IconPath { get; set; }
    public ContextMenuTarget Target { get; set; }
    public string TargetLabel { get; set; }
    public BitmapSource? IconBitmap { get; set; }

    public ContextMenuEntryViewModel(ContextMenuEntry entry)
    {
        RegistryKey = entry.RegistryKey;
        DisplayName = entry.DisplayName;
        Command = entry.Command;
        IconPath = entry.IconPath;
        Target = entry.Target;
        TargetLabel = entry.TargetLabel;
        IconBitmap = IconStoreService.LoadImage(entry.IconPath);
    }

    public ContextMenuEntry ToModel() => new()
    {
        RegistryKey = RegistryKey,
        DisplayName = DisplayName,
        Command = Command,
        IconPath = IconPath,
        Target = Target
    };
}
