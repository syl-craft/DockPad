using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using DockPad.Services.Localization;
using WinForms = System.Windows.Forms;

namespace DockPad;

public partial class App : Application
{
    public static bool IsExiting { get; private set; }

    public static new void Exit()
    {
        IsExiting = true;
        Current.Shutdown();
    }

    private WinForms.NotifyIcon _trayIcon = null!;
    private QuickAccessWindow _mainWindow = null!;
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Avant tout acces aux chemins : AppPaths ne resout sa racine qu'une fois, et le
        // journal ecrit deja dedans.
        Services.AppPaths.Initialize("DockPad");

        Services.LogService.Init();

        // Avant toute fenêtre : une vue construite avant ce point figerait ses libellés dans la
        // langue par défaut. ApplyWpfLanguage fait suivre FrameworkElement.Language, que WPF lit
        // pour les StringFormat de liaison au lieu de CurrentCulture — voir sa remarque.
        Services.Localization.Loc.SetCulture(
            Services.Localization.Loc.Parse(Services.SettingsService.LoadLanguage()));
        ApplyWpfLanguage();

        // Même raison que la langue : une fenêtre construite avant ce point aurait résolu la
        // palette au chargement. Les références sont en DynamicResource, donc un remplacement
        // ultérieur serait bien vu — mais le démarrage clignoterait en clair avant de basculer.
        Services.ThemeService.ApplyFromSettings();

        // « Automatique » doit suivre Windows en cours de route, pas seulement au démarrage.
        // Le désabonnement a lieu dans OnExit : App masque l'événement Exit par une méthode
        // statique du même nom.
        Services.ThemeService.StartFollowingSystem();

