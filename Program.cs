using System.Windows;
using DockPad.Services;

namespace DockPad;

/// <summary>
/// Point d'entrée explicite, pour que le raccourci de démarrage ait lieu <b>avant</b> WPF.
/// </summary>
/// <remarks>
/// <para>
/// WPF engendre normalement ce <c>Main</c> depuis <c>App.xaml</c>, et il commence par construire
/// l'<c>Application</c>. Or au clic sur un lien, ce processus n'a qu'une ligne à écrire dans un
/// tube avant de mourir : mesuré, il payait ~300 ms pour ~2 ms de travail utile. L'écrire à la
/// main est le seul moyen de décider avant que quoi que ce soit ne soit chargé.
/// </para>
/// <para>
/// <b>Le patron est déjà celui des outils de capture</b> (<c>McpShot</c>, <c>UsageShot</c>,
/// <c>BrowserShot</c>, <c>DialogShot</c>) : <c>new App()</c> puis <c>InitializeComponent()</c>.
/// La seule nouveauté est que l'application le fait pour elle-même.
/// </para>
/// <para>
/// <c>App.xaml</c> passe donc en <c>Page</c> et le csproj nomme ce type dans
/// <c>StartupObject</c> ; sans ça, deux <c>Main</c> coexisteraient.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// STA obligatoire : c'est le contrat de WPF, et il n'est plus posé par le <c>Main</c>
    /// engendré. Sans cet attribut, la première fenêtre lève.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        // L'instance résidente a pris la demande : rien à construire, rien à journaliser.
        if (StartupRelay.TryRelay(args)) return 0;

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
