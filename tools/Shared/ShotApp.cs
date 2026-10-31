using System.Windows.Threading;
using DockPad;

namespace DockPad.Tools;

/// <summary>
/// L'<see cref="App"/> des outils de capture et des tests de fenêtre : ses ressources, jamais son démarrage.
/// </summary>
/// <remarks>
/// Le constructeur d'<c>Application</c> poste, en priorité <c>Send</c>, l'opération qui lance <c>OnStartup</c> dès qu'un
/// dispatcher tourne : mutex, systray, tubes, Stream Dock. Un outil qui pompe le dispatcher (<c>Show()</c> +
/// <c>Dispatcher.Run()</c>) l'exécuterait. Seule cette opération est annulée, et le crochet est retiré aussitôt.
/// </remarks>
internal static class ShotApp
{
    /// <summary>
    /// Construit l'application et charge App.xaml, sans le démarrage réel.
    /// </summary>
    /// <exception cref="InvalidOperationException">Le constructeur n'a posté aucune opération de démarrage : WPF a
    /// changé, et rien ne garantit plus que <c>OnStartup</c> ne tournera pas.</exception>
    public static App Create()
    {
        var hooks = Dispatcher.CurrentDispatcher.Hooks;
        DispatcherOperation? startup = null;

        void AbortStartup(object? sender, DispatcherHookEventArgs e)
        {
            if (startup is not null || e.Operation.Priority != DispatcherPriority.Send) return;

            startup = e.Operation;
            startup.Abort();
        }

        App app;
        hooks.OperationPosted += AbortStartup;
        try
        {
            app = new App();
        }
        finally
        {
            hooks.OperationPosted -= AbortStartup;
        }

        if (startup is null)
            throw new InvalidOperationException("The Application constructor posted no startup operation to abort.");

        app.InitializeComponent();
        return app;
    }
}