        // Filets de sécurité : une exception non gérée ne doit pas tuer l'app résidente
        // (systray + hotkey). Tracée dans %APPDATA%\DockPad\logs\ + dialog d'erreur.
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            Services.LogService.Error(args.Exception, "Exception UI non gérée");
            try { AppDialog.Error($"Erreur inattendue :\n{args.Exception.Message}", "DockPad"); }
            catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Services.LogService.Error(ex, "Exception non gérée (AppDomain)");
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Services.LogService.Error(args.Exception, "Exception de tâche non observée");
            args.SetObserved();
        };

        // Mode relais MCP : serveur stdio lancé par Claude Code / Claude Desktop.
        // Aucune UI, pas de mutex (coexiste avec l'instance principale et d'autres relais).
        if (e.Args.Contains("--mcp"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = Mcp.McpRelay.RunAsync()
                .ContinueWith(_ => Dispatcher.BeginInvoke(() => Shutdown()));
            return;
        }

        string? url = ParseArg(e.Args, "--url");
        string? injectPath = ParseArg(e.Args, "--inject-secrets");

        _mutex = new Mutex(initiallyOwned: true, "DockPad_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;

            // Instance secondaire lancée par Windows avec un fichier à injecter : relais via pipe.
            if (injectPath is not null && !InjectPipe.TrySend(injectPath))
            {
                // Repli : l'instance principale est injoignable, on rend ici. Elle n'a pas de
                // systray, donc elle mourrait à la fermeture de la fenêtre — et OnExit viderait le
                // presse-papier avant que l'utilisateur ait pu coller. D'où la sortie différée.
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var window = Secrets.SecretInjection.Handle(injectPath);
                window.Closed += (_, _) => ShutdownWhenClipboardIsSafe();
                return;
            }

            // Instance secondaire lancée par Windows avec une URL : relais via pipe.
            if (url is not null && !Services.UrlPipeService.TrySend(url))
            {
                // Fallback : instance principale injoignable → popup locale. On reste en
                // shutdown explicite : si aucune popup n'a été créée (lancement direct via
                // une règle), on quitte immédiatement ; sinon on quitte à sa fermeture.
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var picker = Services.UrlRouterService.Handle(url);
                if (picker is not null)
                    picker.Closed += (_, _) => Shutdown();
                else
                    Shutdown();
                return;
            }

            Current.Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _mainWindow = new QuickAccessWindow();
        MainWindow = _mainWindow;

        if (url is null && injectPath is null)
        {
            _mainWindow.Show();
        }
        else
        {
            // Lancé par un clic sur une URL alors que DockPad ne tournait pas :
            // démarrage en arrière-plan. EnsureHandle déclenche OnSourceInitialized
            // (hook WndProc + hotkey global) sans afficher la fenêtre.
            new System.Windows.Interop.WindowInteropHelper(_mainWindow).EnsureHandle();
        }

        _trayIcon = CreateTrayIcon();

        Services.UrlPipeService.StartServer(u =>
            Dispatcher.BeginInvoke(() => Services.UrlRouterService.Handle(u)));

        InjectPipe.StartServer(path =>
            Dispatcher.BeginInvoke(() => Secrets.SecretInjection.Handle(path)));

        Services.McpDispatcher.OnMutation = () => Dispatcher.BeginInvoke(() => _mainWindow.RefreshGrid());
        Services.McpPipeService.StartServer(req => Services.McpDispatcher.Handle(req));

        if (url is not null)
            Dispatcher.BeginInvoke(() => Services.UrlRouterService.Handle(url));

        if (injectPath is not null)
            Dispatcher.BeginInvoke(() => Secrets.SecretInjection.Handle(injectPath));
    }

    /// <summary>Pipe du clic droit « Injecter les secrets… », jumeau de celui des URL.</summary>
    private static readonly Services.LinePipeService InjectPipe = new("DockPad_InjectPipe");

    private static string? ParseArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name)
                return args[i + 1];
        return null;
    }

    /// <summary>
    /// Quitte, mais pas avant que le presse-papier ait été rendu à l'utilisateur.
    /// </summary>
    /// <remarks>
    /// Seule l'instance éphémère du repli en a besoin : l'instance résidente ne meurt pas à la
    /// fermeture d'une fenêtre. Sans ce délai, sa sortie viderait le presse-papier immédiatement,
    /// et l'injection paraîtrait n'avoir rien fait.
    /// </remarks>
    private void ShutdownWhenClipboardIsSafe()
    {
        if (!Secrets.SecretInjection.IsClipboardArmed) { Shutdown(); return; }

        void OnChanged(object? sender, EventArgs e)
        {
            if (Secrets.SecretInjection.IsClipboardArmed) return;
            Secrets.SecretInjection.ClipboardChanged -= OnChanged;
            Dispatcher.BeginInvoke(() => Shutdown());
        }

        Secrets.SecretInjection.ClipboardChanged += OnChanged;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Filet de sortie : un rendu encore dans le presse-papier en est retiré, à condition qu'il
        // s'y trouve toujours — l'utilisateur a pu copier autre chose entre-temps.
        Secrets.SecretInjection.ClearClipboardNow();

        // SystemEvents garde une référence statique sur l'abonné.
        Services.ThemeService.StopFollowingSystem();
        _trayIcon?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        Services.LogService.Shutdown();
        base.OnExit(e);
    }

    private WinForms.NotifyIcon CreateTrayIcon()
    {
        var icon = Icon.ExtractAssociatedIcon(
            Environment.ProcessPath
            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName)
            ?? SystemIcons.Application;

        var menu = new WinForms.ContextMenuStrip();
        var closeItem = new WinForms.ToolStripMenuItem("Fermer");
        closeItem.Click += (_, _) => Exit();
        menu.Items.Add(closeItem);

        var tray = new WinForms.NotifyIcon
        {
            Icon = icon,
            Text = "DockPad",
            Visible = true,
            ContextMenuStrip = menu,
        };

        tray.MouseClick += (_, args) =>
        {
            if (args.Button != WinForms.MouseButtons.Left) return;
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        };

        return tray;
    }

    /// <summary>
    /// Fait suivre à WPF la langue courante, en plus des cultures posées par
    /// <see cref="Loc.SetCulture"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WPF ignore <c>CurrentCulture</c> pour les <c>StringFormat</c> de liaison : il lit
    /// <c>FrameworkElement.Language</c>. Sans ça, un nombre ou une date formaté par une liaison sort
    /// en <c>en-US</c> quelle que soit la culture du thread.
    /// </para>
    /// <para>
    /// <b>Pourquoi un gestionnaire de classe et non un <c>OverrideMetadata</c>.</b>
    /// <c>OverrideMetadata</c> ne s'appelle qu'une fois par propriété et par type : il figerait la
    /// langue du démarrage, et une fenêtre ouverte après une bascule hériterait de l'ancienne. Poser
    /// la langue au <c>Loaded</c> de chaque fenêtre couvre les deux cas, y compris les fenêtres
    /// ajoutées au projet plus tard, sans rien à brancher dans leur code.
    /// </para>
    /// <para>
    /// <c>Language</c> étant une propriété héritée, la poser sur la fenêtre suffit à couvrir tout
    /// son contenu.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <c>public</c> et non <c>private</c> : les outils de capture n'appellent jamais
    /// <c>OnStartup</c> — par conception, il monte mutex, systray et fenêtres — et doivent pourtant
    /// poser la langue à WPF, sans quoi tout <c>StringFormat</c> de liaison rendrait en <c>en-US</c>
    /// quelle que soit la langue demandée pour la capture. Même raison que
    /// <c>QuickAccessWindow.UsageBannerPanel</c>.
    /// </remarks>
    public static void ApplyWpfLanguage()
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is not Window window) return;

                window.Language = CurrentXmlLanguage();
                // Même point d'accroche pour la barre de titre : WPF ne la peint pas, elle
                // appartient au gestionnaire de fenêtres et resterait claire sur un contenu sombre.
                // Loaded arrive après SourceInitialized, le HWND existe donc.
                Services.ThemeService.ApplyTitleBar(window);
            }));

        // Les fenêtres déjà ouvertes au moment de la bascule : le gestionnaire ci-dessus ne les
        // reverra pas, leur Loaded est passé.
        Loc.LanguageChanged += (_, _) =>
        {
            var language = CurrentXmlLanguage();
            foreach (Window window in Current.Windows) window.Language = language;
        };
    }

    private static XmlLanguage CurrentXmlLanguage() =>
        XmlLanguage.GetLanguage(Loc.Current.IetfLanguageTag);
}
