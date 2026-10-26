using System.IO;
using DockPad.StreamDock.Hid;

namespace DockPad.Tests;

/// <summary>
/// Écriture HID bornée sur un flux dont l'E/S ne rend jamais la main, même annulée : TimeoutException au délai,
/// pas avant, et la fermeture libère le flux.
/// </summary>
public class StreamDockHidConnectionTests
{
    [Fact]
    public async Task EcritureSansFin_LeveTimeoutAuDelai()
    {
        var stream = new NeverEndingStream();
        var time = new ManualTimeProvider();
        using var connection = new HidConnection(stream, handle: null, TimeSpan.FromMilliseconds(500), time);

        var write = connection.WriteAsync(new byte[1025], CancellationToken.None);
        time.Advance(TimeSpan.FromMilliseconds(499));
        await Task.Delay(50);
        Assert.False(write.IsCompleted, "Le délai n'est pas encore écoulé.");
        time.Advance(TimeSpan.FromMilliseconds(1));

        await Assert.ThrowsAsync<TimeoutException>(() => write.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Fermeture_LibereLeFlux()
    {
        var stream = new NeverEndingStream();
        var connection = new HidConnection(stream, handle: null, TimeSpan.FromMilliseconds(500), new ManualTimeProvider());

        connection.Dispose();

        Assert.True(stream.IsDisposed);
    }

    /// <summary>
    /// Écriture qui ignore son jeton et ne se termine jamais, comme un pilote qui n'achève pas l'annulation.
    /// </summary>
    private sealed class NeverEndingStream : Stream
    {
        private readonly TaskCompletionSource _never = new();

        public bool IsDisposed { get; private set; }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => new(_never.Task);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
