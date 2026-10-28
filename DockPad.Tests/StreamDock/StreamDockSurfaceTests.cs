using System.Buffers.Binary;
using System.IO;
using System.Text;
using DockPad.StreamDock;
using DockPad.Surfaces;

namespace DockPad.Tests;

/// <summary>
/// Faux HID : démarrage, images atomiques, dernière demande gagnante, validation STP, maintien en vie et fermeture.
/// Colonne : tranches modifiées, chevauchements, refus JPEG, annulation, effacement et déconnexion pendant l'envoi.
/// Image de démarrage : « LOG » puis attente de l'accusé reçu après l'écriture, taille refusée, délai dépassé.
/// </summary>
public class StreamDockSurfaceTests
{
    private static readonly TimeSpan EventWait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Ouverture_EnvoieLaSequenceDeDemarrage()
    {
        var (connection, _, surface) = await OpenAsync();

        var written = connection.Written;
        Assert.Equal(["DIS", "LIG", "CLE", "STP"], written.Select(CommandOf));
        Assert.Equal(100, written[1][11]);
        Assert.Equal(0xFF, written[2][12]);
        Assert.Equal(new SurfaceDescription("293S V3", 3, 5, 96, new SurfaceSize(75, 380), new SurfaceSize(854, 480)),
            surface.Description);
        await surface.DisposeAsync();
    }

