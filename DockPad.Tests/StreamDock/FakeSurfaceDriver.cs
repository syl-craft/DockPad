using DockPad.Surfaces;

namespace DockPad.Tests;

internal sealed class FakeSurfaceDriver : IControlSurfaceDriver
{
    private int _searches;
    private int _opens;
    public string Name => "Fake";
    public FakeSurface? Surface { get; set; } = new();
    public bool Busy { get; set; }
    public Exception? OpenFailure { get; set; }
    public TaskCompletionSource? OpenGate { get; set; }
    public TaskCompletionSource OpenStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Searches => Volatile.Read(ref _searches);
    public int Opens => Volatile.Read(ref _opens);
    public event Action? SurfacesChanged;
    public void Changed() => SurfacesChanged?.Invoke();

    public IReadOnlyList<SurfaceInfo> FindSurfaces()
    {
        Interlocked.Increment(ref _searches);
        return Surface == null ? [] : [new(Name, "fake", "Fake", null)];
    }

    public async Task<IControlSurface> OpenAsync(SurfaceInfo surface, CancellationToken ct)
    {
        Interlocked.Increment(ref _opens);
        OpenStarted.TrySetResult();
        if (OpenGate != null) await OpenGate.Task;
        if (OpenFailure != null) throw OpenFailure;
        if (Busy) throw new SurfaceBusyException("Fake surface is busy.");
        return Surface ?? throw new InvalidOperationException("No fake surface.");
    }
}
