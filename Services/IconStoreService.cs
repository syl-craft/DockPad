using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media.Imaging;
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
    /// <summary>Racine du profil DockPad : %APPDATA%\DockPad\ (voir <see cref="AppPaths"/>)</summary>
    public static readonly string ProfileRoot = AppPaths.ProfileRoot;

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
    /// <summary>
    /// Image affichable d'une référence d'icône — <c>chemin[,index]</c> — ou <c>null</c> si elle est
    /// absente, illisible ou d'un format non géré.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Porte unique.</b> Cinq copies de cette fonction vivaient dans les vues, chacune avec sa
    /// propre déclaration P/Invoke de <c>DeleteObject</c> : corriger un défaut de chargement
    /// demandait cinq corrections, et la copie suivante repartait avec.
    /// </para>
    /// <para>
    /// <b><c>OnLoad</c> et non le défaut.</b> <c>new BitmapImage(uri)</c> laisse
    /// <c>BitmapCacheOption.OnDemand</c>, qui garde le fichier ouvert tant que l'image vit. Le store
    /// réécrit et resynchronise des fichiers : le verrou finit par produire un « fichier utilisé par
    /// un autre processus » chez l'utilisateur, jamais chez le développeur. <c>OnLoad</c> lit tout
    /// puis referme.
    /// </para>
    /// <para>
    /// <b>Gelée.</b> Une image <c>Freeze()</c> coûte moins cher et traverse les threads — sans quoi
    /// une icône chargée depuis un <c>Task.Run</c> lèverait à l'affichage.
    /// </para>
    /// <para>
    /// <b>L'index est respecté</b>, contrairement aux copies qu'elle remplace : elles coupaient sur
    /// la virgule puis appelaient <c>ExtractAssociatedIcon</c>, qui rend toujours l'icône 0. Chrome
    /// Canary (<c>chrome.exe,4</c>) s'affichait donc avec l'icône bleue de Chrome.
    /// </para>
    /// </remarks>
    public static BitmapSource? LoadImage(string? iconRef)
    {
        if (string.IsNullOrWhiteSpace(iconRef)) return null;

        var (path, index) = ParseIconRef(iconRef);
        if (!File.Exists(path)) return null;

        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext is ".exe" or ".dll")
            {
                using var icon = ExtractIcon(path, index);
                if (icon is null) return null;
                using var bmp = icon.ToBitmap();

                var handle = bmp.GetHbitmap();
                try
                {
                    var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
                finally { DeleteObject(handle); }
            }

            if (ext is not (".ico" or ".png" or ".bmp" or ".jpg" or ".jpeg")) return null;

            // Les octets sont lus par nous, pas par WPF : File.ReadAllBytes referme aussitot, et
            // un fichier illisible ne laisse donc rien d'ouvert. Avec UriSource, EndInit() qui leve
            // sur une image corrompue abandonne l'objet AVEC son flux ouvert — le fichier reste
            // verrouille jusqu'au finaliseur, ce qu'un test de suppression attrape immediatement.
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;   // decode tout de suite : le flux peut partir
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, $"Chargement de l'icône : {iconRef}");
            return null;
        }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

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
