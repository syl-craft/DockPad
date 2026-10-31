using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// L'hôte de test garde une Application vivante sans lancer le démarrage réel de DockPad, et sans toucher au profil de
/// l'utilisateur.
/// </summary>
public class WpfTestHostTests
{
    private static object? AppField(string name) =>
        typeof(App).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(Application.Current);

    [Fact]
    public Task Hote_AnnuleLeVraiOnStartup() => WpfTestHost.Run(() =>
    {
        // Les opérations déjà postées ont le temps de tourner : un OnStartup qui aurait survécu s'y exécuterait.
        WpfTestHost.Flush();

        Assert.NotNull(Application.Current);
        Assert.False(Application.Current.Dispatcher.HasShutdownStarted);
        Assert.Null(AppField("_mutex"));
        Assert.Null(AppField("_trayIcon"));
        Assert.Null(AppField("_mainWindow"));
    });

    [Fact]
    public Task Hote_LaisseTournerLesOperationsPosteesApresLaConstruction() => WpfTestHost.Run(() =>
    {
        var ran = false;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Send, () => ran = true);

        WpfTestHost.Flush();

        Assert.True(ran);
    });

    [Fact]
    public void Profil_EstCeluiDuProcessusDeTest_JamaisLeProfilReel()
    {
        var realProfile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DockPad");

        Assert.StartsWith(TestProfile.Root, AppPaths.ProfileRoot);
        Assert.NotEqual(realProfile, AppPaths.ProfileRoot);
    }

    [Fact]
    public Task Hote_NEcritRienDansLeJournalDuProfil() => WpfTestHost.Run(() =>
    {
        WpfTestHost.Flush();

        Assert.False(Directory.Exists(Path.Combine(AppPaths.ProfileRoot, "logs")));
    });
}
