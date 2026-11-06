using System.ComponentModel;

namespace DockPad.Services.Updates;

/// <summary>
/// Regroupement sans consultation des processus.
/// </summary>
public static class UpdateBlockerGroups
{
    public static IReadOnlyList<UpdateBlockerGroup> Group(IReadOnlyList<UpdateBlocker> blockers) => blockers
        .GroupBy(b => (b.IsRelay, Program: b.IsRelay ? "" : b.Name), new GroupKeyComparer())
        .OrderByDescending(g => g.Key.IsRelay)
        .ThenBy(g => g.Key.Program, StringComparer.CurrentCultureIgnoreCase)
        .Select(g => new UpdateBlockerGroup(g.Key.IsRelay, g.Key.Program, g.OrderBy(b => b.Pid).ToArray()))
        .ToArray();

    // Le nom d'un programme est insensible à la casse, comme ses fichiers sous Windows.
    private sealed class GroupKeyComparer : IEqualityComparer<(bool IsRelay, string Program)>
    {
        public bool Equals((bool IsRelay, string Program) x, (bool IsRelay, string Program) y) =>
            x.IsRelay == y.IsRelay && StringComparer.OrdinalIgnoreCase.Equals(x.Program, y.Program);
        public int GetHashCode((bool IsRelay, string Program) key) =>
            HashCode.Combine(key.IsRelay, StringComparer.OrdinalIgnoreCase.GetHashCode(key.Program));
    }
}

public sealed class UpdateBlockerGroup(bool isRelay, string program, IReadOnlyList<UpdateBlocker> processes) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsRelay => isRelay;
    public string Name => isRelay ? UpdateTexts.T("Update_McpGroup") : program;
    public string Help => UpdateTexts.T(isRelay ? "Update_McpHelp" : "Update_ProgramHelp");
    public IReadOnlyList<UpdateBlocker> Processes => processes;
    public int Count => processes.Count;
    public bool? Selected
    {
        get => UpdateBlockers.SelectionState(processes);
        set
        {
            foreach (var process in processes) process.Selected = value == true;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
        }
    }
}
