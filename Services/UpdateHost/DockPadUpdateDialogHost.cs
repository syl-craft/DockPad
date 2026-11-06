using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DockPad.Services.Updates;

namespace DockPad.Services.UpdateHost;

public sealed class DockPadUpdateDialogHost : IUpdateDialogHost
{
    public IUpdateLog Log { get; } = new DockPadUpdateLog();
    public IUpdateBlockerClassifier Classifier { get; } = new DockPadBlockerClassifier();
    public IUpdateBlockerFinder Finder { get; }
    public bool IsExiting => App.IsExiting;
    public string LatestReleasePage => "https://github.com/syl-craft/DockPad/releases/latest";
    public string ReleasePage(string version) =>
        "https://github.com/syl-craft/DockPad/releases/tag/v" + Uri.EscapeDataString(version.TrimStart('v'));

    public DockPadUpdateDialogHost() => Finder = new WindowsUpdateBlockerFinder(Log);

    public async Task<bool> PrepareAsync(object owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (AppInstallation.IsWithin(AppPaths.ProfileRoot, AppInstallation.Root ?? AppContext.BaseDirectory))
            throw new IOException(Loc.T("Update_ProfileInside"));
        return Application.Current is App app && await app.PrepareForUpdateAsync((Window)owner);
    }

    public void BeforeApply()
    {
        AppInstallation.BeginUpdate();
        try { ((App)Application.Current).BeginUpdateHandoff(); }
        catch { ApplyFailed(); throw; }
    }

    public void ApplyFailed()
    {
        AppInstallation.EndUpdate();
        ((App)Application.Current).CancelUpdateHandoff();
    }

    // Les relais déjà acceptés sont drainés après la pose du marqueur, avant de quitter.
    public void ExitAfterApply() =>
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(App.Exit));

    public void ShowError(string message, object owner) => AppDialog.Error(message, Loc.T("Update_Title"), (Window)owner);
}
