using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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

    /// <summary>
    /// Réglage stocké, vide pour « suivre Windows ».
    /// </summary>
    /// <remarks>
    /// Dans <c>settings.json</c> avec les autres options. Le thème de <b>Windows</b>, lui, se lit
    /// bien dans le registre — c'est Windows qui l'y écrit, voir <see cref="SystemIsDark"/>.
    /// </remarks>
    public static string LoadSetting() => AppSettingsService.Current.Theme;

    public static void SaveSetting(string setting) =>
        AppSettingsService.Update(s => s.Theme = setting);


    /// <summary>
    /// Le reglage laisse-t-il Windows decider ?
    /// </summary>
    /// <remarks>
    /// Decision pure, extraite pour la meme raison que <see cref="IsDark"/> : un choix explicite
    /// doit rester insensible a un changement de theme de Windows, et c'est le genre de regle qui
    /// se verifie sans monter WPF.
    /// </remarks>
    public static bool FollowsSystem(string? setting) =>
        IsDark(setting, systemIsDark: true) != IsDark(setting, systemIsDark: false);

    /// <summary>
    /// Ecoute les changements de theme de Windows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sans cela, « Automatique » ne se decidait qu'au demarrage : basculer Windows en sombre
    /// laissait DockPad en clair jusqu'au prochain lancement.
    /// </para>
    /// <para>
    /// <b>L'evenement arrive sur un thread a lui</b>, pas sur celui de l'interface : appliquer le
    /// theme directement leverait une exception d'affinite de thread. D'ou le passage par le
    /// Dispatcher.
    /// </para>
    /// </remarks>
    public static void StartFollowingSystem()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public static void StopFollowingSystem()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // La categorie General couvre le passage clair/sombre de Windows.
        if (e.Category != UserPreferenceCategory.General) return;
        if (!FollowsSystem(LoadSetting())) return;

        var dark = SystemIsDark();
        if (dark == CurrentIsDark) return;

        Application.Current?.Dispatcher.Invoke(() => Apply(dark));
    }

    // ───────────── Barre de titre ─────────────

    private const int DwmUseImmersiveDarkMode = 20;

    /// <summary>Ancienne valeur de l'attribut, sur les Windows 10 anterieurs a 20H1.</summary>
    private const int DwmUseImmersiveDarkModeLegacy = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Met la barre de titre d'une fenetre au theme courant.
    /// </summary>
    /// <remarks>
    /// WPF ne peint pas la barre de titre : elle appartient au gestionnaire de fenetres, et reste
    /// donc claire meme quand tout le contenu est sombre. Les deux numeros d'attribut sont essayes
    /// parce que Windows 10 a change le sien en 20H1 ; un appel qui echoue rend un HRESULT non nul
    /// et ne casse rien. La fenetre principale n'a pas de chrome, mais les dialogues en ont une.
    /// </remarks>
    public static void ApplyTitleBar(Window window)
    {
        if (PresentationSource.FromVisual(window) is not HwndSource source) return;

        int value = CurrentIsDark ? 1 : 0;
        if (DwmSetWindowAttribute(source.Handle, DwmUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
            DwmSetWindowAttribute(source.Handle, DwmUseImmersiveDarkModeLegacy, ref value, sizeof(int));
    }

    /// <summary>Met a jour la barre de titre de toutes les fenetres ouvertes.</summary>
    private static void ApplyTitleBarToAll()
    {
        if (Application.Current is not { } app) return;

        foreach (Window window in app.Windows)
            ApplyTitleBar(window);
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
            $"pack://application:,,,/DockPad.Core;component/Themes/{(dark ? "Dark" : "Light")}.xaml",
            UriKind.Absolute);
        var palette = new ResourceDictionary { Source = source };

        // Vérifié : l'affectation par l'indexeur suffit, les fenêtres déjà construites suivent —
        // les références étant en DynamicResource. Mesuré par la cible « dark-switch » de
        // DialogShot, qui bascule APRÈS construction comme le fait l'utilisateur.
        var merged = app.Resources.MergedDictionaries;
        if (merged.Count == 0) merged.Add(palette);
        else merged[0] = palette;

        CurrentIsDark = dark;
        ApplyTitleBarToAll();
        ThemeChanged?.Invoke();
    }
}
