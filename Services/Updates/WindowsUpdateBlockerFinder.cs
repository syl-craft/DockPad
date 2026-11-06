using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace DockPad.Services.Updates;

/// <summary>
/// Restart Manager identifie les verrous ; RmShutdown n'est jamais appelé car il peut forcer la fermeture.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsUpdateBlockerFinder(IUpdateLog? log = null) : IUpdateBlockerFinder
{
    private readonly IUpdateLog _log = log ?? new SilentUpdateLog();
    public IReadOnlyList<UpdateBlocker> Find(string directory, IUpdateBlockerClassifier classifier, bool enrich = true)
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
        // Les processus console peuvent retenir une image sans être signalés par Restart Manager.
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                try
                {
                    var image = process.MainModule?.FileName;
                    if (classifier.IsIgnored(image, directory))
                    {
                        results.Remove(process.Id);
                        continue;
                    }
                    if (image is not null && VelopackLayout.IsWithin(image, directory))
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
            var classification = classifier.Classify(new(image, directory, process?.CommandLine,
                parent?.Name, parent?.Image, grandparent?.Name, grandparent?.Image));
            return new UpdateBlocker
            {
                Pid = blocker.Pid, Started = blocker.Started, Name = blocker.Name, File = image,
                IsRelay = classification.IsRelay, ParentName = classification.IsRelay ? classification.Program : null,
                Selected = classification.IsRelay,
            };
        }).OrderBy(p => p.Name).ThenBy(p => p.Pid).ToArray();
    }

    internal sealed record ProcessMetadata(int ParentPid, string? Name, string? Image, string? CommandLine);
    internal Dictionary<int, ProcessMetadata> ReadProcessMetadata(Action<Dictionary<int, ProcessMetadata>>? read = null)
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
            _log.Warn(ex, "Cannot identify update lock owners");
            metadata.Clear();
        }
        return metadata;
    }

    public async Task<bool> CloseAsync(UpdateBlocker blocker, bool force)
    {
        try
        {
            using var process = Process.GetProcessById(blocker.Pid);
            if (!UpdateBlockers.MatchesIdentity(blocker, process.StartTime.ToFileTimeUtc())) return true; // PID réutilisé.
            if (force || blocker.IsRelay) process.Kill(entireProcessTree: false);
            else if (!process.CloseMainWindow()) return false;
            await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(2000));
            return process.HasExited;
        }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _log.Warn(ex, "Cannot close an update lock owner");
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
