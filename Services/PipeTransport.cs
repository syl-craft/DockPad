using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DockPad.Services;

/// <summary>Lecture UTF-8 et écriture de lignes annulable sur un pipe.</summary>
internal static class PipeTransport
{
    public static StreamReader Reader(PipeStream pipe) => new(pipe, Encoding.UTF8,
        detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);

    public static async Task WriteLineAsync(PipeStream pipe, string line, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        await pipe.WriteAsync(bytes, token).ConfigureAwait(false);
    }
}
