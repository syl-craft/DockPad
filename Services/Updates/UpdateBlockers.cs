using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace DockPad.Services.Updates;

public sealed class UpdateBlocker : System.ComponentModel.INotifyPropertyChanged
{
    private bool _selected;
    private bool _isRelay;
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public int Pid { get; init; }
    public string Name { get; init; } = "";
    public long Started { get; init; }
    public string File { get; init; } = "";
    public bool IsRelay
    {
        get => _isRelay;
        init { _isRelay = value; _selected = value; }
    }
    public string? ParentName { get; init; }
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Selected)));
        }
    }
    public string Label => $"{Name} · PID {Pid}";
    public string DisplayName => IsRelay ? ParentName ?? UpdateTexts.T("Update_UnknownProgram") : Name;
    public string Detail => Started > 0
        ? UpdateTexts.F("Update_ProcessDetail", FormatStarted(DateTime.FromFileTimeUtc(Started).ToLocalTime(), DateTime.Now), Pid)
        : UpdateTexts.F("Update_ProcessPid", Pid);
    internal static string FormatStarted(DateTime started, DateTime now)
    {
        if (started.Date == now.Date) return started.ToString("t", UpdateTexts.Formatting);
        if (started.Date == now.Date.AddDays(-1)) return UpdateTexts.F("Update_ProcessYesterday", started.ToString("t", UpdateTexts.Formatting));
        return started.ToString("d", UpdateTexts.Formatting);
    }
}

public static class UpdateBlockers
{
    /// <summary>
    /// État de la case « Tout sélectionner » : <c>null</c> quand une partie seulement est cochée.
    /// </summary>
    public static bool? SelectionState(IReadOnlyCollection<UpdateBlocker> blockers)
    {
        var selectedCount = blockers.Count(b => b.Selected);
        if (selectedCount == 0) return false;
        return selectedCount == blockers.Count ? true : null;
    }

    /// <summary>
    /// Coche tout, sauf si tout l'était déjà : alors tout se décoche.
    /// </summary>
    public static void ToggleAll(IReadOnlyCollection<UpdateBlocker> blockers)
    {
        var selectAll = SelectionState(blockers) != true;
        foreach (var blocker in blockers) blocker.Selected = selectAll;
    }

    public static bool MatchesIdentity(UpdateBlocker blocker, long started) => blocker.Started > 0 && blocker.Started == started;

    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<UpdateBlocker> Find(string directory, IUpdateBlockerClassifier classifier, bool enrich = true) =>
        new WindowsUpdateBlockerFinder().Find(directory, classifier, enrich);

    [SupportedOSPlatform("windows")]
    public static Task<bool> CloseAsync(UpdateBlocker blocker, bool force) =>
        new WindowsUpdateBlockerFinder().CloseAsync(blocker, force);
}