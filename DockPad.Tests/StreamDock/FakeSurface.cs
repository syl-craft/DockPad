using System.Collections.Concurrent;
using System.IO;
using DockPad.Surfaces;

namespace DockPad.Tests;

internal sealed class FakeSurface : IControlSurface
{
    private int _commits;
    private int _disposeCalls;
    private Exception? _disconnection;
    private Action<Exception>? _disconnected;
    public SurfaceDescription Description { get; set; } = new("Fake", 3, 5, 4, new SurfaceSize(75, 380));
    public ConcurrentQueue<(SurfaceKey Key, SurfaceImage Image)> Images { get; } = new();
    public ConcurrentQueue<SurfaceImage> SideStrips { get; } = new();
    public Func<CancellationToken, Task>? BeforeSideStrip { get; set; }
    public int Clears { get; private set; }
    public int Commits => Volatile.Read(ref _commits);
    public int DisposeCalls => Volatile.Read(ref _disposeCalls);
    public TaskCompletionSource? WriteGate { get; set; }
    public TaskCompletionSource? CommitGate { get; set; }
    public TaskCompletionSource? DisposeGate { get; set; }
    public Exception? WriteFailure { get; set; }
    public Exception? CommitFailure { get; set; }
    public Action? AfterCommit { get; set; }
    public Action? BeforeImage { get; set; }
    public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource CommitStarted { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource DisposeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public event Action<KeyEvent>? KeyChanged;
    public event Action<Exception>? Disconnected
    {
        add
        {
            _disconnected += value;
            if (_disconnection != null) value?.Invoke(_disconnection);
        }
        remove => _disconnected -= value;
    }

    public void Press(int index) => KeyChanged?.Invoke(new KeyEvent(Key(index), KeyState.Pressed));
    public void Release(int index) => KeyChanged?.Invoke(new KeyEvent(Key(index), KeyState.Released));
    public static SurfaceKey Key(int index) => new(index / 5, index % 5);

    public void Disconnect(Exception? cause = null)
    {
        cause ??= new IOException("Fake surface disconnected.");
        if (Interlocked.CompareExchange(ref _disconnection, cause, null) == null)
            _disconnected?.Invoke(cause);
    }

    public async Task SetKeyImageAsync(SurfaceKey key, SurfaceImage image, CancellationToken ct)
    {
        BeforeImage?.Invoke();
        WriteStarted.TrySetResult();
        if (WriteGate != null) await WriteGate.Task;
        if (_disconnection != null) throw _disconnection;
        if (WriteFailure != null) throw WriteFailure;
        ct.ThrowIfCancellationRequested();
        Images.Enqueue((key, image));
    }

    public async Task SetSideStripImageAsync(SurfaceImage image, CancellationToken ct)
    {
        if (BeforeSideStrip != null) await BeforeSideStrip(ct);
        ct.ThrowIfCancellationRequested();
        if (_disconnection != null) throw _disconnection;
        if (image.Width != Description.SideStrip?.Width || image.Height != Description.SideStrip?.Height)
            throw new ArgumentException("Side strip image size does not match the surface.", nameof(image));
        SideStrips.Enqueue(image);
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        CommitStarted.TrySetResult();
        if (CommitGate != null) await CommitGate.Task;
        if (_disconnection != null) throw _disconnection;
        ct.ThrowIfCancellationRequested();
        if (CommitFailure != null) throw CommitFailure;
        Interlocked.Increment(ref _commits);
        AfterCommit?.Invoke();
    }

    public Task ClearAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Clears++;
        return Task.CompletedTask;
    }
    public Task SleepAsync(CancellationToken ct) => Task.CompletedTask;

    public ConcurrentQueue<SurfaceImage> BootImages { get; } = new();
    public Exception? BootImageFailure { get; set; }

    public int BootScreens { get; private set; }

    public Task ShowBootScreenAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_disconnection != null) throw _disconnection;
        BootScreens++;
        return Task.CompletedTask;
    }

    public Task SetBootImageAsync(SurfaceImage image, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_disconnection != null) throw _disconnection;
        if (BootImageFailure != null) throw BootImageFailure;
        BootImages.Enqueue(image);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCalls);
        DisposeStarted.TrySetResult();
        if (DisposeGate != null) await DisposeGate.Task;
    }
}
