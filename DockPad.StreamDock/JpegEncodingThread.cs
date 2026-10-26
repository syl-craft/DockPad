using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace DockPad.StreamDock;

/// <summary>
/// Fil STA unique, en arrière-plan, qui encode les JPEG du dock : l'imagerie WPF y crée son Dispatcher une seule
/// fois, au lieu d'en laisser un sur chaque thread du pool qui encode.
/// </summary>
internal static class JpegEncodingThread
{
    private static readonly BlockingCollection<Action> Pending = [];
    private static readonly Lazy<Thread> Worker = new(StartWorker);

    /// <summary>
    /// Un encodage annulé avant son tour n'est jamais commencé.
    /// </summary>
    public static Task<byte[]> EncodeAsync(SurfaceImage image, int size, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<byte[]>(ct);

        _ = Worker.Value;
        var encoded = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        Pending.Add(() =>
        {
            if (ct.IsCancellationRequested)
            {
                encoded.TrySetCanceled(ct);
                return;
            }
            try
            {
                encoded.TrySetResult(StreamDockImage.ToJpeg(image, size));
            }
            catch (Exception ex)
            {
                encoded.TrySetException(ex);
            }
        });
        return encoded.Task.WaitAsync(ct);
    }

    private static Thread StartWorker()
    {
        var thread = new Thread(() =>
        {
            foreach (var encode in Pending.GetConsumingEnumerable())
            {
                encode();
            }
        })
        {
            IsBackground = true,
            Name = "StreamDock JPEG",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    }
}
