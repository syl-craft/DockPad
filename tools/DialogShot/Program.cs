using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
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
            Console.WriteLine("usage : DialogShot <settings|ctxmenu|presets|mcp|bindings|grid> <fr|en> <chemin.png>");
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
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

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
            _ => throw new ArgumentException($"fenêtre inconnue : {target}"),
        };

        Render(window, outPath, target, lang);
    }

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
            ("UsageConfigDialog", () => new UsageConfigDialog()),
            ("BrowserConfigDialog", () => new BrowserConfigDialog()),
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

        // PresentationSource.FromVisual est nul ici : l'échelle DPI vient de VisualTreeHelper.
        var dpi = VisualTreeHelper.GetDpi(root);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        using var stream = File.Create(outPath);
        encoder.Save(stream);

        Console.WriteLine($"capturé : {outPath} ({target}, {lang}, {width}x{Math.Ceiling(height)})");
    }
}
