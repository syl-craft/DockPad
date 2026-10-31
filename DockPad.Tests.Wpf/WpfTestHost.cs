using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace DockPad.Tests;

/// <summary>
/// Un seul thread STA, avec son Dispatcher et l'unique <c>Application</c> du processus de test : WPF n'en accepte
/// qu'une, et une fenêtre ne vit que sur le thread qui l'a construite.
/// </summary>
internal static class WpfTestHost
{
    private static readonly Lazy<Dispatcher> Host = new(Start);

    private static Dispatcher Start()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            // Les URI « application:,,, » (icône de la fenêtre) se résolvent dans l'assembly d'entrée, qui est ici
            // celui de l'hôte de test. Le setter public refuse dès que la valeur a été lue, ce qu'un autre test a pu
            // faire : on pose le champ lui-même.
            typeof(Application)
                .GetField("_resourceAssembly", BindingFlags.NonPublic | BindingFlags.Static)?
                .SetValue(null, typeof(App).Assembly);

            // Le vrai OnStartup ouvrirait le systray, les tubes et le journal du profil réel, puis arrêterait
            // l'application : la garde des outils de capture l'annule.
            var app = DockPad.Tools.ShotApp.Create();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        }) { IsBackground = true, Name = "WpfTestHost" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Exécute <paramref name="body"/> sur le thread WPF ; une exception du corps remonte dans le test.
    /// </summary>
    public static Task Run(Action body) => Host.Value.InvokeAsync(body).Task.WaitAsync(TimeSpan.FromSeconds(30));

    /// <summary>
    /// Traite les messages en attente, ceux de priorité Background et au-dessus.
    /// </summary>
    public static void Flush()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    /// <summary>
    /// Fait tourner le Dispatcher jusqu'à la fin de la tâche.
    /// </summary>
    public static void PumpUntilCompleted(Task task)
    {
        var frame = new DispatcherFrame();
        var dispatcher = Dispatcher.CurrentDispatcher;
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
        if (task.IsCompleted == false) Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }
}
