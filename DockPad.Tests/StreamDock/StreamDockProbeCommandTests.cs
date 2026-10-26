using DockPad.StreamDock;
using DockPad.Surfaces;

namespace DockPad.Tests;

/// <summary>
/// Effacement ciblé : numéro matériel, absence de validation implicite, réveil et annulation.
/// </summary>
public class StreamDockProbeCommandTests
{
    [Theory]
    [InlineData(0, 0, 13)]
    [InlineData(1, 2, 8)]
    [InlineData(2, 4, 3)]
    public async Task EffacementCible_EnvoieCleSansValider(int row, int column, byte keyId)
    {
        var connection = new FakeHidConnection();
        await using var surface = new StreamDockSurface(connection, new ManualTimeProvider());
        await surface.StartAsync(CancellationToken.None);

        await surface.SendCommandAsync(StreamDockProtocol.Clear(StreamDockModel.KeyId(new SurfaceKey(row, column))), CancellationToken.None);

        var report = Assert.Single(connection.Written.Skip(4));
        Assert.Equal(StreamDockProtocol.Clear(keyId), report);

        await surface.CommitAsync(CancellationToken.None);

        Assert.Equal(StreamDockProtocol.Commit(), connection.Written[^1]);
    }

    [Fact]
    public async Task Reveil_EnvoieDisSansValider()
    {
        var connection = new FakeHidConnection();
        await using var surface = new StreamDockSurface(connection, new ManualTimeProvider());
        await surface.StartAsync(CancellationToken.None);

        await surface.SendCommandAsync(StreamDockProtocol.Wake(), CancellationToken.None);

        Assert.Equal(StreamDockProtocol.Wake(), Assert.Single(connection.Written.Skip(4)));
    }

    [Fact]
    public async Task EffacementAnnule_NEcritPasDeRapport()
    {
        var connection = new FakeHidConnection();
        await using var surface = new StreamDockSurface(connection, new ManualTimeProvider());
        await surface.StartAsync(CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => surface.SendCommandAsync(StreamDockProtocol.Clear(StreamDockModel.KeyId(new SurfaceKey(0, 0))), cancelled.Token));

        Assert.Equal(4, connection.Written.Count);
    }
}
