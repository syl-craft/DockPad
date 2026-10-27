using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;
using DockPad.Surfaces;

namespace DockPad.Services.StreamDock;

/// <summary>
/// File de rendus sur un thread STA de fond ; l’arrêt refuse les travaux en attente et attend au plus une seconde.
/// </summary>
public sealed class StaDockRenderer : IDockRenderer, IDisposable
{
    private readonly BlockingCollection<RenderWork> _jobs = new();
    private readonly object _gate = new();
    private readonly Thread _thread;
    private readonly Func<DockKey, bool, int, SurfaceImage> _renderKey;
    private readonly Func<SideStripContent, SideStripStyle, SurfaceSize, SurfaceImage> _renderSideStrip;
    private bool _disposed;

    public StaDockRenderer() : this(DockImageRenderer.RenderKey,
        (content, style, size) => SideStripRenderer.Render(content, style, size.Width, size.Height)) { }

    internal StaDockRenderer(Func<DockKey, bool, int, SurfaceImage> renderKey,
        Func<SideStripContent, SideStripStyle, SurfaceSize, SurfaceImage> renderSideStrip)
    {
        _renderKey = renderKey;
        _renderSideStrip = renderSideStrip;
        _thread = new Thread(RenderQueuedImages) { IsBackground = true, Name = "DockPad Stream Dock renderer" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<SurfaceImage> RenderKeyAsync(DockKey key, bool pressed, int size) =>
        Enqueue(() => _renderKey(key, pressed, size));

    public Task<SurfaceImage> RenderSideStripAsync(SideStripContent content, SideStripStyle style, SurfaceSize size) =>
        Enqueue(() => _renderSideStrip(content, style, size));

    private Task<SurfaceImage> Enqueue(Func<SurfaceImage> render)
    {
        lock (_gate)
        {
            if (_disposed)
                return Task.FromException<SurfaceImage>(new ObjectDisposedException(nameof(StaDockRenderer)));

            var completion = new TaskCompletionSource<SurfaceImage>(TaskCreationOptions.RunContinuationsAsynchronously);
            _jobs.Add(new RenderWork(render, completion));
            return completion.Task;
        }
    }

    private void RenderQueuedImages()
    {
        try
        {
            foreach (var work in _jobs.GetConsumingEnumerable())
            {
                if (Volatile.Read(ref _disposed))
                {
                    work.Completion.TrySetException(new ObjectDisposedException(nameof(StaDockRenderer)));
                    continue;
                }

                try { work.Completion.TrySetResult(work.Render()); }
                catch (Exception ex) { work.Completion.TrySetException(ex); }
            }
        }
        finally
        {
            lock (_gate)
                _jobs.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            Volatile.Write(ref _disposed, true);
            _jobs.CompleteAdding();
            while (_jobs.TryTake(out var work))
                work.Completion.TrySetException(new ObjectDisposedException(nameof(StaDockRenderer)));
        }

        // L’attente est bornée : le rendu en cours peut se terminer après le retour de Dispose.
        if (Thread.CurrentThread != _thread)
            _thread.Join(TimeSpan.FromSeconds(1));
    }

    private sealed record RenderWork(Func<SurfaceImage> Render, TaskCompletionSource<SurfaceImage> Completion);
}
