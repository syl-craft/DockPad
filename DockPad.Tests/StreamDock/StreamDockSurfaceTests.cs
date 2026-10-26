using System.Buffers.Binary;
using System.IO;
using System.Text;
using DockPad.StreamDock;
using DockPad.Surfaces;

namespace DockPad.Tests;

/// <summary>
/// Surface du 293S V3 contre un faux handle HID :
/// - démarrage DIS, LIG 100, CLE 0xFF, STP avant que la surface soit rendue ; en échec, handle fermé et erreur levée ;
/// - une image part d'un bloc (en-tête BAT puis ses blocs, sans rapport intercalé) avec le numéro matériel de la
///   touche ou de l'écran 16 à 18 ; seule la dernière image non commencée d'une touche est écrite ; image annulée jamais écrite ;
/// - CommitAsync rend la main une fois STP écrit, échoue si la surface se déconnecte avant ;
/// - CONNECT toutes les 10 s, devant les images en attente ;
/// - appuis lus et levés en KeyChanged, même si un abonné lève ;
/// - Disconnected levé une seule fois sur erreur de lecture, d'écriture ou délai dépassé, aussi à un abonné tardif,
///   handle fermé, puis envois refusés par IOException ;
/// - fermeture qui efface et met en veille, rend la main en une seconde même si l'écriture ou la fermeture du handle
///   bloque, partagée entre appels concurrents, sans lever Disconnected ; envois après fermeture refusés.
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
        Assert.Equal(new SurfaceDescription("293S V3", 3, 5, 96, 3, 80), surface.Description);
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

    [Theory]
    [InlineData(0, 16)]
    [InlineData(1, 17)]
    [InlineData(2, 18)]
    public async Task EcranLateral_UtiliseLesNumeros16A18(int display, byte displayId)
    {
        var (connection, _, surface) = await OpenAsync();

        await surface.SetDisplayImageAsync(display, Noise(80, seed: 1), CancellationToken.None);
        await surface.CommitAsync(CancellationToken.None);

        var header = connection.Written.Where(report => CommandOf(report) == "BAT").First();
        Assert.Equal(displayId, header[13]);
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
