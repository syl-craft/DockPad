using System.IO;
using System.Security.Cryptography;
using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// Store des icônes du profil (%APPDATA%\DockPad\icons\) : copie de référence des icônes
/// des tuiles/pages/navigateurs, dédupliquée par SHA1. Ce n'est pas un cache — rien n'expire,
/// le store est la source d'affichage (IconProfilePath), le chemin d'origine (IconPath)
/// n'étant gardé qu'à titre de provenance.
/// </summary>
public static class IconStoreService
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
        var (path, iconIndex) = ParseIconRef(sourcePath);
        if (!File.Exists(path)) return null;

        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".exe" or ".dll")
                return ExtractAndStore(path, iconIndex);

            byte[] data = File.ReadAllBytes(path);
            var (abs, rel) = DestPaths(data, ext);
            if (!File.Exists(abs))
                File.WriteAllBytes(abs, data);
            return rel;
        }
        catch (Exception ex) { LogService.Warn(ex, $"Copie d'une icône dans le store : {path}"); return null; }
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

    /// <summary>
    /// Découpe une référence d'icône au format registre "chemin[,index]" (ex: valeur DefaultIcon).
    /// Index positif = position dans le fichier, négatif = ID de ressource, absent = 0.
    /// </summary>
    public static (string Path, int Index) ParseIconRef(string iconRef)
    {
        iconRef = iconRef.Trim();
        int comma = iconRef.LastIndexOf(',');
        if (comma > 0 && int.TryParse(iconRef[(comma + 1)..].Trim(), out int index))
            return (iconRef[..comma].Trim().Trim('"'), index);
        return (iconRef.Trim('"'), 0);
    }

    private static string? ExtractAndStore(string exePath, int iconIndex = 0)
    {
        try
        {
            using var icon = ExtractIcon(exePath, iconIndex);
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
        catch (Exception ex) { LogService.Warn(ex, $"Extraction de l'icône d'un exécutable : {exePath}"); return null; }
    }

    /// <summary>
    /// Extrait l'icône d'un exe/dll. Index non nul → Icon.ExtractIcon qui respecte
    /// l'index DefaultIcon du registre (ex: Chrome Canary = chrome.exe,4 pour l'icône jaune) ;
    /// sinon icône associée classique.
    /// </summary>
    private static System.Drawing.Icon? ExtractIcon(string exePath, int iconIndex)
    {
        if (iconIndex != 0)
        {
            try
            {
                var icon = System.Drawing.Icon.ExtractIcon(exePath, iconIndex, 64);
                if (icon != null) return icon;
            }
            catch (Exception ex) { LogService.Warn(ex, $"Index d'icône invalide ({iconIndex}) pour {exePath}, icône associée utilisée"); }
        }
        return System.Drawing.Icon.ExtractAssociatedIcon(exePath);
    }

    /// <summary>
    /// Enregistre des bytes d'icône dans le store (déduplication SHA1).
    /// Retourne le chemin relatif au profil, ou null si échec.
    /// </summary>
    public static string? StoreBytes(byte[] data, string ext)
    {
        try
        {
            var (abs, rel) = DestPaths(data, ext);
            if (!File.Exists(abs))
                File.WriteAllBytes(abs, data);
            return rel;
        }
        catch (Exception ex) { LogService.Warn(ex, "Écriture d'une icône dans le store"); return null; }
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
