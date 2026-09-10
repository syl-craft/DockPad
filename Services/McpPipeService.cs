using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace DockPad.Services;

/// <summary>Relais JSON MCP avec un délai sur les IO, y compris après connexion.</summary>
public static class McpPipeService
{
    public const string PipeName = "DockPad_McpPipe";
    private const int MaxInstances = 4;
    private const int ExchangeTimeoutMs = 10000;

    public static void StartServer(Func<string, string> handleRequest) =>
        _ = RunServerAsync(handleRequest, CancellationToken.None);

    /// <summary>Démarre quatre boucles d'écoute annulables.</summary>
    public static Task RunServerAsync(Func<string, string> handleRequest, CancellationToken token,
        string pipeName = PipeName, int timeoutMs = ExchangeTimeoutMs) =>
        Task.WhenAll(Enumerable.Range(0, MaxInstances).Select(_ =>
            ServerLoopAsync(handleRequest, token, pipeName, timeoutMs)));

    private static async Task ServerLoopAsync(Func<string, string> handleRequest,
        CancellationToken token, string pipeName, int timeoutMs)
    {
        bool faulted = false;
        while (!App.IsExiting && !token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut,
                    MaxInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(timeoutMs);
                using var reader = PipeTransport.Reader(server);
                var request = await reader.ReadLineAsync(deadline.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(request))
                {
                    // Suspend le délai d'IO pendant le traitement de la requête.
                    deadline.CancelAfter(Timeout.Infinite);
                    var response = handleRequest(request);
                    deadline.CancelAfter(timeoutMs);
                    await PipeTransport.WriteLineAsync(server, response, deadline.Token).ConfigureAwait(false);
                    // Attend la fermeture du client après lecture, avec le même délai d'IO.
                    await reader.ReadLineAsync(deadline.Token).ConfigureAwait(false);
                }
                faulted = false;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (OperationCanceledException)
            {
                // Reprend l'écoute après expiration du délai d'IO.
            }
            catch (Exception ex)
            {
                if (!faulted) LogService.Warn(ex, "Pipe MCP interrompu, réécoute");
                faulted = true;
                try { await Task.Delay(1000, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>Échange limité à 60 s par défaut, dont 2 s maximum pour la connexion.</summary>
    public static string Send(string requestJson, int timeoutMs = 60000, string? pipeName = null) =>
        SendAsync(requestJson, timeoutMs, pipeName).GetAwaiter().GetResult();

    public static async Task<string> SendAsync(string requestJson, int timeoutMs = 60000, string? pipeName = null)
    {
        using var deadline = new CancellationTokenSource(timeoutMs);
        using var client = new NamedPipeClientStream(".", pipeName ?? PipeName,
            PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await client.ConnectAsync(Math.Min(timeoutMs, 2000), deadline.Token).ConfigureAwait(false);
            await PipeTransport.WriteLineAsync(client, requestJson, deadline.Token).ConfigureAwait(false);
            using var reader = PipeTransport.Reader(client);
            return await reader.ReadLineAsync(deadline.Token).ConfigureAwait(false)
                ?? throw new IOException("Réponse vide du pipe MCP.");
        }
        catch (OperationCanceledException ex)
        {
            throw new TimeoutException("Délai de l'échange MCP dépassé.", ex);
        }
    }
}
