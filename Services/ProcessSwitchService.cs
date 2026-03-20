using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using DockPad.Models;

namespace DockPad.Services;

public static class ProcessSwitchService
{
    public static void SwitchOrLaunch(ProcessSwitchConfig config)
    {
        string procName = Path.GetFileNameWithoutExtension(config.ProcessName);
        var procs = Process.GetProcessesByName(procName);

        foreach (var proc in procs)
        {
            try
            {
                string? cmdLine = GetCommandLine(proc.Id);
                if (cmdLine != null &&
                    (string.IsNullOrWhiteSpace(config.Parameters) ||
                     cmdLine.Contains(config.Parameters, StringComparison.OrdinalIgnoreCase)))
                {
                    BringToFront(proc);
                    return;
                }
            }
            catch { }
        }

        // Process non trouvé → lancer
        Process.Start(new ProcessStartInfo(config.Executable, config.Parameters)
            { UseShellExecute = true });
    }

    private static string? GetCommandLine(int pid)
    {
        using var searcher = new ManagementObjectSearcher(
            $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
        foreach (ManagementObject obj in searcher.Get())
            return obj["CommandLine"]?.ToString();
        return null;
    }

    private static void BringToFront(Process proc)
    {
        var hWnd = proc.MainWindowHandle;
        if (hWnd == IntPtr.Zero) return;
        if (IsIconic(hWnd)) ShowWindow(hWnd, 9); // SW_RESTORE
        SetForegroundWindow(hWnd);
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
}
