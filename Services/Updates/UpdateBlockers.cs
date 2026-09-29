using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DockPad.Services.Updates;

public sealed class UpdateBlocker
{
    public int Pid { get; init; }
    public string Name { get; init; } = "";
    public long Started { get; init; }
    public string File { get; init; } = "";
    public bool Selected { get; set; }
    public string Label => $"{Name} · PID {Pid}";
}

/// <summary>Restart Manager identifies locks; never calls RmShutdown (which may force closure).</summary>
public static class UpdateBlockers
{
    public static IReadOnlyList<UpdateBlocker> Find(string directory)
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
                    if (image is not null && AppInstallation.IsWithin(image, directory))
                        results[process.Id] = new UpdateBlocker { Pid = process.Id, Name = process.ProcessName,
                            Started = process.StartTime.ToFileTimeUtc(), File = image };
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
            }
        }
        return results.Values.OrderBy(p => p.Name).ThenBy(p => p.Pid).ToArray();
    }
    public static async Task<bool> CloseAsync(UpdateBlocker blocker, bool force)
    {
        try
        {
            using var process = Process.GetProcessById(blocker.Pid);
            if (process.StartTime.ToFileTimeUtc() != blocker.Started) return true; // PID reused.
            if (force) process.Kill(entireProcessTree: false);
            else if (!process.CloseMainWindow()) return false;
            await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(2000));
            return process.HasExited;
        }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
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