    [Fact]
    public async Task DemarrageEnEchec_FermeLaConnexionEtLeve()
    {
        var connection = new FakeHidConnection();
        var surface = new StreamDockSurface(connection, new ManualTimeProvider());
        var disconnectedRaised = false;
        surface.Disconnected += _ => disconnectedRaised = true;
        connection.FailWrites(new IOException("Device removed."));

        await Assert.ThrowsAsync<IOException>(() => surface.StartAsync(CancellationToken.None));
        await Task.Delay(100);

        Assert.True(connection.IsDisposed);
        Assert.False(disconnectedRaised);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => surface.CommitAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ImageDeTouche_EnvoieBatAvecLeBonNumero()
    {
        var (connection, _, surface) = await OpenAsync();
        var image = Noise(96, seed: 1);
        var expectedJpeg = await OnStaThread(() => StreamDockImage.ToJpeg(image, 96));

        var commitsDuringEncoding = Enumerable.Range(0, 20).Select(_ => Task.Run(() => surface.CommitAsync(CancellationToken.None)));
        await Task.WhenAll([surface.SetKeyImageAsync(new SurfaceKey(0, 0), image, CancellationToken.None), .. commitsDuringEncoding]);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => surface.CommitAsync(CancellationToken.None))));

        var written = connection.Written;
        Assert.Equal(41, written.Where(report => CommandOf(report) == "STP").Count());
        var (keyId, jpeg) = ImageAt(written, written.ToList().FindIndex(report => CommandOf(report) == "BAT"));
        Assert.Equal(13, keyId);
        Assert.True(expectedJpeg.Length > 1024, "L'image doit tenir sur plusieurs blocs.");
        Assert.Equal(expectedJpeg, jpeg);
        await surface.DisposeAsync();
    }

    [Fact]
    public async Task DeuxImagesRapprochees_SeuleLaDerniereEstEcrite()
    {
        var (connection, _, surface) = await OpenAsync();
        var latest = Noise(96, seed: 2);
        var expectedJpeg = await OnStaThread(() => StreamDockImage.ToJpeg(latest, 96));
        connection.PauseWrites();
        var blockingCommit = surface.CommitAsync(CancellationToken.None);
        await Task.Delay(50);

        await surface.SetKeyImageAsync(new SurfaceKey(0, 0), Noise(96, seed: 1), CancellationToken.None);
        await surface.SetKeyImageAsync(new SurfaceKey(0, 0), latest, CancellationToken.None);
        connection.ResumeWrites();
        await blockingCommit.WaitAsync(EventWait);
        await surface.CommitAsync(CancellationToken.None).WaitAsync(EventWait);

        var written = connection.Written;
        var headers = written.Where(report => CommandOf(report) == "BAT").ToList();
        Assert.Single(headers);
        Assert.Equal(expectedJpeg, ImageAt(written, written.ToList().FindIndex(report => CommandOf(report) == "BAT")).Jpeg);
        await surface.DisposeAsync();
    }

    [Fact]
    public async Task ImageAnnulee_NEstPasEcrite()
    {
        var (connection, _, surface) = await OpenAsync();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => surface.SetKeyImageAsync(new SurfaceKey(0, 0), Noise(96, seed: 1), cancelled.Token));
        await surface.CommitAsync(CancellationToken.None);

        var headers = connection.Written.Where(report => CommandOf(report) == "BAT").ToList();
        Assert.Empty(headers);
        await surface.DisposeAsync();
    }

    [Fact]
    public async Task Validation_RendLaMainUneFoisEcrite()
    {
        var (connection, _, surface) = await OpenAsync();
        connection.PauseWrites();
        await surface.SetKeyImageAsync(new SurfaceKey(0, 0), Noise(96, seed: 1), CancellationToken.None);

        var commit = surface.CommitAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.False(commit.IsCompleted, "STP n'est pas écrit tant que l'image précédente bloque.");
        connection.ResumeWrites();
        await commit.WaitAsync(EventWait);

        var commands = connection.Written.Select(CommandOf).ToList();
        Assert.Equal("STP", commands[^1]);
        Assert.True(commands.IndexOf("BAT") < commands.Count - 1);
        await surface.DisposeAsync();
    }

    [Fact]
    public async Task Validation_EchoueALaDeconnexion()
    {
        var (connection, _, surface) = await OpenAsync();
        connection.PauseWrites();
        var commit = surface.CommitAsync(CancellationToken.None);
        await Task.Delay(50);

        connection.FailReads(new IOException("Device removed."));

        await Assert.ThrowsAsync<IOException>(() => commit.WaitAsync(EventWait));
        await surface.DisposeAsync();
    }

    [Fact]
    public async Task MaintienEnVie_Toutes10Secondes()
    {
        var (connection, time, surface) = await OpenAsync();

        foreach (var step in new[] { 10, 10, 5 })
        {
            time.Advance(TimeSpan.FromSeconds(step));
            await surface.CommitAsync(CancellationToken.None);
        }
        await Task.Delay(100);

        Assert.Equal(2, connection.Written.Where(report => CommandOf(report) == "CONNECT").Count());
        await surface.DisposeAsync();
    }

    [Fact]
    public async Task MaintienEnVie_PasseDevantLesImagesEnAttente()
    {
        var (connection, time, surface) = await OpenAsync();
        connection.PauseWrites();
        var blockingCommit = surface.CommitAsync(CancellationToken.None);
        await Task.Delay(50);
        for (var column = 0; column < 3; column++)
        {
            await surface.SetKeyImageAsync(new SurfaceKey(0, column), Noise(96, seed: column), CancellationToken.None);
        }

        time.Advance(TimeSpan.FromSeconds(10));
        connection.ResumeWrites();
        await blockingCommit.WaitAsync(EventWait);
        await surface.CommitAsync(CancellationToken.None).WaitAsync(EventWait);

        var commands = connection.Written.Select(CommandOf).ToList();
        Assert.True(commands.IndexOf("CONNECT") < commands.IndexOf("BAT"), "CONNECT doit précéder les images en attente.");
        await surface.DisposeAsync();
    }

    [Fact]
    public async Task Lecture_LeveKeyChanged()
    {
        var (connection, _, surface) = await OpenAsync();
        var received = new TaskCompletionSource<KeyEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        surface.KeyChanged += keyEvent => received.TrySetResult(keyEvent);

        connection.QueueInput(InputReport(keyId: 0xFF, state: 0x01));
        connection.QueueInput(InputReport(keyId: 13, state: 0x01));

        Assert.Equal(new KeyEvent(new SurfaceKey(0, 0), KeyState.Pressed), await received.Task.WaitAsync(EventWait));
        await surface.DisposeAsync();
    }

    [Fact]
    public async Task AbonneQuiLeve_NArretePasLaLecture()
    {
        var (connection, _, surface) = await OpenAsync();
        var received = new List<KeyEvent>();
        var secondReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        surface.KeyChanged += _ => throw new InvalidOperationException("Subscriber failure.");
        surface.KeyChanged += keyEvent =>
        {
            lock (received)
                received.Add(keyEvent);
            if (received.Count == 2)
                secondReceived.TrySetResult();
        };

        connection.QueueInput(InputReport(keyId: 13, state: 0x01));
        connection.QueueInput(InputReport(keyId: 13, state: 0x00));
        await secondReceived.Task.WaitAsync(EventWait);

        Assert.Equal(KeyState.Released, received[1].State);
        await surface.DisposeAsync();
    }

    [Fact]
    public async Task LectureEnErreur_LeveDisconnectedUneSeuleFois()
    {
        var (connection, _, surface) = await OpenAsync();
        var causes = new List<Exception>();
        surface.Disconnected += cause =>
        {
            lock (causes)
                causes.Add(cause);
        };

        connection.FailWrites(new IOException("Device removed."));
        connection.FailReads(new IOException("Device removed."));
        _ = surface.CommitAsync(CancellationToken.None);
        await WaitUntilAsync(() => causes.Count > 0);
        await Task.Delay(200);

        Assert.Single(causes);
        Assert.IsType<IOException>(causes[0]);
        Assert.True(connection.IsDisposed, "La déconnexion ferme le handle.");
        await surface.DisposeAsync();
    }

    [Fact]
    public async Task AbonneTardif_RecoitLaDeconnexion()
    {
        var (connection, _, surface) = await OpenAsync();
        connection.FailReads(new IOException("Device removed."));
        await WaitUntilAsync(() => connection.IsDisposed);
        var causes = new List<Exception>();

        surface.Disconnected += cause =>
        {
            lock (causes)
                causes.Add(cause);
        };
        await WaitUntilAsync(() => causes.Count > 0);
        await Task.Delay(100);

        Assert.Single(causes);
        await surface.DisposeAsync();
    }

    [Fact]
    public async Task EcritureEnDelai_LeveDisconnected()
    {
        var (connection, _, surface) = await OpenAsync();
        var disconnected = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        surface.Disconnected += cause => disconnected.TrySetResult(cause);

        connection.FailWrites(new TimeoutException("Write exceeded 500 ms."));
        _ = surface.CommitAsync(CancellationToken.None);

        Assert.IsType<TimeoutException>(await disconnected.Task.WaitAsync(EventWait));
        await surface.DisposeAsync();
    }

    [Fact]
    public async Task ApresDeconnexion_LesEnvoisLevent()
    {
        var (connection, _, surface) = await OpenAsync();
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        surface.Disconnected += _ => disconnected.TrySetResult();
        connection.FailReads(new IOException("Device removed."));
        await disconnected.Task.WaitAsync(EventWait);

        await Assert.ThrowsAsync<IOException>(() => surface.CommitAsync(CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(() => surface.SetKeyImageAsync(new SurfaceKey(1, 1), Noise(96, seed: 1), CancellationToken.None));

        Assert.Equal(4, connection.Written.Count);
        await surface.DisposeAsync();
    }

    [Fact]
    public async Task Fermeture_EffaceEtMetEnVeille()
    {
        var (connection, _, surface) = await OpenAsync();
        var disconnectedRaised = false;
        surface.Disconnected += _ => disconnectedRaised = true;

        await surface.DisposeAsync();

        var shutdown = connection.Written.Skip(4).ToList();
        Assert.Equal(["CLE", "STP", "CLE", "HAN"], shutdown.Select(CommandOf));
        Assert.Equal(0xFF, shutdown[0][12]);
        Assert.Equal("DC", Encoding.ASCII.GetString(shutdown[2], 11, 2));
        Assert.True(connection.IsDisposed);
        Assert.False(disconnectedRaised);
    }

    [Fact]
    public async Task EcritureBloquee_DisposeRendLaMainEnUneSeconde()
    {
        var (connection, time, surface) = await OpenAsync();
        var disconnectedRaised = false;
        surface.Disconnected += _ => disconnectedRaised = true;
        connection.BlockWrites();
        var commit = surface.CommitAsync(CancellationToken.None);
        await Task.Delay(50);

        var dispose = surface.DisposeAsync().AsTask();
        await Task.Delay(50);
        Assert.False(dispose.IsCompleted, "La fermeture attend l'écriture en cours jusqu'à la fin du budget.");
        time.Advance(TimeSpan.FromSeconds(1));
        await dispose.WaitAsync(EventWait);
        await WaitUntilAsync(() => connection.IsDisposed);
        await Task.Delay(100);

        Assert.False(disconnectedRaised);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => commit.WaitAsync(EventWait));
    }

    [Fact]
    public async Task FermetureDuHandleBloquee_DisposeRendLaMainEnUneSeconde()
    {
        var (connection, time, surface) = await OpenAsync();
        connection.BlockWrites();
        connection.BlockDispose();
        _ = surface.CommitAsync(CancellationToken.None);
        await Task.Delay(50);

        var dispose = surface.DisposeAsync().AsTask();
        time.Advance(TimeSpan.FromSeconds(1));
        await dispose.WaitAsync(EventWait);

        connection.ReleaseDispose();
    }

    [Fact]
    public async Task DoubleFermeture_AttendLaMemeFin()
    {
        var (connection, time, surface) = await OpenAsync();
        connection.BlockWrites();
        _ = surface.CommitAsync(CancellationToken.None);
        await Task.Delay(50);

        var first = surface.DisposeAsync().AsTask();
        var second = surface.DisposeAsync().AsTask();
        await Task.Delay(50);
        Assert.False(second.IsCompleted, "Le second appel attend la fin du premier.");
        time.Advance(TimeSpan.FromSeconds(1));

        await Task.WhenAll(first, second).WaitAsync(EventWait);
        await WaitUntilAsync(() => connection.IsDisposed);
    }

    [Fact]
    public async Task ApresFermeture_LesEnvoisLevent()
    {
        var (_, _, surface) = await OpenAsync();
        await surface.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => surface.CommitAsync(CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => surface.SetKeyImageAsync(new SurfaceKey(0, 0), Noise(96, seed: 1), CancellationToken.None));
        await surface.DisposeAsync();
    }

    [Fact]
    public async Task Colonne_OrdreDEnvoi16_17_18PuisCommit()
    {
        var (connection, _, surface) = await OpenAsync();
        await using var cleanup = surface;

        await surface.SetSideStripImageAsync(Strip(), CancellationToken.None);
        await surface.CommitAsync(CancellationToken.None);

        Assert.Equal(new byte[] { 16, 17, 18 }, StripTargets(connection));
        Assert.Equal(new[] { "BAT", "BAT", "BAT", "STP" }, connection.Written.Skip(4)
            .Select(CommandOf).Where(command => command != ""));
    }

    [Fact]
    public async Task Colonne_TrancheInchangee_PasRenvoyee()
    {
        var (connection, _, surface) = await OpenAsync();
        await using var cleanup = surface;
        var strip = Strip();
        await surface.SetSideStripImageAsync(strip, CancellationToken.None);
        await surface.CommitAsync(CancellationToken.None);

        await surface.SetSideStripImageAsync(Strip(), CancellationToken.None);
        await surface.CommitAsync(CancellationToken.None);
        Assert.Equal(new byte[] { 16, 17, 18 }, StripTargets(connection));

        strip.Bgra32[(200 * 75 + 10) * 4] = 200;
        await surface.SetSideStripImageAsync(strip, CancellationToken.None);
        await surface.CommitAsync(CancellationToken.None);
        Assert.Equal(new byte[] { 16, 17, 18, 17 }, StripTargets(connection));
    }

    [Theory]
    [InlineData(150, 16, 17)]
    [InlineData(151, 16, 17)]
    [InlineData(300, 17, 18)]
    [InlineData(301, 17, 18)]
    public async Task Colonne_LigneCommuneModifiee_DeuxTranchesRenvoyees(int row, byte first, byte second)
    {
        var (connection, _, surface) = await OpenAsync();
        await using var cleanup = surface;
        var strip = Strip();
        await surface.SetSideStripImageAsync(strip, CancellationToken.None);
        await surface.CommitAsync(CancellationToken.None);

        strip.Bgra32[(row * 75 + 10) * 4] = 200;
        await surface.SetSideStripImageAsync(strip, CancellationToken.None);
        await surface.CommitAsync(CancellationToken.None);

        Assert.Equal(new byte[] { 16, 17, 18, first, second }, StripTargets(connection));
    }

    [Fact]
    public async Task Colonne_ApresEffacement_ToutesLesTranchesRenvoyees()
    {
        var (connection, _, surface) = await OpenAsync();
        await using var cleanup = surface;
        await surface.SetSideStripImageAsync(Strip(), CancellationToken.None);
        await surface.CommitAsync(CancellationToken.None);
        await surface.ClearAsync(CancellationToken.None);

        await surface.SetSideStripImageAsync(Strip(), CancellationToken.None);
        await surface.CommitAsync(CancellationToken.None);

        Assert.Equal(new byte[] { 16, 17, 18, 16, 17, 18 }, StripTargets(connection));
    }

    [Theory]
    [InlineData(80, 380)]
    [InlineData(75, 420)]
    [InlineData(380, 75)]
    public async Task Colonne_MauvaiseTaille_Refusee(int width, int height)
    {
        var (connection, _, surface) = await OpenAsync();
        await using var cleanup = surface;

        await Assert.ThrowsAsync<ArgumentException>(() => surface.SetSideStripImageAsync(
            new SurfaceImage(width, height, new byte[width * height * 4]), CancellationToken.None));

        Assert.Empty(StripTargets(connection));
    }

    [Fact]
    public async Task Colonne_AvantCommit_LesTranchesNeSontPasAffichees()
    {
        var (connection, _, surface) = await OpenAsync();
        await using var cleanup = surface;
        await surface.SetSideStripImageAsync(Strip(), CancellationToken.None);
        await surface.SendCommandAsync(StreamDockProtocol.Wake(), CancellationToken.None);

        await surface.SetSideStripImageAsync(Strip(), CancellationToken.None);
        await surface.CommitAsync(CancellationToken.None);

        Assert.Equal(new byte[] { 16, 17, 18, 16, 17, 18 }, StripTargets(connection));
    }

    [Fact]
    public async Task Colonne_DeuxDemandesEnAttente_SeuleLaDerniereEstEcrite()
    {
        var (connection, _, surface) = await OpenAsync();
        await using var cleanup = surface;
        connection.PauseWrites();
        var blocker = surface.CommitAsync(CancellationToken.None);
        var latest = Strip();
        latest.Bgra32[0] = 200;
        try
        {
            await surface.SetSideStripImageAsync(Strip(), CancellationToken.None);
            await surface.SetSideStripImageAsync(latest, CancellationToken.None);
        }
        finally
        {
            connection.ResumeWrites();
        }
        await blocker.WaitAsync(EventWait);
        await surface.CommitAsync(CancellationToken.None);

        Assert.Equal(new byte[] { 16, 17, 18 }, StripTargets(connection));
        var written = connection.Written;
        var expected = await OnStaThread(() => StreamDockImage.ToJpeg(StreamDockStrip.Slice(latest)[0].Image));
        Assert.Equal(expected, ImageAt(written, written.ToList().FindIndex(report => CommandOf(report) == "BAT")).Jpeg);
    }

    [Fact]
    public async Task Colonne_RetourALImageAffichee_RetireLaTrancheEnAttente()
    {
        var (connection, _, surface) = await OpenAsync();
        await using var cleanup = surface;
        await surface.SetSideStripImageAsync(Strip(), CancellationToken.None);
        await surface.CommitAsync(CancellationToken.None);
        connection.PauseWrites();
        var blocker = surface.CommitAsync(CancellationToken.None);
        var changed = Strip();
        changed.Bgra32[0] = 200;
        try
        {
            await surface.SetSideStripImageAsync(changed, CancellationToken.None);
            await surface.SetSideStripImageAsync(Strip(), CancellationToken.None);
        }
        finally
        {
            connection.ResumeWrites();
        }
        await blocker.WaitAsync(EventWait);
        await surface.CommitAsync(CancellationToken.None);

        Assert.Equal(new byte[] { 16, 17, 18 }, StripTargets(connection));
    }

    [Fact]
    public async Task Colonne_RetourALImageAffichee_RestaureLaTrancheDejaEcrite()
    {
        var (connection, _, surface) = await OpenAsync();
        await using var cleanup = surface;
        await surface.SetSideStripImageAsync(Strip(), CancellationToken.None);
        await surface.CommitAsync(CancellationToken.None);
        var changed = Strip();
        changed.Bgra32[0] = 200;
        await surface.SetSideStripImageAsync(changed, CancellationToken.None);
        await surface.SendCommandAsync(StreamDockProtocol.Wake(), CancellationToken.None);

        await surface.SetSideStripImageAsync(Strip(), CancellationToken.None);
        await surface.CommitAsync(CancellationToken.None);

        Assert.Equal(new byte[] { 16, 17, 18, 16, 16 }, StripTargets(connection));
    }

    [Fact]
    public async Task Colonne_TrancheTropLourde_NonEnvoyeeEtRetentee()
    {
        var connection = new FakeHidConnection();
        var encodings = 0;
        await using var surface = new StreamDockSurface(connection, new ManualTimeProvider(), (image, ct) =>
            Interlocked.Increment(ref encodings) == 1
                ? Task.FromResult<byte[]?>(null)
                : JpegEncodingThread.EncodeAsync(image, ct));
        await surface.StartAsync(CancellationToken.None);

        await surface.SetSideStripImageAsync(Strip(), CancellationToken.None);
        await surface.CommitAsync(CancellationToken.None);
        Assert.Equal(new byte[] { 17, 18 }, StripTargets(connection));

        await surface.SetSideStripImageAsync(Strip(), CancellationToken.None);
        await surface.CommitAsync(CancellationToken.None);
        Assert.Equal(new byte[] { 17, 18, 16 }, StripTargets(connection));
    }

    [Fact]
    public async Task Colonne_EncodageDepasse_NeRemplacePasLaDerniereDemande()
    {
        var connection = new FakeHidConnection();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var encodings = 0;
        await using var surface = new StreamDockSurface(connection, new ManualTimeProvider(), async (image, ct) =>
        {
            if (Interlocked.Increment(ref encodings) == 1)
            {
                started.TrySetResult();
                await resume.Task.WaitAsync(EventWait);
            }
            return await JpegEncodingThread.EncodeAsync(image, ct);
        });
        await surface.StartAsync(CancellationToken.None);
        var obsolete = surface.SetSideStripImageAsync(Strip(), CancellationToken.None);
        await started.Task.WaitAsync(EventWait);
        var latest = Strip();
        latest.Bgra32[0] = 200;
        try
        {
            await surface.SetSideStripImageAsync(latest, CancellationToken.None);
            await surface.CommitAsync(CancellationToken.None);
        }
        finally
        {
            resume.TrySetResult();
        }
        await obsolete.WaitAsync(EventWait);
        await surface.SetSideStripImageAsync(latest, CancellationToken.None);
        await surface.CommitAsync(CancellationToken.None);

        Assert.Equal(new byte[] { 16, 17, 18 }, StripTargets(connection));
    }

    [Fact]
    public async Task Colonne_EffacementPendantEncodage_AppelAnnule()
    {
        var connection = new FakeHidConnection();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var encodings = 0;
        await using var surface = new StreamDockSurface(connection, new ManualTimeProvider(), async (image, ct) =>
        {
            if (Interlocked.Increment(ref encodings) == 1)
            {
                started.TrySetResult();
                await resume.Task.WaitAsync(EventWait);
            }
            return await JpegEncodingThread.EncodeAsync(image, ct);
        });
        await surface.StartAsync(CancellationToken.None);
        var obsolete = surface.SetSideStripImageAsync(Strip(), CancellationToken.None);
        await started.Task.WaitAsync(EventWait);
        try
        {
            await surface.ClearAsync(CancellationToken.None);
        }
        finally
        {
            resume.TrySetResult();
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => obsolete.WaitAsync(EventWait));
        await surface.CommitAsync(CancellationToken.None);
        Assert.Empty(StripTargets(connection));

        await surface.SetSideStripImageAsync(Strip(), CancellationToken.None);
        await surface.CommitAsync(CancellationToken.None);
        Assert.Equal(new byte[] { 16, 17, 18 }, StripTargets(connection));
    }

    [Fact]
    public async Task Colonne_CommitBloque_NeMarquePasLesTranchesAffichees()
    {
        var (connection, _, surface) = await OpenAsync();
        await using var cleanup = surface;
        await surface.SetSideStripImageAsync(Strip(), CancellationToken.None);
        await surface.SendCommandAsync(StreamDockProtocol.Wake(), CancellationToken.None);
        connection.PauseWrites();
        var commit = surface.CommitAsync(CancellationToken.None);
        try
        {
            await surface.SetSideStripImageAsync(Strip(), CancellationToken.None);
            Assert.False(commit.IsCompleted);
        }
        finally
        {
            connection.ResumeWrites();
        }
        await commit.WaitAsync(EventWait);
        await surface.CommitAsync(CancellationToken.None);

        Assert.Equal(new byte[] { 16, 17, 18, 16, 17, 18 }, StripTargets(connection));
    }

    [Fact]
    public async Task Colonne_DeconnexionPendantEnvoi_ReconnexionRenvoieTout()
    {
        var (connection, _, surface) = await OpenAsync();
        await using var cleanup = surface;
        connection.PauseWrites();
        await surface.SetSideStripImageAsync(Strip(), CancellationToken.None);
        var commit = surface.CommitAsync(CancellationToken.None);
        connection.FailReads(new IOException("Device removed."));
        await Assert.ThrowsAsync<IOException>(() => commit.WaitAsync(EventWait));

        var (reconnected, _, reopened) = await OpenAsync();
        await using var reopenedCleanup = reopened;
        await reopened.SetSideStripImageAsync(Strip(), CancellationToken.None);
        await reopened.CommitAsync(CancellationToken.None);

        Assert.Equal(new byte[] { 16, 17, 18 }, StripTargets(reconnected));
    }

    [Fact]
    public async Task Colonne_DemandeAnnulee_AucuneTrancheEnvoyee()
    {
        var (connection, _, surface) = await OpenAsync();
        await using var cleanup = surface;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => surface.SetSideStripImageAsync(Strip(), cancelled.Token));
        await surface.CommitAsync(CancellationToken.None);

        Assert.Empty(StripTargets(connection));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Colonne_DemandeAnnuleePendantEncodage_PrecedenteConservee(bool observesCancellation)
    {
        var connection = new FakeHidConnection();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var encodings = 0;
        await using var surface = new StreamDockSurface(connection, new ManualTimeProvider(), async (image, ct) =>
        {
            if (Interlocked.Increment(ref encodings) == 4)
            {
                started.TrySetResult();
                await resume.Task.WaitAsync(EventWait);
            }
            return await JpegEncodingThread.EncodeAsync(image, observesCancellation ? ct : CancellationToken.None);
        });
        await surface.StartAsync(CancellationToken.None);
        connection.PauseWrites();
        var blocker = surface.CommitAsync(CancellationToken.None);
        var previous = Strip();
        using var cancelled = new CancellationTokenSource();
        try
        {
            await surface.SetSideStripImageAsync(previous, CancellationToken.None);
            var latest = Strip();
            latest.Bgra32[0] = 200;
            var pending = surface.SetSideStripImageAsync(latest, cancelled.Token);
            await started.Task.WaitAsync(EventWait);
            cancelled.Cancel();
            resume.TrySetResult();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(EventWait));
        }
        finally
        {
            resume.TrySetResult();
            connection.ResumeWrites();
        }
        await blocker.WaitAsync(EventWait);
        await surface.CommitAsync(CancellationToken.None);

        Assert.Equal(new byte[] { 16, 17, 18 }, StripTargets(connection));
        var written = connection.Written;
        var headers = written.Select((report, index) => (Report: report, Index: index))
            .Where(entry => CommandOf(entry.Report) == "BAT").ToArray();
        var slices = StreamDockStrip.Slice(previous);
        for (var index = 0; index < slices.Count; index++)
        {
            var expected = await OnStaThread(() => StreamDockImage.ToJpeg(slices[index].Image));
            Assert.Equal(expected, ImageAt(written, headers[index].Index).Jpeg);
        }
    }

    [Theory]
    [InlineData("BAT")]
    [InlineData("STP")]
    public async Task Colonne_FermetureDeFile_RienTenuPourAffiche(string pausedCommand)
    {
        var (connection, _, surface) = await OpenAsync();
        await using var cleanup = surface;
        await surface.SetSideStripImageAsync(Strip(), CancellationToken.None);
        await surface.CommitAsync(CancellationToken.None);
        Assert.Equal(3, surface.DisplayedStripCount);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.WriteStarting = report =>
        {
            if (CommandOf(report) != pausedCommand)
                return;
            connection.WriteStarting = null;
            connection.PauseWrites();
            started.TrySetResult();
        };
        var changed = Strip();
        changed.Bgra32[0] = 200;
        Task? commit = null;
        Task closing;
        try
        {
            await surface.SetSideStripImageAsync(changed, CancellationToken.None);
            if (pausedCommand == "STP")
                commit = surface.CommitAsync(CancellationToken.None);
            await started.Task.WaitAsync(EventWait);
            closing = surface.DisposeAsync().AsTask();
            Assert.Equal(0, surface.DisplayedStripCount);
            Assert.Equal(0, surface.WrittenStripCount);
        }
        finally
        {
            connection.WriteStarting = null;
            connection.ResumeWrites();
        }
        await closing.WaitAsync(EventWait);
        if (commit != null)
            await commit.WaitAsync(EventWait);

        Assert.Equal(0, surface.DisplayedStripCount);
        Assert.Equal(0, surface.WrittenStripCount);
        Assert.True(connection.IsDisposed);
    }

    private static byte[] StripTargets(FakeHidConnection connection)
        => connection.Written.Where(report => CommandOf(report) == "BAT").Select(report => report[13]).ToArray();

    private static SurfaceImage Strip()
    {
        var pixels = new byte[75 * 380 * 4];
        for (var offset = 3; offset < pixels.Length; offset += 4)
            pixels[offset] = 255;
        return new SurfaceImage(75, 380, pixels);
    }

    [Fact]
    public async Task ImageDeDemarrage_EnvoieLogPuisRendLaMainALAccuse()
    {
        var (connection, _, surface) = await OpenAsync();
        var image = Gradient(854, 480);
        var expectedJpeg = await OnStaThread(() => StreamDockImage.ToBootJpeg(image));

        var sending = surface.SetBootImageAsync(image, CancellationToken.None);
        await connection.WaitUntilWrittenAsync(written => written.Where(report => CommandOf(report) == "LOG").Any()
            && written.Count >= 5 + (expectedJpeg.Length + 1023) / 1024);
        await Task.Delay(50);
        Assert.False(sending.IsCompleted);
        connection.QueueInput(WriteAcknowledgement());
        await sending.WaitAsync(EventWait);

        var written = connection.Written;
        var header = written.ToList().FindIndex(report => CommandOf(report) == "LOG");
        var (slot, jpeg) = ImageAt(written, header);
        Assert.Equal(1, slot);
        Assert.Equal(expectedJpeg, jpeg);

        // Indépendamment de l'encodeur : JPEG baseline, tourné d'un quart de tour antihoraire (la gauche rouge en bas).
        Assert.Contains(new byte[] { 0xFF, 0xC0 }, Pairs(jpeg));
        Assert.DoesNotContain(new byte[] { 0xFF, 0xC2 }, Pairs(jpeg));
        var (width, height, top, bottom) = await OnStaThread(() =>
        {
            var frame = new System.Windows.Media.Imaging.JpegBitmapDecoder(new MemoryStream(jpeg),
                System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
            var bgra = new System.Windows.Media.Imaging.FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
            bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
            int Offset(int x, int y) => (y * bgra.PixelWidth + x) * 4;
            return (bgra.PixelWidth, bgra.PixelHeight, pixels[Offset(240, 4)..(Offset(240, 4) + 3)],
                pixels[Offset(240, 849)..(Offset(240, 849) + 3)]);
        });
        Assert.Equal((480, 854), (width, height));
        Assert.True(top[0] > 200 && top[2] < 60, $"Haut attendu bleu : {string.Join(",", top)}.");
        Assert.True(bottom[2] > 200 && bottom[0] < 60, $"Bas attendu rouge : {string.Join(",", bottom)}.");
        await surface.DisposeAsync();
    }

    [Fact]
    public async Task ImageDeDemarrage_AccuseAnterieurALEcriture_Ignore()
    {
        var (connection, _, surface) = await OpenAsync();
        connection.PauseWrites();

        var sending = surface.SetBootImageAsync(Gradient(854, 480), CancellationToken.None);
        connection.QueueInput(WriteAcknowledgement());
        await Task.Delay(100);
        connection.ResumeWrites();
        await connection.WaitUntilWrittenAsync(written => written.Where(report => CommandOf(report) == "LOG").Any());
        await Task.Delay(100);
        Assert.False(sending.IsCompleted);

        connection.QueueInput(WriteAcknowledgement());
        await sending.WaitAsync(EventWait);
        await surface.DisposeAsync();
    }

    [Fact]
    public async Task ImageDeDemarrage_SansAccuse_ExpireApresDixSecondes()
    {
        var (connection, time, surface) = await OpenAsync();

        var sending = surface.SetBootImageAsync(Gradient(854, 480), CancellationToken.None);
        await connection.WaitUntilWrittenAsync(written => written.Where(report => CommandOf(report) == "LOG").Any());
        await Task.Delay(200);
        time.Advance(TimeSpan.FromSeconds(9));
        await Task.Delay(50);
        Assert.False(sending.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<TimeoutException>(() => sending.WaitAsync(EventWait));
        await surface.DisposeAsync();
    }

    [Fact]
    public async Task ImageDeDemarrage_MauvaiseTaille_RefuseeSansEcriture()
    {
        var (connection, _, surface) = await OpenAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => surface.SetBootImageAsync(Gradient(800, 480), CancellationToken.None));

        await Task.Delay(50);
        Assert.Equal(4, connection.Written.Count);
        await surface.DisposeAsync();
    }

    private static IEnumerable<byte[]> Pairs(byte[] bytes) => bytes.Zip(bytes.Skip(1), (first, second) => new[] { first, second });

    private static byte[] WriteAcknowledgement()
    {
        var report = new byte[513];
        Encoding.ASCII.GetBytes("ACK").CopyTo(report, 1);
        Encoding.ASCII.GetBytes("OK").CopyTo(report, 6);
        return report;
    }

    /// <summary>
    /// Dégradé opaque, qui se compresse bien : le JPEG reste sous le plafond de l'image de démarrage.
    /// </summary>
    private static SurfaceImage Gradient(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var x = offset / 4 % width;
            pixels[offset] = (byte)(x * 255 / width);
            pixels[offset + 1] = 40;
            pixels[offset + 2] = (byte)(255 - x * 255 / width);
            pixels[offset + 3] = 255;
        }
        return new SurfaceImage(width, height, pixels);
    }

    private static async Task<(FakeHidConnection Connection, ManualTimeProvider Time, StreamDockSurface Surface)> OpenAsync()
    {
        var connection = new FakeHidConnection();
        var time = new ManualTimeProvider();
        var surface = new StreamDockSurface(connection, time);
        await surface.StartAsync(CancellationToken.None);
        return (connection, time, surface);
    }

    /// <summary>
    /// Nom de commande lu après le préfixe « CRT\0\0 » jusqu'au premier zéro, vide pour un bloc d'image.
    /// </summary>
    private static string CommandOf(byte[] report)
    {
        if (Encoding.ASCII.GetString(report, 1, 3) != "CRT")
            return "";
        var name = report.AsSpan(6, 7);
        var end = name.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? name : name[..end]);
    }

    /// <summary>
    /// Numéro et JPEG d'une image dont l'en-tête est à headerIndex : ses blocs doivent le suivre sans rapport intercalé.
    /// </summary>
    private static (byte KeyId, byte[] Jpeg) ImageAt(IReadOnlyList<byte[]> written, int headerIndex)
    {
        var header = written[headerIndex];
        var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(9, 4));
        var jpeg = new List<byte>();
        for (var index = headerIndex + 1; jpeg.Count < length; index++)
        {
            Assert.Equal("", CommandOf(written[index]));
            jpeg.AddRange(written[index].Skip(1).Take(Math.Min(1024, length - jpeg.Count)));
        }
        return (header[13], jpeg.ToArray());
    }

    private static byte[] InputReport(byte keyId, byte state)
    {
        var report = new byte[513];
        Encoding.ASCII.GetBytes("ACK").CopyTo(report, 1);
        report[10] = keyId;
        report[11] = state;
        return report;
    }

    /// <summary>
    /// Bruit opaque, qui se compresse mal : le JPEG dépasse un bloc de 1024 octets.
    /// </summary>
    private static SurfaceImage Noise(int size, int seed)
    {
        var pixels = new byte[size * size * 4];
        new Random(seed).NextBytes(pixels);
        for (var offset = 3; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = 255;
        }
        return new SurfaceImage(size, size, pixels);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + EventWait;
        while (condition() == false)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met.");
            await Task.Delay(5);
        }
    }

    private static async Task<T> OnStaThread<T>(Func<T> action)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { done.SetResult(action()); }
            catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await done.Task.WaitAsync(EventWait);
    }
}
