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
        => EncodeAsync(() => StreamDockImage.ToJpeg(image, size), ct);

    public static Task<byte[]?> EncodeAsync(SurfaceImage image, CancellationToken ct)
        => EncodeAsync(() => StreamDockImage.ToJpeg(image), ct);

    public static Task<byte[]> EncodeBootImageAsync(SurfaceImage image, CancellationToken ct)
        => EncodeAsync(() => StreamDockImage.ToBootJpeg(image), ct);

    private static Task<T> EncodeAsync<T>(Func<T> encode, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<T>(ct);

        _ = Worker.Value;
        var encoded = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Pending.Add(() =>
        {
            if (ct.IsCancellationRequested)
            {
                encoded.TrySetCanceled(ct);
                return;
            }
            try
            {
                encoded.TrySetResult(encode());
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
