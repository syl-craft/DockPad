using System.Runtime.InteropServices;

namespace DockPad.Services.StreamDock;

public sealed record SystemLoad(int? CpuPct, int? MemoryPct);

public interface ISystemLoadMonitor
{
    SystemLoad Sample();
    void Reset();
}

internal readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);

/// <summary>
/// Charge du PC sur l'intervalle entre deux lectures réussies ; une lecture en échec conserve la dernière valeur.
/// </summary>
public sealed class SystemLoadMonitor : ISystemLoadMonitor
{
    private readonly object _sync = new();
    private readonly Func<CpuTimes?> _readCpu;
    private readonly Func<int?> _readMemory;
    private CpuTimes? _previousCpu;
    private int? _cpuPct;
    private int? _memoryPct;

    public SystemLoadMonitor() : this(ReadCpu, ReadMemory)
    {
    }

    internal SystemLoadMonitor(Func<CpuTimes?> readCpu, Func<int?> readMemory)
    {
        ArgumentNullException.ThrowIfNull(readCpu);
        ArgumentNullException.ThrowIfNull(readMemory);
        _readCpu = readCpu;
        _readMemory = readMemory;
    }

    public SystemLoad Sample()
    {
        lock (_sync)
        {
            var currentCpu = _readCpu();
            if (currentCpu != null)
            {
                if (_previousCpu != null)
                    _cpuPct = CpuPercent(_previousCpu.Value, currentCpu.Value);
                _previousCpu = currentCpu;
            }

            _memoryPct = _readMemory() ?? _memoryPct;
            return new SystemLoad(_cpuPct, _memoryPct);
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _previousCpu = null;
            _cpuPct = null;
            _memoryPct = null;
        }
    }

    internal static int CpuPercent(CpuTimes previous, CpuTimes current)
    {
        var idle = (decimal)current.Idle - previous.Idle;
        var kernel = (decimal)current.Kernel - previous.Kernel;
        var user = (decimal)current.User - previous.User;
        // Le temps noyau fourni par Windows inclut déjà le temps inactif.
        var total = kernel + user;
        if (total <= 0)
            return 0;

        var percent = Math.Clamp(100m * (total - idle) / total, 0m, 100m);
        return (int)Math.Round(percent, MidpointRounding.AwayFromZero);
    }

    private static CpuTimes? ReadCpu()
    {
        if (SystemLoadNative.GetSystemTimes(out var idle, out var kernel, out var user) == false)
            return null;

        return new CpuTimes(idle.Ticks, kernel.Ticks, user.Ticks);
    }

    private static int? ReadMemory()
    {
        var status = new SystemLoadNative.MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<SystemLoadNative.MemoryStatusEx>(),
        };
        if (SystemLoadNative.GlobalMemoryStatusEx(ref status) == false)
            return null;

        return (int)status.MemoryLoad;
    }
}

internal static partial class SystemLoadNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;

        public readonly ulong Ticks => ((ulong)HighDateTime << 32) | LowDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
}
