using DockPad.Services.StreamDock;

namespace DockPad.Tests;

internal sealed class FakeSystemLoadMonitor : ISystemLoadMonitor
{
    public SystemLoad Value { get; set; } = new(23, 61);
    public Exception? Failure { get; set; }
    public int Samples { get; private set; }
    public int Resets { get; private set; }

    public SystemLoad Sample()
    {
        Samples++;
        if (Failure != null) throw Failure;
        return Value;
    }

    public void Reset() => Resets++;
}
