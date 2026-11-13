using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32.SafeHandles;

namespace DockPad.Mcp;

internal sealed record McpProcessEntry(int Pid, int ParentPid, string ExecutableName, DateTime StartedAt);
internal enum McpClientState { Unknown, Running, Exited }
internal sealed record McpClientSelection(McpClientState State, McpProcessEntry? Client = null);

/// <summary>
/// Le lanceur Velopack attend le relais : sa présence ne prouve donc pas que le client MCP vit encore.
/// </summary>
internal static class McpClientProcess
{
    public static McpClientSelection SelectClient(int relayPid, IReadOnlyList<McpProcessEntry> entries)
    {
        var child = entries.Where(entry => entry.Pid == relayPid).FirstOrDefault();
        var visited = new HashSet<int>();
        while (child != null && visited.Add(child.Pid))
        {
            var parent = entries.Where(entry => entry.Pid == child.ParentPid).FirstOrDefault();
            if (parent == null) return new(McpClientState.Unknown);
            // Un PID recyclé désigne un autre programme : attendre sa mort retiendrait encore le relais.
            if (parent.StartedAt > child.StartedAt) return new(McpClientState.Exited);
            if (!string.Equals(parent.ExecutableName, "DockPad.exe", StringComparison.OrdinalIgnoreCase))
                return new(McpClientState.Running, parent);
            child = parent;
        }
        return new(McpClientState.Unknown);
    }

    public static (McpClientSelection Selection, Process? Client) FindClient()
    {
        var entries = new List<McpProcessEntry>();
        var pid = Environment.ProcessId;
        var visited = new HashSet<int>();
        while (visited.Add(pid))
        {
            Process? process = null;
            try
            {
                process = Process.GetProcessById(pid);
                // Ouvrir le handle avant les lectures et le garder pour l'attente évite de rouvrir un PID recyclé.
                var handle = process.SafeHandle;
                if (NtQueryInformationProcess(handle, 0, out var basic, Marshal.SizeOf<ProcessBasicInformation>(), out _) != 0)
                    return (new(McpClientState.Unknown), null);
                if (basic.ParentPid > int.MaxValue) return (new(McpClientState.Unknown), null);
                var entry = new McpProcessEntry(pid, (int)basic.ParentPid,
                    process.ProcessName + ".exe", process.StartTime.ToUniversalTime());
                entries.Add(entry);
                var selection = SelectClient(Environment.ProcessId, entries);
                if (selection.State == McpClientState.Exited) return (selection, null);
                if (selection.State == McpClientState.Running)
                {
                    var client = process;
                    process = null;
                    return (selection, client);
                }
                pid = entry.ParentPid;
            }
            // L'identification est facultative : un refus d'accès ne doit pas couper une session valide.
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
            {
                return (new(McpClientState.Unknown), null);
            }
            finally { process?.Dispose(); }
        }
        return (new(McpClientState.Unknown), null);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public nint ExitStatus;
        public nint PebBaseAddress;
        public nint AffinityMask;
        public nint BasePriority;
        public nuint Pid;
        public nuint ParentPid;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(SafeProcessHandle process, int informationClass,
        out ProcessBasicInformation information, int informationLength, out int returnLength);
}

/// <summary>
/// Certains clients laissent stdin ouvert à leur mort ; le handle du client fournit alors le second signal d'arrêt.
/// </summary>
internal sealed class McpClientLifetime(IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var (selection, client) = McpClientProcess.FindClient();
        using (client)
        {
            if (selection.State == McpClientState.Unknown) return;
            try
            {
                if (client != null) await client.WaitForExitAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Win32Exception) { return; }
            if (stoppingToken.IsCancellationRequested) return;
            LogService.Info("Relais MCP arrêté : client disparu.");
            lifetime.StopApplication();
        }
    }
}
