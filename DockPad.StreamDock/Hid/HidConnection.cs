using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace DockPad.StreamDock.Hid;

internal interface IHidConnection : IDisposable
{
    /// <summary>
    /// Lève TimeoutException au-delà du délai d'écriture, que l'E/S native ait rendu la main ou non.
    /// </summary>
    Task WriteAsync(byte[] report, CancellationToken ct);

    /// <summary>
    /// Lève IOException si l'appareil est débranché.
    /// </summary>
    Task<int> ReadAsync(byte[] buffer, CancellationToken ct);
}

/// <summary>
/// Handle HID ouvert en E/S asynchrones. Une écriture dépassée est abandonnée par CancelIoEx ; si le pilote ne
/// l'achève pas, elle reste en suspens jusqu'à Dispose, qui ferme le handle.
/// </summary>
internal sealed class HidConnection : IHidConnection
{
    private readonly Stream _stream;
    private readonly SafeFileHandle? _handle;
    private readonly TimeSpan _writeTimeout;
    private readonly TimeProvider _time;

    internal HidConnection(Stream stream, SafeFileHandle? handle, TimeSpan writeTimeout, TimeProvider time)
    {
        _stream = stream;
        _handle = handle;
        _writeTimeout = writeTimeout;
        _time = time;
    }

    /// <summary>
    /// Lève SurfaceBusyException si une autre application garde l'appareil (refus d'accès ou de partage).
    /// </summary>
    public static HidConnection Open(string devicePath, TimeSpan writeTimeout)
    {
        var handle = HidNative.CreateFile(devicePath, HidNative.GenericRead | HidNative.GenericWrite,
            HidNative.FileShareRead | HidNative.FileShareWrite, securityAttributes: 0, HidNative.OpenExisting,
            HidNative.FileFlagOverlapped, templateFile: 0);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            if (error is HidNative.ErrorAccessDenied or HidNative.ErrorSharingViolation)
                throw new SurfaceBusyException($"HID device is in use by another application (Win32 error {error}).");
            throw new IOException($"Cannot open HID device (Win32 error {error}).");
        }

        try
        {
            var stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);
            return new HidConnection(stream, handle, writeTimeout, TimeProvider.System);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public async Task WriteAsync(byte[] report, CancellationToken ct)
    {
        var write = _stream.WriteAsync(report, ct).AsTask();
        try
        {
            await write.WaitAsync(_writeTimeout, _time, ct);
        }
        catch (TimeoutException)
        {
            Abandon(write);
            throw new TimeoutException($"HID write exceeded {_writeTimeout.TotalMilliseconds} ms.");
        }
        catch (OperationCanceledException) when (write.IsCompleted == false)
        {
            Abandon(write);
            throw;
        }
    }

    public Task<int> ReadAsync(byte[] buffer, CancellationToken ct) => _stream.ReadAsync(buffer, ct).AsTask();

    public void Dispose()
    {
        CancelPendingIo();
        _stream.Dispose();
    }

    /// <summary>
    /// Écriture qu'on n'attend plus : on demande son annulation et son échec éventuel est observé, jamais relevé.
    /// </summary>
    private void Abandon(Task write)
    {
        CancelPendingIo();
        _ = write.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void CancelPendingIo()
    {
        if (_handle == null)
            return;
        try
        {
            HidNative.CancelIoEx(_handle, overlapped: 0);
        }
        catch (ObjectDisposedException)
        {
            // Handle déjà fermé : la fermeture a annulé les E/S en cours.
        }
    }
}
