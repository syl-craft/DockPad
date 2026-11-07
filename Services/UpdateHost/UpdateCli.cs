using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mue;

namespace DockPad.Services.UpdateHost;

/// <summary>
/// <c>DockPad.exe update</c> : mise à jour sans fenêtre, pour un script ou un agent IA, dans un processus à part.
/// </summary>
/// <remarks>
/// <para>
/// Jamais relayée à l'instance ouverte : celle-ci compte parmi les programmes qui bloquent. Avec
/// <c>--close-blockers</c>, elle reçoit une demande de quitter par son tube d'affichage, comme « Fermer » dans la zone
/// de notification.
/// </para>
/// <para>
/// Le lanceur stable rend la main sans attendre son enfant, sauf en MCP : un script appelle
/// <c>current\DockPad.exe update</c> pour obtenir le code de sortie.
/// </para>
/// </remarks>
public static class UpdateCli
{
    public const string Verb = "update";

    public static bool Handles(string[] args) => args.Length > 0 && args[0] == Verb;

    public static int Run(string[] args)
    {
        AppPaths.Initialize("DockPad");
        LogService.Init();
        Localization.Loc.SetCulture(Localization.Loc.Parse(SettingsService.LoadLanguage()));
        var options = args[1..];
        var output = OpenOutput();
        try
        {
            var host = new DockPadCommandHost(restart: options.Contains("--restart"));
            return UpdateCommand.RunAsync(DockPadUpdates.Service, host, options, output).GetAwaiter().GetResult();
        }
        finally
        {
            output.Flush();
            DockPadUpdates.Stop();
            LogService.Shutdown();
        }
    }

    // Une WinExe n'a pas de console. S'y attacher alors que la sortie est redirigée remplacerait la redirection, et le
    // JSON attendu par le script serait perdu.
    private static TextWriter OpenOutput()
    {
        var handle = GetStdHandle(StdOutputHandle);
        if (handle != IntPtr.Zero && handle != new IntPtr(-1))
            return new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        AttachConsole(AttachParentProcess);
        return Console.Out;
    }

    private const int StdOutputHandle = -11;
    private const int AttachParentProcess = -1;
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int handle);
    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int processId);
}

/// <summary>
/// Hôte de la commande : ce processus n'a aucune fenêtre, il n'y a donc rien à vérifier ni à vider avant l'installation.
/// </summary>
public sealed class DockPadCommandHost(bool restart) : IUpdateDialogHost
{
    public IUpdateLog Log { get; } = new DockPadUpdateLog();
    public IUpdateBlockerClassifier Classifier { get; } = new DockPadBlockerClassifier();
    public IUpdateBlockerFinder Finder { get; } = new ResidentClosingFinder(new WindowsUpdateBlockerFinder(new DockPadUpdateLog()));
    public bool IsExiting => false;
    public string LatestReleasePage => DockPadUpdateDialogHost.LatestRelease;
    public string ReleasePage(string version) => DockPadUpdateDialogHost.Release(version);

    public Task<UpdatePreparationCheck> CheckAsync(object owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (AppInstallation.IsWithin(AppPaths.ProfileRoot, AppInstallation.Root ?? AppContext.BaseDirectory))
            throw new IOException(Loc.T("Update_ProfileInside"));
        return Task.FromResult(new UpdatePreparationCheck(true));
    }

    public async Task<bool> PrepareAsync(object owner, CancellationToken token)
    {
        await PendingWrites.CompleteAsync().WaitAsync(token);
        return true;
    }

    // Le marqueur retient les lancements pendant la copie ; seule l'instance relancée le retire. Sans --restart,
    // il retiendrait le prochain lancement jusqu'à cinq minutes.
    public void BeforeApply()
    {
        if (restart) AppInstallation.BeginUpdate();
    }

    public void ApplyFailed()
    {
        if (restart) AppInstallation.EndUpdate();
    }

    // Main rend le code de sortie, puis le processus quitte : Velopack attend cette sortie pour copier.
    public void ExitAfterApply() { }

    // La commande écrit elle-même l'échec dans son rapport.
    public void ShowError(string message, object owner) { }
}

/// <summary>
/// Ferme l'instance ouverte de DockPad par son tube : elle vit dans la zone de notification, et une fermeture douce de
/// sa fenêtre la cacherait au lieu de la faire quitter. Les autres programmes passent par le chercheur de Mue.
/// </summary>
public sealed class ResidentClosingFinder(IUpdateBlockerFinder inner) : IUpdateBlockerFinder
{
    public static bool IsResident(UpdateBlocker blocker) =>
        !blocker.IsRelay && !blocker.Unavailable && blocker.Name.Equals("DockPad", StringComparison.OrdinalIgnoreCase);

    public UpdateBlockerScan Find(UpdateBlockerQuery query, IUpdateBlockerClassifier classifier, bool enrich = true) =>
        inner.Find(query, classifier, enrich);

    public async Task<UpdateBlockerCloseResult> CloseAsync(UpdateBlocker blocker, bool force, CancellationToken token = default)
    {
        if (force || !IsResident(blocker)) return await inner.CloseAsync(blocker, force, token);
        try
        {
            using var process = Process.GetProcessById(blocker.Pid);
            if (!UpdateBlockers.MatchesIdentity(blocker, process.StartTime.ToFileTimeUtc())) return UpdateBlockerCloseResult.IdentityChanged;
            if (!new LinePipeService(StartupRelay.ShowPipeName).TrySendSilently(StartupRelay.QuitRequest, 2000))
                return UpdateBlockerCloseResult.StillRunning;
            // L'arrêt du Stream Dock et des tubes prend plus que les deux secondes d'une fenêtre ordinaire.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            return process.HasExited ? UpdateBlockerCloseResult.Closed : UpdateBlockerCloseResult.StillRunning;
        }
        catch (ArgumentException) { return UpdateBlockerCloseResult.Gone; }
        catch (InvalidOperationException) { return UpdateBlockerCloseResult.Gone; }
        catch (Win32Exception) { return UpdateBlockerCloseResult.AccessDenied; }
    }
}
