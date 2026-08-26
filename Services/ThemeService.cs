using System.Windows;
using Microsoft.Win32;

namespace DockPad.Services;

/// <summary>
/// Applique le thème clair ou sombre, et le fait basculer à chaud.
/// </summary>
/// <remarks>
/// <para>
/// La palette vit dans deux dictionnaires interchangeables (<c>Themes/Light.xaml</c> et
/// <c>Themes/Dark.xaml</c>) qui portent exactement les mêmes clés. Basculer, c'est remplacer le
/// dictionnaire fusionné en position 0 — rien d'autre. C'est pour cela que les fenêtres
/// référencent la palette en <c>DynamicResource</c> : <c>StaticResource</c> fige la valeur au
/// chargement et ne verrait jamais le remplacement.
/// </para>
/// <para>
/// <b>La décision est séparée de l'application</b> : <see cref="IsDark(string, bool)"/> est une
/// fonction pure, testée sans WPF, comme la résolution des modificateurs de l'overlay.
/// </para>
/// </remarks>
public static class ThemeService
{
    private const string RegPath = @"Software\DockPad\Settings";

    /// <summary>Clé où Windows range son propre choix pour les applications.</summary>
    private const string SystemThemeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Levé après un changement de thème, pour ce qui est construit en code.</summary>
    public static event Action? ThemeChanged;

    /// <summary>Le thème appliqué en ce moment.</summary>
    public static bool CurrentIsDark { get; private set; }

    /// <summary>
    /// Faut-il le thème sombre ?
    /// </summary>
    /// <param name="setting">
    /// Réglage stocké : <c>"Light"</c>, <c>"Dark"</c>, ou vide pour « suivre Windows ». Même
    /// convention que <c>Language</c> et <c>TriggerFirst</c> — le vide veut dire « laisse le
    /// système décider ». Toute autre valeur est traitée comme le vide : un réglage écrit par une
    /// version plus récente, puis revenue en arrière, ne doit ni planter ni figer un thème.
    /// </param>
    public static bool IsDark(string? setting, bool systemIsDark) => setting?.Trim().ToLowerInvariant() switch
    {
        "dark" => true,
        "light" => false,
        _ => systemIsDark,
    };

    /// <summary>
    /// Windows est-il en thème sombre pour les applications ?
    /// </summary>
    /// <remarks>
    /// <c>AppsUseLightTheme</c> vaut 0 en sombre. Absente — Windows antérieur, stratégie
    /// d'entreprise — on répond clair : c'était le seul thème de DockPad jusqu'ici, c'est le repli
    /// qui ne surprend personne.
    /// </remarks>
    public static bool SystemIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SystemThemeKey);
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, "Lecture du thème de Windows");
            return false;
        }
    }

    /// <summary>Réglage stocké, vide pour « suivre Windows ».</summary>
    public static string LoadSetting()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegPath);
        return key?.GetValue("Theme") as string ?? "";
    }

    public static void SaveSetting(string setting)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegPath);
        key.SetValue("Theme", setting, RegistryValueKind.String);
    }

    /// <summary>Applique le thème que dit le réglage courant.</summary>
    public static void ApplyFromSettings() => Apply(IsDark(LoadSetting(), SystemIsDark()));

    /// <summary>
    /// Remplace la palette par celle du thème demandé.
    /// </summary>
    /// <remarks>
    /// Le dictionnaire de palette est celui de <b>position 0</b> : App.xaml n'en fusionne qu'un, et
    /// c'est le seul que ce service a le droit de toucher. Remplacer l'entrée plutôt que vider la
    /// collection évite de perdre un dictionnaire qu'on ajouterait plus tard.
    /// </remarks>
    public static void Apply(bool dark)
    {
        var app = Application.Current;
        if (app is null) return;

        // Pack URI nommant l'assembly, et non un chemin relatif : celui-ci se résoudrait dans
        // l'assembly HÔTE, qui n'est pas DockPad quand un outil de capture monte l'application.
        // Même piège que app.ico, référencée par pack URI pour la même raison.
        var source = new Uri(
            $"pack://application:,,,/DockPad;component/Themes/{(dark ? "Dark" : "Light")}.xaml",
            UriKind.Absolute);
        var palette = new ResourceDictionary { Source = source };

        var merged = app.Resources.MergedDictionaries;
        if (merged.Count == 0) merged.Add(palette);
        else merged[0] = palette;

        CurrentIsDark = dark;
        ThemeChanged?.Invoke();
    }
}
