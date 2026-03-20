using System.IO;
using System.Security.Cryptography;
using DockPad.Models;

namespace DockPad.Services;

public static class IconCacheService
{
    /// <summary>Racine du profil DockPad : %APPDATA%\DockPad\</summary>
    public static readonly string ProfileRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DockPad");

    public static readonly string IconsFolder = Path.Combine(ProfileRoot, "icons");

    /// <summary>Convertit un chemin relatif au profil en chemin absolu. Retourne null si vide.</summary>
    public static string? ResolveProfilePath(string? relativePath) =>
        string.IsNullOrWhiteSpace(relativePath)
            ? null
            : Path.Combine(ProfileRoot, relativePath);

    /// <summary>
    /// Copie l'icône source dans %APPDATA%\DockPad\icons\ avec déduplication SHA1.
    /// Pour .exe/.dll, l'icône est extraite et sauvegardée en .png.
    /// Retourne le chemin de destination, ou null si échec.
    /// </summary>
    public static string? CopyToProfile(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;
        string path = sourcePath.Split(',')[0].Trim('"').Trim();
        if (!File.Exists(path)) return null;

        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".exe" or ".dll")
                return ExtractAndCache(path);

            byte[] data = File.ReadAllBytes(path);
            var (abs, rel) = DestPaths(data, ext);
            if (!File.Exists(abs))
                File.WriteAllBytes(abs, data);
            return rel;
        }
        catch { return null; }
    }

    /// <summary>
    /// Synchronise les entrées dont IconProfilePath est absent.
    /// Retourne true si au moins une entrée a été mise à jour.
    /// </summary>
    public static bool SyncAll(List<ShortcutEntry> entries)
    {
        bool changed = false;
        foreach (var entry in entries)
        {
            // Copier vers le profil si iconProfilePath absent
            if (!string.IsNullOrEmpty(entry.IconPath) && string.IsNullOrEmpty(entry.IconProfilePath))
            {
                var dest = CopyToProfile(entry.IconPath);
                if (dest != null)
                {
                    entry.IconProfilePath = dest;
                    changed = true;
                }
            }
            // Effacer iconPath si le fichier source n'existe plus
            if (!string.IsNullOrEmpty(entry.IconPath) && !File.Exists(entry.IconPath))
            {
                entry.IconPath = "";
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>Synchronise les pages : copie les icônes manquantes, efface iconPath si fichier absent.</summary>
    public static bool SyncAllPages(List<PageConfig> pages)
    {
        bool changed = false;
        foreach (var page in pages)
        {
            if (!string.IsNullOrEmpty(page.IconPath) && string.IsNullOrEmpty(page.IconProfilePath))
            {
                var dest = CopyToProfile(page.IconPath);
                if (dest != null) { page.IconProfilePath = dest; changed = true; }
            }
            if (!string.IsNullOrEmpty(page.IconPath) && !File.Exists(page.IconPath))
            {
                page.IconPath = "";
                changed = true;
            }
        }
        return changed;
    }

    private static string? ExtractAndCache(string exePath)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon == null) return null;
            using var bmp = icon.ToBitmap();
            using var ms = new System.IO.MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            byte[] data = ms.ToArray();
            var (abs, rel) = DestPaths(data, ".png");
            if (!File.Exists(abs))
                File.WriteAllBytes(abs, data);
            return rel;
        }
        catch { return null; }
    }

    /// <summary>Retourne (chemin absolu, chemin relatif au profil) pour un fichier icône.</summary>
    private static (string Abs, string Rel) DestPaths(byte[] data, string ext)
    {
        byte[] hash = SHA1.HashData(data);
        string sha1 = Convert.ToHexString(hash).ToLowerInvariant();
        Directory.CreateDirectory(IconsFolder);
        string rel = Path.Combine("icons", sha1 + ext);
        return (Path.Combine(ProfileRoot, rel), rel);
    }
}
