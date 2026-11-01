using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DockPad.Services.Updates;

public sealed class UpdateBlocker : System.ComponentModel.INotifyPropertyChanged
{
    private bool _selected;
    private bool _isMcpRelay;
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public int Pid { get; init; }
    public string Name { get; init; } = "";
    public long Started { get; init; }
    public string File { get; init; } = "";
    public bool IsMcpRelay
    {
        get => _isMcpRelay;
        init { _isMcpRelay = value; _selected = value; }
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
    public string DisplayName => IsMcpRelay ? ParentName ?? Loc.T("Update_UnknownProgram") : Name;
    public string Detail => Started > 0
        ? Loc.F("Update_ProcessDetail", FormatStarted(DateTime.FromFileTimeUtc(Started).ToLocalTime(), DateTime.Now), Pid)
        : Loc.F("Update_ProcessPid", Pid);
    internal static string FormatStarted(DateTime started, DateTime now)
    {
        if (started.Date == now.Date) return started.ToString("t", Loc.Formatting);
        if (started.Date == now.Date.AddDays(-1)) return Loc.F("Update_ProcessYesterday", started.ToString("t", Loc.Formatting));
        return started.ToString("d", Loc.Formatting);
    }
}

/// <summary>Restart Manager identifies locks; never calls RmShutdown (which may force closure).</summary>
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

    public static IReadOnlyList<UpdateBlocker> Find(string directory, bool enrich = true)
    {
        var results = new Dictionary<int, UpdateBlocker>();
        var sessionKey = new StringBuilder(33);
        Check(RmStartSession(out var session, 0, sessionKey));
        try
        {
            var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            foreach (var batch in files.Chunk(128))
                Check(RmRegisterResources(session, (uint)batch.Length, batch, 0, null, 0, null));
            uint count = 0, needed = 0, reasons = 0;
            var status = RmGetList(session, out needed, ref count, null, ref reasons);
            for (var attempt = 0; status == 234 && attempt < 5; attempt++)
            {
                var infos = new ProcessInfo[needed];
                count = needed;
                status = RmGetList(session, out needed, ref count, infos, ref reasons);
                if (status != 0) continue;
                foreach (var info in infos.Take((int)count))
                {
                    if (info.Process.Pid == Environment.ProcessId) continue;
                    results[info.Process.Pid] = new UpdateBlocker
                    {
                        Pid = info.Process.Pid, Name = info.Name,
                        Started = info.Process.Started.ToLong(),
                    };
                }
            }
            Check(status);
        }
        finally { RmEndSession(session); }
        // Include console/MCP processes even when Restart Manager does not report their image lock.
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                try
                {
                    var image = process.MainModule?.FileName;
                    if (UpdateBlockerGroups.IsStableLauncher(image, directory))
                    {
                        results.Remove(process.Id);
                        continue;
                    }
                    if (image is not null && AppInstallation.IsWithin(image, directory))
                        results[process.Id] = new UpdateBlocker { Pid = process.Id, Name = process.ProcessName,
                            Started = process.StartTime.ToFileTimeUtc(), File = image };
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
            }
        }
        // Le contrôle final de verrouillage n'a besoin d'aucun enrichissement WMI.
        if (!enrich) return results.Values.ToArray();
        var metadata = ReadProcessMetadata();
        return results.Values.Select(blocker =>
        {
            metadata.TryGetValue(blocker.Pid, out var process);
            ProcessMetadata? parent = null;
            if (process != null) metadata.TryGetValue(process.ParentPid, out parent);
            ProcessMetadata? grandparent = null;
            if (parent != null) metadata.TryGetValue(parent.ParentPid, out grandparent);
            var image = blocker.File.Length > 0 ? blocker.File : process?.Image ?? "";
            var classification = UpdateBlockerGroups.Classify(image, directory, process?.CommandLine,
                parent?.Name, parent?.Image, grandparent?.Name, grandparent?.Image);
            return new UpdateBlocker
            {
                Pid = blocker.Pid, Started = blocker.Started, Name = blocker.Name, File = image,
                IsMcpRelay = classification.IsMcpRelay, ParentName = classification.IsMcpRelay ? classification.Program : null,
                Selected = classification.IsMcpRelay,
            };
        }).OrderBy(p => p.Name).ThenBy(p => p.Pid).ToArray();
    }

    internal sealed record ProcessMetadata(int ParentPid, string? Name, string? Image, string? CommandLine);
    internal static Dictionary<int, ProcessMetadata> ReadProcessMetadata(Action<Dictionary<int, ProcessMetadata>>? read = null)
    {
        var metadata = new Dictionary<int, ProcessMetadata>();
        try
        {
            if (read != null) { read(metadata); return metadata; }
            using var search = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, Name, ExecutablePath, CommandLine FROM Win32_Process");
            using var processes = search.Get();
            foreach (ManagementObject process in processes)
            {
                using (process)
                    metadata[Convert.ToInt32(process["ProcessId"])] = new(Convert.ToInt32(process["ParentProcessId"]),
                        process["Name"] as string, process["ExecutablePath"] as string, process["CommandLine"] as string);
            }
        }
        // L'enrichissement d'affichage ne doit jamais empêcher une installation, même si WMI/COM tombe.
        catch (Exception ex)
        {
            LogService.Warn(ex, "Cannot identify update lock owners");
            metadata.Clear();
        }
        return metadata;
    }

    public static bool MatchesIdentity(UpdateBlocker blocker, long started) => blocker.Started > 0 && blocker.Started == started;
    public static async Task<bool> CloseAsync(UpdateBlocker blocker, bool force)
    {
        try
        {
            using var process = Process.GetProcessById(blocker.Pid);
            if (!MatchesIdentity(blocker, process.StartTime.ToFileTimeUtc())) return true; // PID reused.
            if (force || blocker.IsMcpRelay) process.Kill(entireProcessTree: false);
            else if (!process.CloseMainWindow()) return false;
            await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(2000));
            return process.HasExited;
        }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
        catch (System.ComponentModel.Win32Exception ex)
        {
            LogService.Warn(ex, "Cannot close an update lock owner");
            return false;
        }
    }
    private static void Check(int result)
    {
        if (result != 0) throw new System.ComponentModel.Win32Exception(result);
    }
    [StructLayout(LayoutKind.Sequential)] private struct FileTime
    {
        public uint Low, High;
        public readonly long ToLong() => ((long)High << 32) | Low;
    }
    [StructLayout(LayoutKind.Sequential)] private struct UniqueProcess { public int Pid; public FileTime Started; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ProcessInfo
    {
        public UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Service;
        public uint Type, Status, Session;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmStartSession(out uint session, uint flags, StringBuilder key);
    [DllImport("rstrtmgr.dll")] private static extern int RmEndSession(uint session);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmRegisterResources(uint session, uint fileCount,
        string[] files, uint appCount, UniqueProcess[]? apps, uint serviceCount, string[]? services);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmGetList(uint session, out uint needed,
        ref uint count, [In, Out] ProcessInfo[]? info, ref uint reasons);
}
