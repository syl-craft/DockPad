using System.Buffers.Binary;
using System.Text;

namespace DockPad.StreamDock;

/// <summary>
/// Rapports HID du 293S V3 : l'octet 0 est l'id de rapport, toujours 0x00.
/// </summary>
internal static class StreamDockProtocol
{
    private const int CommandPrefixOffset = 1;
    private const int CommandNameOffset = 6;
    private const int InputKeyOffset = 10;
    private const int InputStateOffset = 11;
    private const byte PressedState = 0x01;

    /// <summary>
    /// Touche des accusés d'écriture, sans appui à signaler.
    /// </summary>
    private const byte WriteAcknowledgeKeyId = 0xFF;

    private static readonly byte[] CommandPrefix = Encoding.ASCII.GetBytes("CRT\0\0");
    private static readonly byte[] InputMarker = Encoding.ASCII.GetBytes("ACK");
    private static readonly byte[] SuccessMarker = Encoding.ASCII.GetBytes("OK");
    private const int SuccessOffset = 6;

    /// <summary>
    /// Octet qui suit la longueur dans l'en-tête « LOG », relevé sur le firmware V3.HSV293S.02.009.
    /// </summary>
    private const byte BootImageSlot = 0x01;

    public static byte[] Wake() => Command("DIS");

    public static byte[] KeepAlive() => Command("CONNECT");

    public static byte[] Brightness(int percent)
    {
        var report = Command("LIG");
        report[11] = (byte)Math.Clamp(percent, 0, 100);
        return report;
    }

    public static byte[] Clear(byte keyId)
    {
        var report = Command("CLE");
        report[12] = keyId;
        return report;
    }

    public static byte[] Commit() => Command("STP");

    public static byte[] Sleep() => Command("HAN");

    public static byte[] Disconnect()
    {
        var report = Command("CLE");
        report[11] = (byte)'D';
        report[12] = (byte)'C';
        return report;
    }

    /// <summary>
    /// En-tête « BAT » (longueur gros-boutiste aux octets 9 à 12, touche à l'octet 13) puis un rapport par bloc de 1024 octets.
    /// </summary>
    public static IReadOnlyList<byte[]> Image(byte keyId, byte[] jpeg) => Blob("BAT", keyId, jpeg);

    /// <summary>
    /// Image de démarrage 854 × 480 : en-tête « LOG » comme « BAT », suivie de blocs de 1024 octets.
    /// Le dock l'écrit dans sa mémoire permanente.
    /// </summary>
    public static IReadOnlyList<byte[]> BootImage(byte[] jpeg) => Blob("LOG", BootImageSlot, jpeg);

    private static IReadOnlyList<byte[]> Blob(string command, byte target, byte[] jpeg)
    {
        var header = Command(command);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(9, 4), jpeg.Length);
        header[13] = target;

        var reports = new List<byte[]> { header };
        for (var offset = 0; offset < jpeg.Length; offset += StreamDockModel.ChunkLength)
        {
            var chunkLength = Math.Min(StreamDockModel.ChunkLength, jpeg.Length - offset);
            var chunk = new byte[StreamDockModel.OutputReportLength];
            jpeg.AsSpan(offset, chunkLength).CopyTo(chunk.AsSpan(1));
            reports.Add(chunk);
        }
        return reports;
    }

    public static KeyEvent? ParseInput(ReadOnlySpan<byte> report)
    {
        if (report.Length <= InputStateOffset)
            return null;
        if (report.Slice(1, InputMarker.Length).SequenceEqual(InputMarker) == false)
            return null;

        var keyId = report[InputKeyOffset];
        if (keyId == WriteAcknowledgeKeyId)
            return null;

        var key = StreamDockModel.KeyOf(keyId);
        if (key == null)
            return null;

        var state = report[InputStateOffset] == PressedState ? KeyState.Pressed : KeyState.Released;
        return new KeyEvent(key.Value, state);
    }

    /// <summary>
    /// « ACK » suivi de « OK » sans touche : le dock confirme une écriture.
    /// </summary>
    public static bool IsWriteAcknowledgement(ReadOnlySpan<byte> report)
    {
        if (report.Length <= InputStateOffset)
            return false;
        if (report.Slice(1, InputMarker.Length).SequenceEqual(InputMarker) == false)
            return false;
        if (report.Slice(SuccessOffset, SuccessMarker.Length).SequenceEqual(SuccessMarker) == false)
            return false;
        return StreamDockModel.KeyOf(report[InputKeyOffset]) == null;
    }

    private static byte[] Command(string name)
    {
        var report = new byte[StreamDockModel.OutputReportLength];
        CommandPrefix.CopyTo(report, CommandPrefixOffset);
        Encoding.ASCII.GetBytes(name).CopyTo(report, CommandNameOffset);
        return report;
    }
}
