using System.IO;
using System.Text.Json;
using DockPad.Models;
using Microsoft.Win32;

namespace DockPad.Services;

/// <summary>
/// Load/Save de <c>settings.json</c> (<c>%APPDATA%\DockPad\settings.json</c>), avec reprise
/// automatique des réglages restés dans le registre.
/// </summary>
/// <remarks>
/// <para>
/// Les options vivaient dans <c>HKCU\Software\DockPad\Settings</c>. Elles rejoignent les autres
/// configs du profil, dans un fichier qu'on peut lire, sauvegarder et versionner — et qui part
/// avec <c>DOCKPAD_PROFILE_DIR</c> quand on veut un profil portable, ce que le registre ne
/// permettait pas.
/// </para>
/// <para>
/// <b>La reprise ne détruit rien.</b> Le registre n'est pas effacé : si l'utilisateur revient à
/// une version antérieure, il retrouve ses réglages. Le fichier fait autorité dès qu'il existe.
/// </para>
/// <para>
/// <b>Le lecteur de registre est un paramètre</b> des méthodes testables : la reprise se vérifie
/// alors sans toucher aux réglages de la machine, et sans dépendre de ce qu'elle contient.
/// </para>
/// </remarks>
public static class AppSettingsService
{
    private const string RegPath = @"Software\DockPad\Settings";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static string FilePath => AppPaths.File("settings.json");

    /// <summary>
    /// Les options en mémoire.
    /// </summary>
    /// <remarks>
    /// Gardées en cache : <c>LoadLanguage</c> et consorts sont appelés à chaque construction de
    /// fenêtre et à chaque changement de langue, et relire le disque à chaque fois n'apporterait
    /// rien — ce fichier n'est écrit que par cette application.
    /// </remarks>
    private static AppSettings? _cached;

    public static AppSettings Current
    {
        get
        {
            lock (ConfigLock.Gate)
                return _cached ??= LoadFrom(FilePath, ReadRegistry);
        }
    }

    /// <summary>Écrit les options et met le cache à jour.</summary>
    public static void Save(AppSettings settings)
    {
        lock (ConfigLock.Gate)
        {
            SaveTo(FilePath, settings);
            _cached = settings;
        }
    }

    /// <summary>Modifie une option et enregistre — la forme utilisée par les appelants.</summary>
    public static void Update(Action<AppSettings> change)
    {
        lock (ConfigLock.Gate)
        {
            var settings = _cached ??= LoadFrom(FilePath, ReadRegistry);
            change(settings);
            SaveTo(FilePath, settings);
        }
    }

    /// <summary>Oublie le cache — pour les tests, et après une restauration de sauvegarde.</summary>
    public static void Invalidate()
    {
        lock (ConfigLock.Gate)
            _cached = null;
    }

    // ───────────── Cœurs testables ─────────────

    /// <summary>
    /// Lit les options du fichier ; à défaut, les reprend du registre et écrit le fichier.
    /// </summary>
    /// <param name="registry">
    /// Lecteur de valeur de registre par nom, rendant <c>null</c> pour une valeur absente.
    /// </param>
    public static AppSettings LoadFrom(string path, Func<string, object?> registry)
    {
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                if (JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) is { } settings)
                    return settings;
            }
            catch (Exception ex)
            {
                // Fichier abîmé : on repart des défauts plutôt que d'empêcher le démarrage, comme
                // le font déjà les autres configs du profil.
                LogService.Warn(ex, $"Lecture de {Path.GetFileName(path)}");
            }
            return new AppSettings();
        }

        var migrated = FromRegistry(registry);
        try { SaveTo(path, migrated); }
        catch (Exception ex) { LogService.Warn(ex, "Reprise des options du registre"); }
        return migrated;
    }

    /// <summary>Options reconstituées depuis le registre. Décision pure, sans IO ni WPF.</summary>
    public static AppSettings FromRegistry(Func<string, object?> registry)
    {
        var settings = new AppSettings();

        if (registry("Language") is string language) settings.Language = language;
        if (registry("Theme") is string theme) settings.Theme = theme;
        if (registry("TriggerFirst") is string first) settings.TriggerFirst = first;
        if (registry("TriggerSecond") is string second) settings.TriggerSecond = second;
        if (registry("ClaudeArgs") is string args) settings.ClaudeArgs = args;
        // Le seul booléen : absent = activé, comme la lecture qu'il remplace.
        if (registry("AutoFavicon") is int favicon) settings.AutoFavicon = favicon != 0;
        if (registry("HotkeyModifiers") is int modifiers) settings.HotkeyModifiers = modifiers;
        if (registry("HotkeyKey") is int key) settings.HotkeyKey = key;

        return settings;
    }

    public static void SaveTo(string path, AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOpts));
    }

    // ───────────── Registre réel ─────────────

    private static object? ReadRegistry(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegPath);
            return key?.GetValue(name);
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, $"Lecture de HKCU\\{RegPath}\\{name}");
            return null;
        }
    }
}
