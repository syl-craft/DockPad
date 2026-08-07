using System.Drawing;
using System.Threading;
using System.Windows;
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

        Services.LogService.Init();

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

        string? url = ParseUrlArg(e.Args);

        _mutex = new Mutex(initiallyOwned: true, "DockPad_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;

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

        if (url is null)
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

        Services.McpDispatcher.OnMutation = () => Dispatcher.BeginInvoke(() => _mainWindow.RefreshGrid());
        Services.McpPipeService.StartServer(req => Services.McpDispatcher.Handle(req));

        if (url is not null)
            Dispatcher.BeginInvoke(() => Services.UrlRouterService.Handle(url));
    }

    private static string? ParseUrlArg(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--url")
                return args[i + 1];
        return null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
}
