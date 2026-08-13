using System.IO;
using System.Text.Json;
using DockPad.Models;

namespace DockPad.Services;

/// <summary>Un profil trouvé dans le « User Data » d'un navigateur Chromium.</summary>
public sealed record DetectedProfile(string Directory, string Name, string? IconPath);

/// <summary>
/// Détection des profils des navigateurs Chromium (Chrome, Edge, Brave, Vivaldi…) et
/// fusion dans browsers.json. Les profils sont lus dans le fichier « Local State » du
/// dossier User Data ; un profil est une <see cref="BrowserEntry"/> rattachée à son
/// navigateur par <see cref="BrowserEntry.ParentId"/>.
/// </summary>
public static class BrowserProfileService
{
    /// <summary>Dossier vendeur dans %LOCALAPPDATA%, par nom d'exécutable ("" = pas de vendeur).</summary>
    private static readonly Dictionary<string, string> Vendors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome.exe"]   = "Google",
        ["msedge.exe"]   = "Microsoft",
        ["brave.exe"]    = "BraveSoftware",
        ["vivaldi.exe"]  = "",
        ["chromium.exe"] = "",
    };

    private static readonly string[] IconNames =
        ["Google Profile Picture.png", "Edge Profile Picture.png", "Profile Picture.png"];

    /// <summary>Profils du navigateur installé à <paramref name="exePath"/>, liste vide si inconnu.</summary>
    public static List<DetectedProfile> Detect(string exePath) =>
        Detect(exePath, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <inheritdoc cref="Detect(string)"/>
    public static List<DetectedProfile> Detect(string exePath, string localAppData)
    {
        var userDataDir = ResolveUserDataDir(exePath, localAppData);
        if (userDataDir is null) return [];

        var localState = Path.Combine(userDataDir, "Local State");
        if (!File.Exists(localState)) return [];

        string json;
        try
        {
            // Le navigateur garde « Local State » ouvert : lecture en partage.
            using var stream = new FileStream(localState, FileMode.Open, FileAccess.Read,
                                             FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            json = reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, $"Lecture des profils du navigateur ({localState})");
            return [];
        }

        return ParseProfiles(json)
            .Select(p => p with { IconPath = FindProfileIcon(userDataDir, p.Directory) })
            .ToList();
    }

    /// <summary>
    /// Dossier User Data d'un navigateur Chromium, déduit du chemin de l'exe :
    /// &lt;exe&gt; doit être dans un dossier « Application », et le dossier au-dessus donne
    /// la variante (Chrome, Chrome SxS, Edge Dev…) sous %LOCALAPPDATA%\&lt;vendeur&gt;.
    /// Null si l'exécutable n'est pas un navigateur Chromium connu.
    /// </summary>
    public static string? ResolveUserDataDir(string exePath, string localAppData)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;
        if (!Vendors.TryGetValue(Path.GetFileName(exePath), out var vendor)) return null;

        var appDir = Path.GetDirectoryName(exePath);
        if (appDir is null || !Path.GetFileName(appDir).Equals("Application", StringComparison.OrdinalIgnoreCase))
            return null;

        var variant = Path.GetFileName(Path.GetDirectoryName(appDir) ?? "");
        if (variant.Length == 0) return null;

        return Path.Combine(localAppData, vendor, variant, "User Data");
    }

    /// <summary>
    /// Profils déclarés dans « Local State » (profile.info_cache) : dossier + nom d'affichage,
    /// « Default » d'abord puis par numéro croissant. Liste vide si le JSON est illisible.
    /// </summary>
    public static List<DetectedProfile> ParseProfiles(string localStateJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(localStateJson);
            if (!doc.RootElement.TryGetProperty("profile", out var profile) ||
                !profile.TryGetProperty("info_cache", out var cache) ||
                cache.ValueKind != JsonValueKind.Object)
                return [];

            return cache.EnumerateObject()
                .Select(p => new DetectedProfile(
                    p.Name,
                    p.Value.ValueKind == JsonValueKind.Object &&
                    p.Value.TryGetProperty("name", out var n) &&
                    n.GetString() is { Length: > 0 } name ? name : p.Name,
                    null))
                .OrderBy(p => SortKey(p.Directory))
                .ToList();
        }
        catch (JsonException ex)
        {
            LogService.Warn(ex, "Lecture de « Local State » (profils de navigateur)");
            return [];
        }
    }

    /// <summary>« Default » d'abord, puis « Profile N » par numéro croissant, puis alphabétique.</summary>
    private static (int Rank, int Number, string Name) SortKey(string directory)
    {
        if (directory.Equals("Default", StringComparison.OrdinalIgnoreCase)) return (0, 0, "");

        var digits = new string(directory.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        return (1, int.TryParse(digits, out int n) ? n : int.MaxValue, directory);
    }

    /// <summary>Image du profil (compte connecté) dans son dossier, ou null s'il n'y en a pas.</summary>
    public static string? FindProfileIcon(string userDataDir, string profileDirectory) =>
        IconNames.Select(n => Path.Combine(userDataDir, profileDirectory, n)).FirstOrDefault(File.Exists);

    /// <summary>
    /// Fusionne les profils détectés dans la config et retourne les entrées créées.
    /// Rien n'est ajouté sous un navigateur qui n'a qu'un seul profil : son comportement
    /// par défaut suffit. Un profil déjà connu garde son id, son masquage, son ordre et son
    /// nom s'il a été personnalisé ; sinon le nom suit celui du navigateur. Un profil disparu
    /// n'est pas supprimé (ça détruirait ses règles de domaine).
    /// </summary>
    public static List<BrowserEntry> MergeProfiles(BrowsersConfig cfg, BrowserEntry parent,
                                                   IReadOnlyList<DetectedProfile> profiles)
    {
        var added = new List<BrowserEntry>();
        if (profiles.Count < 2) return added;

        int order = parent.Order;

        foreach (var p in profiles)
        {
            var existing = cfg.Browsers.FirstOrDefault(
                b => b.ParentId == parent.Id &&
                     string.Equals(b.ProfileDirectory, p.Directory, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                if (string.IsNullOrEmpty(existing.DetectedName) || existing.Name == existing.DetectedName)
                    existing.Name = p.Name;
                existing.DetectedName = p.Name;
                existing.ExePath = parent.ExePath;
                continue;
            }

            var child = new BrowserEntry
            {
                Name             = p.Name,
                DetectedName     = p.Name,
                ExePath          = parent.ExePath,
                ParentId         = parent.Id,
                ProfileDirectory = p.Directory,
                IconPath         = p.IconPath ?? parent.IconPath,
                Order            = ++order,
            };
            cfg.Browsers.Add(child);
            added.Add(child);
        }

        if (added.Count > 0) BrowserRowLayout.Reindex(cfg);
        return added;
    }
}
