using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;

namespace DockPad.Services.Usage;

/// <summary>
/// Ne rend que des lignes terminées : la position reste en octets, même si une écriture coupe un caractère UTF-8.
/// </summary>
internal static class JsonlIncrementalReader
{
    internal static long Read(string file, long position, long length, Action<string> consume, CancellationToken token)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Position = position;
        using var pending = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        var completePosition = position;
        try
        {
            // La borne est celle du relevé de métadonnées : une écriture concurrente attendra le prochain tick.
            while (position < length)
            {
                token.ThrowIfCancellationRequested();
                var count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, length - position));
                if (count == 0) break;
                var start = 0;
                for (var i = 0; i < count; i++)
                {
                    if (buffer[i] != (byte)'\n') continue;
                    token.ThrowIfCancellationRequested();
                    pending.Write(buffer, start, i - start);
                    var bytes = pending.GetBuffer().AsSpan(0, (int)pending.Length);
                    if (bytes.Length > 0 && bytes[^1] == (byte)'\r') bytes = bytes[..^1];
                    // StreamReader acceptait le BOM des journaux existants, uniquement en tête de fichier.
                    if (completePosition == 0 && bytes.StartsWith(Encoding.UTF8.Preamble))
                        bytes = bytes[Encoding.UTF8.Preamble.Length..];
                    consume(Encoding.UTF8.GetString(bytes));
                    completePosition = position + i + 1;
                    pending.SetLength(0);
                    start = i + 1;
                }
                pending.Write(buffer, start, count - start);
                position += count;
            }
            token.ThrowIfCancellationRequested();
            return completePosition;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
