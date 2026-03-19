using System.Drawing;
using System.Threading;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace DockPad;

public partial class App : Application
{
    public static bool IsExiting { get; private set; }

    public static void Exit()
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

        _mutex = new Mutex(initiallyOwned: true, "DockPad_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            Current.Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _mainWindow = new QuickAccessWindow();
        MainWindow = _mainWindow;
        _mainWindow.Show();

        _trayIcon = CreateTrayIcon();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
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
