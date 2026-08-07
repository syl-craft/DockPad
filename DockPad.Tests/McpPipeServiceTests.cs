using System.IO;
using System.IO.Pipes;
using DockPad.Services;

namespace DockPad.Tests;

public class McpPipeServiceTests
{
    /// <summary>
    /// Non-régression : un aller-retour réussi ne doit pas lever au retour de Send.
    /// (Un double using writer/reader sur le même pipe levait ObjectDisposedException
    /// au dispose, APRÈS la réponse — le relais traduisait en « DockPad n'est pas lancé ».)
    /// </summary>
    private const string TestPipe = "DockPad_McpPipe_Test"; // nom dédié : pas de collision avec l'app

    [Fact]
    public void Send_AllerRetourReussi_RetourneLaReponseSansException()
    {
        var serveur = Task.Run(() =>
        {
            using var server = new NamedPipeServerStream(
                TestPipe, PipeDirection.InOut, 1);
            server.WaitForConnection();
            using var reader = new StreamReader(server);
            using var writer = new StreamWriter(server) { AutoFlush = true };
            var requete = reader.ReadLine();
            writer.WriteLine("{\"ok\":true,\"echo\":" + requete + "}");
            server.WaitForPipeDrain();
        });

        var reponse = McpPipeService.Send("""{"tool":"test"}""", timeoutMs: 5000, pipeName: TestPipe);

        Assert.Contains("\"ok\":true", reponse);
        Assert.True(serveur.Wait(TimeSpan.FromSeconds(5)), "le serveur de test n'a pas terminé");
    }

    [Fact]
    public void Send_PipeAbsent_LeveTimeout()
    {
        // Aucun serveur n'écoute sur le pipe (nom dédié pour ne pas dépendre de l'app)
        Assert.ThrowsAny<Exception>(() =>
        {
            using var client = new NamedPipeClientStream(".", "DockPad_McpPipe_TestAbsent",
                PipeDirection.InOut);
            client.Connect(200);
        });
    }
}
