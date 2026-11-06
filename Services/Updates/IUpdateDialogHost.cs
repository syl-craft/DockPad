using System.Threading;
using System.Threading.Tasks;

namespace DockPad.Services.Updates;

public interface IUpdateDialogHost
{
    Task<bool> PrepareAsync(object owner, CancellationToken token);
    void BeforeApply();
    void ApplyFailed();
    void ExitAfterApply();
    bool IsExiting { get; }
    void ShowError(string message, object owner);
    IUpdateLog Log { get; }
    IUpdateBlockerClassifier Classifier { get; }
    IUpdateBlockerFinder Finder { get; }
    string ReleasePage(string version);
    string LatestReleasePage { get; }
}
