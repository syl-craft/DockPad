using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Application = System.Windows.Application;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using System.Windows.Media.Imaging;
using DockPad;
using DockPad.Services;
using DockPad.Services.Localization;

namespace DialogShot;

/// <summary>
/// Capture une fenêtre de DockPad dans une langue donnée, hors process DockPad, pour vérifier un
/// rendu traduit sans lancer l'application.
/// </summary>
/// <remarks>
/// <para>
/// <c>DialogShot.exe &lt;fenêtre&gt; &lt;langue&gt; &lt;chemin.png&gt;</c> — par exemple
/// <c>DialogShot.exe settings en docs/screenshots/settings-en.png</c>.
/// </para>
/// <para>
/// <b>Rendu hors écran</b> (<c>Measure</c>/<c>Arrange</c> explicites, jamais <c>Show()</c>) : c'est
/// immédiat, ça n'ouvre rien à l'écran, et ça évite la boucle qui affame le dispatcher quand une
/// fenêtre de DockPad est affichée dans un hôte qui n'est pas l'application — voir la remarque de
/// <c>tools/UsageShot</c>.
/// </para>
/// <para>
/// <b>Profil de fixture</b> : <c>DOCKPAD_PROFILE_DIR</c> est posé avant tout accès aux services, donc
/// aucune capture ne lit ni n'écrit le profil réel de l'utilisateur.
/// </para>
/// </remarks>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("usage : DialogShot <settings|ctxmenu|presets|mcp|shortcut|entry|inject|inject-failed|inject-files|bindings|grid> <fr|en> <chemin.png> [onglet]");
            return;
        }

        var target = args[0];
        var lang = args[1];
        var outPath = Path.GetFullPath(args[2]);

        // Avant toute utilisation des services : AppPaths ne lit la variable qu'une fois.
        var fixture = Path.Combine(Path.GetTempPath(), "dockpad-dialogshot");
        Directory.CreateDirectory(fixture);
        Environment.SetEnvironmentVariable(AppPaths.OverrideVariable, fixture);

        var app = new App();
        app.InitializeComponent();
        // L'outil EST l'assembly d'entree, mais il montre les fenetres de DockPad : sans
        // cette pose, le pied de fenetre porterait la version de l'outil (v1.0.0).
        DockPad.Services.AppInfo.Initialize(typeof(App).Assembly);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Theme de capture : une variable d'environnement plutot qu'un argument, pour ne pas
        // deplacer les arguments des cibles existantes — comme DOCKPAD_SHOT_LANG.
        //
        // « dark-switch » reproduit le geste de l'utilisateur : basculer depuis les Options, donc
        // APRES que la fenetre existe. Ce n'est pas le meme chemin que « dark », qui applique le
        // theme avant toute fenetre — et une couleur deja resolue ne suivrait pas.
        var themeEnv = Environment.GetEnvironmentVariable("DOCKPAD_SHOT_THEME") ?? "";
        _switchAfterBuild = string.Equals(themeEnv, "dark-switch", StringComparison.OrdinalIgnoreCase);
        if (!_switchAfterBuild)
            DockPad.Services.ThemeService.Apply(
                string.Equals(themeEnv, "dark", StringComparison.OrdinalIgnoreCase));


        // Loc.Parse rend null sur une etiquette inconnue, ce qui vaudrait « automatique » et
        // capturerait silencieusement la mauvaise langue : ici on prefere le dire.
        var culture = Loc.Parse(lang);
        if (culture is null)
        {
            Console.WriteLine($"langue inconnue : {lang} (fr | en)");
            return;
        }
        Loc.SetCulture(culture);
        // OnStartup n'est jamais appele dans cet hote : c'est lui qui pose normalement la langue a
        // WPF, dont dependent les StringFormat de liaison.
        App.ApplyWpfLanguage();

        // Cible de verification, sans capture : monte les fenetres et ecoute la trace de liaison.
        if (target == "bindings")
        {
            Environment.ExitCode = CheckBindings();
            return;
        }

        // Cout d'un peuplement de la grille : le geste que refait chaque changement de page.
        if (target == "bench")
        {
            GridBench.Run(folders: args.Length < 3 || args[2] != "command");
            return;
        }

        // Overlay clavier deploye : la seule chose que les captures au repos ne montrent pas.
        if (target == "overlay")
        {
            OverlayShot.Render(args[2], firstHalf: true);
            return;
        }

        // Cablage de la grille de tuiles : ce que lisent le clic, le glissement et le depot.
        if (target == "grid")
        {
            Environment.ExitCode = GridCheck.Run();
            return;
        }

        Window window = target switch
        {
            "settings" => new SettingsDialog(),
            "ctxmenu" => new ContextMenuManagerWindow(),
            "presets" => new PresetsDialog(),
            "mcp" => new McpConfigDialog(),
            // Trois fenetres n'avaient aucune couverture de capture : celles d'ajout/modification
            // d'une tuile et d'une entree de registre. Un defaut de couleur y serait passe inapercu.
            "shortcut" => new ShortcutDialog(row: 0, col: 0),
            "entry" => new EntryDialog(),
            // La fenetre d'injection traverse quatre etats, dont deux valent une relecture : la
            // saisie du mot de passe et le compte-rendu d'echec. On les pose par reflexion, comme
            // OverlayShot appelle ShowHintOverlay — sans quoi il faudrait un coffre et un reseau.
            "inject" => InjectionWindow(unlocked: false),
            "inject-failed" => InjectionWindow(unlocked: true),
            "inject-files" => InjectionFilesWindow(),
            "inject-partial" => InjectionPartialWindow(),
            "inject-choice" => InjectionChoiceWindow(),
            _ => throw new ArgumentException($"fenêtre inconnue : {target}"),
        };

        // Onglet a capturer, optionnel et en DERNIERE position : les fenetres a onglets n'en
        // montrent qu'un, et une bascule apres rendu ne se repercute pas. Ajoute a la fin pour ne
        // pas deplacer les arguments des cibles existantes.
        if (window is { } tabbed && args.Length > 3 && int.TryParse(args[3], out var tabIndex))
            SelectTab(tabbed, tabIndex);

        Render(window, outPath, target, lang);
    }

    /// <summary>Selectionne un onglet avant le rendu, s'il y a un TabControl dans la fenetre.</summary>
    private static void SelectTab(Window window, int index)
    {
        if (window.Content is not DependencyObject root) return;
        if (FindTabControl(root) is { } tabs && index >= 0 && index < tabs.Items.Count)
            tabs.SelectedIndex = index;
    }

    private static System.Windows.Controls.TabControl? FindTabControl(DependencyObject root)
    {
        if (root is System.Windows.Controls.TabControl found) return found;

        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            if (FindTabControl(child) is { } nested) return nested;

        return null;
    }

    /// <summary>
    /// La fenetre d'injection, posee sur l'un de ses etats.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Par reflexion, comme <c>OverlayShot</c> : les etats sont poses par des methodes privees, et
    /// les exposer pour l'outil de capture ferait entrer du code de test dans la fenetre.
    /// </para>
    /// <para>
    /// <b>Aucun de ces deux etats ne touche au presse-papier.</b> L'armement appartient au
    /// deroulement, pas a l'affichage — c'est ce qui rend cette capture sans effet de bord sur la
    /// machine qui la produit.
    /// </para>
    /// </remarks>
    private static Window InjectionWindow(bool unlocked)
    {
        var window = new DockPad.Secrets.SecretInjectionWindow(
            Path.Combine(Path.GetTempPath(), "docker-compose.yml"));

        if (unlocked)
        {
            var report = DockPad.Secrets.InjectionReport.Fail(
                Loc.F("Inject_Error_ItemMissingOrg", "ntfy", "Infra maison"), "exit 1 — Not found.");
            Invoke(window, "ShowFailure", report);
        }
        else
        {
            // (object?)null et non null : avec un parametre `params`, un null nu est pris pour le
            // TABLEAU nul — donc zero argument, et la reflexion refuse.
            Invoke(window, "ShowUnlock", (object?)null);
        }

        return window;
    }

    /// <summary>Le compte-rendu du mode fichiers, sans coffre ni ecriture disque.</summary>
    private static Window InjectionFilesWindow()
    {
        var window = new DockPad.Secrets.SecretInjectionWindow(
            Path.Combine(Path.GetTempPath(), "docker-compose.yml"));

        var files = new DockPad.Secrets.SecretFilesOutcome(
            @"C:\Users\moi\infra\vaultwarden\secrets",
            ["ts-authkey", "smtp-password", "push-installation-id", "push-installation-key"], 1, []);

        // Le cas « les deux » COMPLET : c'est celui qui perdait la moitie de son information
        // avant la fusion des panneaux — il montrait les fichiers sans jamais dire que le rendu
        // etait dans le presse-papier.
        var render = DockPad.Secrets.SecretRenderResult.Rendered("services:", 3, 1, []);

        Invoke(window, "ShowResult",
            DockPad.Secrets.InjectionReport.Produced(render, files, []));
        return window;
    }

    /// <summary>
    /// L'ecran de CHOIX : seulement pour un fichier qui porte les deux formats.
    /// </summary>
    /// <remarks>
    /// Il vit dans la fenetre d'injection comme cinquieme etat, et arrive AVANT le mot de passe.
    /// </remarks>
    private static Window InjectionChoiceWindow()
    {
        var window = new DockPad.Secrets.SecretInjectionWindow(
            Path.Combine(Path.GetTempPath(), "docker-compose.yml"));

        Invoke(window, "ShowChoice");
        return window;
    }

    /// <summary>
    /// Le compte-rendu INCOMPLET : produit, mais avec des trous.
    /// </summary>
    /// <remarks>
    /// C'est le seul ecran qui demande une decision — supprimer les fichiers perimes, ou non — et
    /// le seul qui ne se referme pas tout seul. Donc celui qui vaut le plus une relecture.
    /// </remarks>
    private static Window InjectionPartialWindow()
    {
        var window = new DockPad.Secrets.SecretInjectionWindow(
            Path.Combine(Path.GetTempPath(), "docker-compose.yml"));

        var files = new DockPad.Secrets.SecretFilesOutcome(
            @"C:\Users\moi\infra\vaultwarden\secrets",
            ["ts-authkey", "push-installation-id"], 1,
            ["smtp-password", "admin-token"]);

        var render = DockPad.Secrets.SecretRenderResult.Rendered("services:", 3, 1, []);

        var report = DockPad.Secrets.InjectionReport.Produced(render, files,
        [
            "vaultwarden-infra : le champ « smtp-password » est vide dans le coffre.",
            "admin-token : aucun item de ce nom dans le coffre.",
        ]);

        Invoke(window, "ShowResult", report);
        return window;
    }

    /// <remarks>
    /// <c>params</c> : certains etats se posent sans argument (l'ecran de choix), d'autres avec un
    /// seul. Un parametre obligatoire obligerait a inventer une valeur pour les premiers.
    /// </remarks>
    private static void Invoke(Window window, string method, params object?[] arguments) =>
        window.GetType()
            .GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(window, arguments);

    /// <summary>
    /// Monte chaque fenetre et rend le nombre de liaisons cassees.
    /// </summary>
    /// <remarks>
    /// Une liaison qui echoue ne leve pas : elle laisse un controle vide et une ligne dans la trace
    /// de debogage. Un bouton dont la commande ne resout pas reste cliquable et ne fait rien — c'est
    /// invisible a la compilation, et une capture d'ecran ne le montre pas non plus.
    /// </remarks>
    private static int CheckBindings()
    {
        var broken = new List<string>();

        var windows = new (string Name, Func<Window> Build)[]
        {
            ("QuickAccessWindow", () => new DockPad.QuickAccessWindow()),
            ("SettingsDialog", () => new SettingsDialog()),
            ("ContextMenuManagerWindow", () => new ContextMenuManagerWindow()),
            ("PresetsDialog", () => new PresetsDialog()),
            ("McpConfigDialog", () => new McpConfigDialog()),
            ("ShortcutDialog", () => new ShortcutDialog(row: 0, col: 0)),
            ("EntryDialog", () => new EntryDialog()),
            ("UsageConfigDialog", () => new UsageConfigDialog()),
            ("BrowserConfigDialog", () => new BrowserConfigDialog()),
            ("SecretInjectionWindow", () => new DockPad.Secrets.SecretInjectionWindow("x.yml")),
        };

        foreach (var (name, build) in windows)
        {
            try
            {
                var window = build();
                var width = double.IsNaN(window.Width) ? 900 : window.Width;
                BindingCheck.ForceLayout((FrameworkElement)window.Content, width);

                var windowBroken = BindingCheck.BrokenCommands(window);
                broken.AddRange(windowBroken.Select(b => $"{name} : {b}"));
                Console.WriteLine($"  {name} — {(windowBroken.Count == 0 ? "ok" : windowBroken.Count + " cassee(s)")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ECHEC : {name} — {ex.GetType().Name} : {ex.Message}");
                return 2;
            }
        }

        if (broken.Count == 0)
        {
            Console.WriteLine("aucune liaison de commande cassee");
            return 0;
        }

        Console.WriteLine($"{broken.Count} liaison(s) de commande en echec :");
        foreach (var error in broken) Console.WriteLine("  " + error);
        return 1;
    }

    /// <summary>Basculer le theme APRES construction, comme le fait l'utilisateur.</summary>
    private static bool _switchAfterBuild;

    private static void Render(Window window, string outPath, string target, string lang)
    {
        // La largeur vient de la fenêtre ; la hauteur de la mesure, ces fenêtres étant en
        // SizeToContent (leur propriété Height vaut NaN).
        var width = double.IsNaN(window.Width) ? 900 : window.Width;
        var root = (FrameworkElement)window.Content;

        // Deux passages : certaines géométries se posent au premier LayoutUpdated, donc après la
        // première mesure — un seul passage retiendrait la hauteur d'avant.
        double height = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            root.Measure(new System.Windows.Size(width, double.PositiveInfinity));
            height = root.DesiredSize.Height;
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
        }

        if (_switchAfterBuild)
        {
            var res = Application.Current.Resources;
            Console.WriteLine($"  avant : Brush.Text = {(res["Brush.Text"] as SolidColorBrush)?.Color}, "
                              + $"{res.MergedDictionaries.Count} dictionnaire(s)");
            DockPad.Services.ThemeService.Apply(dark: true);
            Console.WriteLine($"  apres : Brush.Text = {(res["Brush.Text"] as SolidColorBrush)?.Color}");
            foreach (var d in res.MergedDictionaries)
                Console.WriteLine($"     - {d.Source}");
            root.Measure(new System.Windows.Size(width, double.PositiveInfinity));
            height = root.DesiredSize.Height;
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
        }

        // PresentationSource.FromVisual est nul ici : l'échelle DPI vient de VisualTreeHelper.
        var dpi = VisualTreeHelper.GetDpi(root);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        // Le fond de la FENETRE avant son contenu : on rend l'element de contenu, qui n'a pas de
        // fond a lui, si bien que la capture sortait transparente derriere le texte. Compose sur
        // noir par la visionneuse, un titre clair y paraissait sombre — quatre faux diagnostics
        // pendant la mise au point du theme sombre, tous dus a cette seule transparence.
        if (Backdrop(root) is { } backdrop)
        {
            var canvas = new DrawingVisual();
            using (var dc = canvas.RenderOpen())
                dc.DrawRectangle(backdrop, null, new Rect(0, 0, width, height));
            bitmap.Render(canvas);
        }

        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        using var stream = File.Create(outPath);
        encoder.Save(stream);

        Console.WriteLine($"capturé : {outPath} ({target}, {lang}, {width}x{Math.Ceiling(height)})");
    }

    /// <summary>Fond de la fenetre qui porte cet element, ou <c>null</c> s'il n'y en a pas.</summary>
    private static Brush? Backdrop(DependencyObject element) =>
        Window.GetWindow(element)?.Background;
}
