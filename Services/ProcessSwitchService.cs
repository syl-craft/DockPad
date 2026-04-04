using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using DockPad.Models;

namespace DockPad.Services;

public static class ProcessSwitchService
{
    public static void SwitchOrLaunch(ProcessSwitchConfig config)
    {
        if (config.SearchMode == ProcessSearchMode.ByWindowTitle)
        {
            SwitchOrLaunchByWindowTitle(config);
            return;
        }

        // ── Par nom de processus (comportement existant) ──────────────────────
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
                    BringToFront(proc.MainWindowHandle);
                    return;
                }
            }
            catch { }
        }

        // Process non trouvé → lancer
        Process.Start(new ProcessStartInfo(config.Executable, config.Parameters)
            { UseShellExecute = true });
    }

    // ── Recherche par titre de fenêtre ────────────────────────────────────────

    private static void SwitchOrLaunchByWindowTitle(ProcessSwitchConfig config)
    {
        IntPtr found = FindWindowByTitle(config.ProcessName);
        if (found != IntPtr.Zero)
        {
            BringToFront(found);
            return;
        }

        // Fenêtre non trouvée → lancer
        Process.Start(new ProcessStartInfo(config.Executable, config.Parameters)
            { UseShellExecute = true });
    }

    private static IntPtr FindWindowByTitle(string titleFragment)
    {
        if (string.IsNullOrWhiteSpace(titleFragment)) return IntPtr.Zero;

        IntPtr result = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, sb.Capacity);
            if (sb.ToString().Contains(titleFragment, StringComparison.OrdinalIgnoreCase))
            {
                result = hWnd;
                return false; // stopper l'énumération
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    // ── Utilitaires ───────────────────────────────────────────────────────────

    private static string? GetCommandLine(int pid)
    {
        using var searcher = new ManagementObjectSearcher(
            $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
        foreach (ManagementObject obj in searcher.Get())
            return obj["CommandLine"]?.ToString();
        return null;
    }

    private static void BringToFront(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        if (IsIconic(hWnd)) ShowWindow(hWnd, 9); // SW_RESTORE
        SetForegroundWindow(hWnd);
    }

    // ── P/Invoke ──────────────────────────────────────────────────────────────

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int  GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
}
