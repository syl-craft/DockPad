using System.Text;
using DockPad.StreamDock;
using DockPad.Surfaces;

namespace DockPad.Tests;

/// <summary>
/// Protocole du 293S V3 : numérotation des touches par position et son inverse, écrans latéraux 16 à 18,
/// commandes préfixées par « CRT\0\0 » dans des rapports de 1025 octets complétés de zéros, luminosité à
/// l'octet 11 bornée à 0..100, effacement à l'octet 12, déconnexion « CLE\0\0DC », image en en-tête « BAT »
/// suivi de blocs de 1024 octets, et décodage des entrées « ACK » : appui, relâchement, accusé et inconnues ignorés.
/// </summary>
public class StreamDockProtocolTests
{
    [Theory]
    [InlineData(0, 0, 13)]
    [InlineData(0, 4, 1)]
    [InlineData(1, 0, 14)]
    [InlineData(1, 2, 8)]
    [InlineData(2, 4, 3)]
    [InlineData(2, 0, 15)]
    public void KeyId_SuitLaTableDuModele(int row, int column, byte keyId)
    {
        Assert.Equal(keyId, StreamDockModel.KeyId(new SurfaceKey(row, column)));
        Assert.Equal(new SurfaceKey(row, column), StreamDockModel.KeyOf(keyId));
    }

    [Fact]
    public void KeyId_LesQuinzePositionsDeLaSpec()
    {
        byte[][] expected = [[13, 10, 7, 4, 1], [14, 11, 8, 5, 2], [15, 12, 9, 6, 3]];

        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 5; column++)
            {
                Assert.Equal(expected[row][column], StreamDockModel.KeyId(new SurfaceKey(row, column)));
            }
        }
    }

    [Fact]
    public void KeyOf_InverseDeKeyIdPourLesQuinzeTouches()
    {
        for (byte keyId = 1; keyId <= 15; keyId++)
        {
            var key = StreamDockModel.KeyOf(keyId) ?? throw new InvalidOperationException($"Touche {keyId} absente.");
            Assert.Equal(keyId, StreamDockModel.KeyId(key));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(0xFF)]
    public void KeyOf_HorsDesTouches_Null(byte keyId)
        => Assert.Null(StreamDockModel.KeyOf(keyId));

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(3, 0)]
    [InlineData(0, 5)]
    [InlineData(0, -1)]
    public void KeyId_HorsDeLaGrille_Refuse(int row, int column)
        => Assert.Throws<ArgumentOutOfRangeException>(() => StreamDockModel.KeyId(new SurfaceKey(row, column)));

    [Fact]
    public void DisplayId_EcransDe16A18()
    {
        Assert.Equal(16, StreamDockModel.DisplayId(0));
        Assert.Equal(17, StreamDockModel.DisplayId(1));
        Assert.Equal(18, StreamDockModel.DisplayId(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => StreamDockModel.DisplayId(3));
    }

    [Fact]
    public void Commande_CommenceParCrtEtLeNom()
    {
        var report = StreamDockProtocol.Wake();

        Assert.Equal(1025, report.Length);
        Assert.Equal(0, report[0]);
        Assert.Equal("CRT\0\0", Ascii(report[1..6]));
        Assert.Equal("DIS", Ascii(report[6..9]));
        Assert.All(report[9..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void Commandes_NomAPartirDeLOctet6()
    {
        Assert.Equal("CONNECT", Ascii(StreamDockProtocol.KeepAlive()[6..13]));
        Assert.Equal("STP", Ascii(StreamDockProtocol.Commit()[6..9]));
        Assert.Equal("HAN", Ascii(StreamDockProtocol.Sleep()[6..9]));
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(42, 42)]
    [InlineData(150, 100)]
    [InlineData(-5, 0)]
    public void Luminosite_PourcentageALOctet11(int percent, byte expected)
    {
        var report = StreamDockProtocol.Brightness(percent);

        Assert.Equal(1025, report.Length);
        Assert.Equal("LIG", Ascii(report[6..9]));
        Assert.Equal(0, report[9]);
        Assert.Equal(0, report[10]);
        Assert.Equal(expected, report[11]);
    }

    [Fact]
    public void Effacement_ToucheALOctet12()
    {
        Assert.Equal(0xFF, StreamDockProtocol.Clear(0xFF)[12]);

        var report = StreamDockProtocol.Clear(13);
        Assert.Equal("CLE", Ascii(report[6..9]));
        Assert.Equal(new byte[] { 0, 0, 0 }, report[9..12]);
        Assert.Equal(13, report[12]);
    }

    [Fact]
    public void Deconnexion_CleSuiviDeDc()
    {
        var report = StreamDockProtocol.Disconnect();

        Assert.Equal(1025, report.Length);
        Assert.Equal("CLE\0\0DC", Ascii(report[6..13]));
        Assert.All(report[13..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void Image_EnTeteBatPuisBlocsDe1024()
    {
        var jpeg = Enumerable.Range(0, 2500).Select(index => (byte)(index % 251 + 1)).ToArray();

        var reports = StreamDockProtocol.Image(keyId: 7, jpeg);

        Assert.Equal(4, reports.Count);
        Assert.All(reports, report => Assert.Equal(1025, report.Length));
        var header = reports[0];
        Assert.Equal("CRT\0\0", Ascii(header[1..6]));
        Assert.Equal("BAT", Ascii(header[6..9]));
        Assert.Equal(new byte[] { 0x00, 0x00, 0x09, 0xC4 }, header[9..13]);
        Assert.Equal(7, header[13]);
        Assert.All(reports.Skip(1), chunk => Assert.Equal(0, chunk[0]));
        Assert.Equal(jpeg[..1024], reports[1][1..]);
        Assert.Equal(jpeg[1024..2048], reports[2][1..]);
        Assert.Equal(jpeg[2048..], reports[3][1..453]);
        Assert.All(reports[3][453..], value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData(1, KeyState.Pressed)]
    [InlineData(0, KeyState.Released)]
    [InlineData(2, KeyState.Released)]
    public void Entree_AppuiEtRelachement(byte state, KeyState expected)
    {
        var report = InputReport(keyId: 13, state);

        Assert.Equal(new KeyEvent(new SurfaceKey(0, 0), expected), StreamDockProtocol.ParseInput(report));
    }

    [Fact]
    public void Entree_AccuseOuInconnue_Ignoree()
    {
        Assert.Null(StreamDockProtocol.ParseInput(InputReport(keyId: 0xFF, state: 1)));
        Assert.Null(StreamDockProtocol.ParseInput(InputReport(keyId: 16, state: 1)));

        var withoutAck = InputReport(keyId: 13, state: 1);
        withoutAck[1] = (byte)'X';
        Assert.Null(StreamDockProtocol.ParseInput(withoutAck));
    }

    [Fact]
    public void Entree_RapportTropCourt_Ignore()
        => Assert.Null(StreamDockProtocol.ParseInput(new byte[] { 0, (byte)'A', (byte)'C', (byte)'K' }));

    private static byte[] InputReport(byte keyId, byte state)
    {
        var report = new byte[513];
        Encoding.ASCII.GetBytes("ACK").CopyTo(report, 1);
        report[10] = keyId;
        report[11] = state;
        return report;
    }

    private static string Ascii(byte[] bytes) => Encoding.ASCII.GetString(bytes);
}
