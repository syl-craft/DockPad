using Microsoft.Win32;

namespace DockPad.Services;

/// <summary>
/// Réglages de l'application.
/// </summary>
/// <remarks>
/// <para>
/// Tout vit désormais dans <c>settings.json</c> (<see cref="AppSettingsService"/>) : ce service en
/// est la façade, conservée telle quelle pour que la vingtaine d'appelants n'ait pas à changer.
/// </para>
/// <para>
/// <b>Une exception, et une seule</b> : le démarrage automatique. C'est une entrée de la clé
/// <c>Run</c> de Windows, lue par le système et non par DockPad — elle n'a pas d'équivalent en
/// fichier, et reste donc dans le registre.
/// </para>
/// </remarks>
public static class SettingsService
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "DockPad";

    /// <summary>
    /// Raccourci global. Aucun réglage enregistré → le défaut, écrit au passage pour que la
    /// fenêtre affiche la même chose au prochain démarrage.
    /// </summary>
    public static (uint Modifiers, uint Key) LoadHotkey()
    {
        var settings = AppSettingsService.Current;
        if (settings.HotkeyModifiers != 0 && settings.HotkeyKey != 0)
            return ((uint)settings.HotkeyModifiers, (uint)settings.HotkeyKey);

        var (modifiers, key) = DefaultHotkey();
        SaveHotkey(modifiers, key);
        return (modifiers, key);
    }

    public static void SaveHotkey(uint modifiers, uint key) =>
        AppSettingsService.Update(s =>
        {
            s.HotkeyModifiers = (int)modifiers;
            s.HotkeyKey = (int)key;
        });

    // ───────────── Démarrage automatique : reste dans le registre ─────────────

    public static bool LoadAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath);
        return key?.GetValue(AppName) is not null;
    }

    public static void SaveAutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath, writable: true);
        if (key is null) return;

        if (enabled)
        {
            var exePath = Environment.ProcessPath
                          ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
            key.SetValue(AppName, $"\"{exePath}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    // ───────────── Le reste : settings.json ─────────────

    /// <summary>
    /// Modificateurs des raccourcis de tuiles (overlay) : côté gauche / côté droit.
    /// <c>""</c> = auto (déduits du raccourci global), sinon <c>Ctrl</c> | <c>Alt</c> | <c>Shift</c>.
    /// </summary>
    public static (string First, string Second) LoadTriggerMods()
    {
        var settings = AppSettingsService.Current;
        return (settings.TriggerFirst, settings.TriggerSecond);
    }

    public static void SaveTriggerMods(string first, string second) =>
        AppSettingsService.Update(s =>
        {
            s.TriggerFirst = first;
            s.TriggerSecond = second;
        });

    /// <summary>
    /// Étiquette de langue choisie, ou chaîne vide pour « automatique ». Même convention que
    /// <c>TriggerFirst</c>/<c>TriggerSecond</c> : le vide veut dire « laisse le système décider ».
    /// </summary>
    public static string LoadLanguage() => AppSettingsService.Current.Language;

    public static void SaveLanguage(string tag) =>
        AppSettingsService.Update(s => s.Language = tag);

    /// <summary>
    /// Télécharger l'icône du site pour les tuiles web. Absent du fichier = activé : un réglage
    /// réseau qu'on n'a jamais vu ne peut pas avoir été refusé.
    /// </summary>
    public static bool LoadAutoFavicon() => AppSettingsService.Current.AutoFavicon;

    public static void SaveAutoFavicon(bool enabled) =>
        AppSettingsService.Update(s => s.AutoFavicon = enabled);

    public static string LoadClaudeArgs() => AppSettingsService.Current.ClaudeArgs;

    public static void SaveClaudeArgs(string args) =>
        AppSettingsService.Update(s => s.ClaudeArgs = args.Trim());

    public static string LoadBitwardenCliPath() => AppSettingsService.Current.BitwardenCliPath;

    public static void SaveBitwardenCliPath(string path) =>
        AppSettingsService.Update(s => s.BitwardenCliPath = path.Trim().Trim('"'));

    public static int LoadClipboardClearSeconds() => AppSettingsService.Current.ClipboardClearSeconds;

    /// <summary>Borné à un jour : au-delà, le réglage ne protège plus rien.</summary>
    public static void SaveClipboardClearSeconds(int seconds) =>
        AppSettingsService.Update(s => s.ClipboardClearSeconds = Math.Clamp(seconds, 0, 86400));

    public static string LoadVaultOrganization() => AppSettingsService.Current.VaultOrganization;

    public static void SaveVaultOrganization(string organisation) =>
        AppSettingsService.Update(s => s.VaultOrganization = organisation.Trim());

    /// <summary>Ctrl+Shift+M.</summary>
    private static (uint, uint) DefaultHotkey() =>
        (HotkeyService.MOD_CONTROL | HotkeyService.MOD_SHIFT, 0x4D);
}
