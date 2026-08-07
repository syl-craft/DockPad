using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace DockPad.Services;

/// <summary>
/// Pipe MCP : l'instance principale écoute (plusieurs instances simultanées — Claude Code +
/// Claude Desktop), les relais --mcp envoient une requête JSON et lisent la réponse.
/// </summary>
public static class McpPipeService
{
    public const string PipeName = "DockPad_McpPipe";
    private const int MaxInstances = 4;

    private static bool _serverFaulted;

    public static void StartServer(Func<string, string> handleRequest)
    {
        for (int i = 0; i < MaxInstances; i++)
        {
            var thread = new Thread(() => ServerLoop(handleRequest))
            { IsBackground = true, Name = $"DockPad_McpPipeServer" };
            thread.Start();
        }
    }

    private static void ServerLoop(Func<string, string> handleRequest)
    {
        while (!App.IsExiting)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.InOut, MaxInstances);
                server.WaitForConnection();
                _serverFaulted = false;

                using var reader = new StreamReader(server);
                using var writer = new StreamWriter(server) { AutoFlush = true };
                var request = reader.ReadLine();
                if (!string.IsNullOrWhiteSpace(request))
                {
                    writer.WriteLine(handleRequest(request));
                    server.WaitForPipeDrain();
                }
            }
            catch (Exception ex)
            {
                if (!_serverFaulted)
                { LogService.Warn(ex, "Pipe DockPad_McpPipe interrompu, réécoute"); _serverFaulted = true; }
                Thread.Sleep(1000);
            }
        }
    }

    /// <summary>Côté relais : envoie la requête, lit la réponse. Lève sur timeout/échec.
    /// pipeName surchargeable pour les tests (défaut : PipeName).</summary>
    public static string Send(string requestJson, int timeoutMs = 2000, string? pipeName = null)
    {
        using var client = new NamedPipeClientStream(".", pipeName ?? PipeName, PipeDirection.InOut);
        client.Connect(timeoutMs);
        // Pas de using sur writer/reader : le Dispose du StreamReader fermerait le pipe,
        // puis celui du StreamWriter tenterait un flush sur pipe fermé → ObjectDisposedException
        // sur le chemin de retour d'un échange pourtant réussi. Le using du client suffit
        // (AutoFlush garantit que la requête est déjà partie).
        var writer = new StreamWriter(client) { AutoFlush = true };
        var reader = new StreamReader(client);
        writer.WriteLine(requestJson);
        return reader.ReadLine() ?? throw new IOException("Réponse vide du pipe MCP.");
    }
}
