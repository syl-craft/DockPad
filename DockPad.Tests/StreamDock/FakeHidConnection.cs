using System.IO;
using System.Threading.Channels;
using DockPad.StreamDock.Hid;

namespace DockPad.Tests;

/// <summary>
/// Faux handle HID : garde les rapports écrits, rend les rapports mis en file, et peut suspendre, bloquer ou faire
/// échouer les écritures à la demande. Une écriture suspendue ou bloquée ignore son jeton, comme un pilote USB qui ne
/// rend plus la main ; Dispose la fait échouer, et peut lui-même bloquer jusqu'à ReleaseDispose.
/// </summary>
internal sealed class FakeHidConnection : IHidConnection
{
    private readonly object _gate = new();
    private readonly List<byte[]> _written = [];
    private readonly Channel<byte[]?> _reads = Channel.CreateUnbounded<byte[]?>();
    private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim _disposeReleased = new(initialState: true);
    private TaskCompletionSource _resumed = CompletedGate();
    private bool _blocksWrites;
    private Exception? _writeFailure;
    private Exception? _readFailure;

    public bool IsDisposed { get; private set; }

    public IReadOnlyList<byte[]> Written
    {
        get
        {
            lock (_gate)
                return [.. _written];
        }
    }

    public void QueueInput(byte[] report) => _reads.Writer.TryWrite(report);

    /// <summary>
    /// La lecture en cours et les suivantes lèvent failure.
    /// </summary>
    public void FailReads(Exception failure)
    {
        _readFailure = failure;
        _reads.Writer.TryWrite(null);
    }

    public void FailWrites(Exception failure) => _writeFailure = failure;

    /// <summary>
    /// Les écritures attendent sans fin ; seule la fermeture les débloque, en échec.
    /// </summary>
    public void BlockWrites() => _blocksWrites = true;

    public void PauseWrites()
    {
        lock (_gate)
            _resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void ResumeWrites()
    {
        lock (_gate)
            _resumed.TrySetResult();
    }

    public void BlockDispose() => _disposeReleased.Reset();

    public void ReleaseDispose() => _disposeReleased.Set();

    public async Task WriteAsync(byte[] report, CancellationToken ct)
    {
        await Task.Yield();
        if (_writeFailure != null)
            throw _writeFailure;
        if (_blocksWrites)
        {
            await _disposed.Task;
            throw new ObjectDisposedException(nameof(FakeHidConnection));
        }

        Task resumed;
        lock (_gate)
            resumed = _resumed.Task;
        await Task.WhenAny(resumed, _disposed.Task);
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(FakeHidConnection));

        lock (_gate)
            _written.Add([.. report]);
    }

    public async Task<int> ReadAsync(byte[] buffer, CancellationToken ct)
    {
        var report = await _reads.Reader.ReadAsync(ct);
        if (report == null)
        {
            _reads.Writer.TryWrite(null);
            throw _readFailure ?? new IOException("Read failure.");
        }

        report.CopyTo(buffer, 0);
        return report.Length;
    }

    /// <summary>
    /// Attend que les rapports écrits vérifient condition, cinq secondes au plus.
    /// </summary>
    public async Task WaitUntilWrittenAsync(Func<IReadOnlyList<byte[]>, bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (condition(Written) == false)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Condition not met, {Written.Count} reports written.");
            await Task.Delay(5);
        }
    }

    public void Dispose()
    {
        IsDisposed = true;
        _disposed.TrySetResult();
        _disposeReleased.Wait();
    }

    private static TaskCompletionSource CompletedGate()
    {
        var gate = new TaskCompletionSource();
        gate.SetResult();
        return gate;
    }
}
