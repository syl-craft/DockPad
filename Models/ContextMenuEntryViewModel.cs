using System;
using System.IO;
using System.Windows.Media.Imaging;

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
        IconBitmap = LoadIcon(entry.IconPath);
    }

    private static BitmapSource? LoadIcon(string iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath)) return null;

        try
        {
            // Support "path,index" format (used by Windows for exe/dll icons)
            string[] parts = iconPath.Split(',');
            string path = parts[0].Trim().Trim('"');

            if (!File.Exists(path)) return null;

            string ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext == ".ico")
            {
                return new BitmapImage(new Uri(path));
            }
            else if (ext is ".exe" or ".dll")
            {
                // Extract icon using System.Drawing
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon == null) return null;
                using var bitmap = icon.ToBitmap();
                return ConvertBitmap(bitmap);
            }
        }
        catch { }

        return null;
    }

    private static BitmapSource ConvertBitmap(System.Drawing.Bitmap bitmap)
    {
        var handle = bitmap.GetHbitmap();
        try
        {
            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                handle, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            DeleteObject(handle);
        }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public ContextMenuEntry ToModel() => new()
    {
        RegistryKey = RegistryKey,
        DisplayName = DisplayName,
        Command = Command,
        IconPath = IconPath,
        Target = Target
    };
}
