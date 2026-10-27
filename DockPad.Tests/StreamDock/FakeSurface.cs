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
    public SurfaceDescription Description { get; } = new("Fake", 3, 5, 4, 3, 4, new SurfaceSize(75, 380));
    public ConcurrentQueue<(SurfaceKey Key, SurfaceImage Image)> Images { get; } = new();
    public ConcurrentQueue<(int Display, SurfaceImage Image)> Displays { get; } = new();
    public ConcurrentQueue<SurfaceImage> SideStrips { get; } = new();
    public int Commits => Volatile.Read(ref _commits);
    public int DisposeCalls => Volatile.Read(ref _disposeCalls);
    public TaskCompletionSource? WriteGate { get; set; }
    public TaskCompletionSource? CommitGate { get; set; }
    public TaskCompletionSource? DisposeGate { get; set; }
    public Exception? WriteFailure { get; set; }
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

    public Task SetDisplayImageAsync(int display, SurfaceImage image, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Displays.Enqueue((display, image));
        return Task.CompletedTask;
    }

    public Task SetSideStripImageAsync(SurfaceImage image, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (image.Width != Description.SideStrip?.Width || image.Height != Description.SideStrip?.Height)
            throw new ArgumentException("Side strip image size does not match the surface.", nameof(image));
        SideStrips.Enqueue(image);
        return Task.CompletedTask;
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        CommitStarted.TrySetResult();
        if (CommitGate != null) await CommitGate.Task;
        if (_disconnection != null) throw _disconnection;
        ct.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _commits);
    }

    public Task ClearAsync(CancellationToken ct) => Task.CompletedTask;
    public Task SleepAsync(CancellationToken ct) => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCalls);
        DisposeStarted.TrySetResult();
        if (DisposeGate != null) await DisposeGate.Task;
    }
}
