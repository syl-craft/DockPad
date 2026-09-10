using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace DockPad.Services;

/// <summary>
/// Relais d'une ligne de texte entre instances : l'instance principale écoute, les instances
/// secondaires lancées par Windows lui transmettent leur argument.
/// </summary>
/// <remarks>
/// <para>
/// Extrait d'<c>UrlPipeService</c> à l'arrivée de l'injection de secrets, qui a exactement le même
/// besoin : Windows lance <c>DockPad.exe</c> avec un argument, le mutex n'est pas acquis, et
/// l'argument doit rejoindre l'instance qui tourne déjà.
/// </para>
/// <para>
/// <b>Deux pipes distincts plutôt qu'un préfixe dans la charge utile</b> : les deux flux n'ont rien
/// à voir, et un protocole partagé se paie au premier ajout — celui où l'on découvre qu'un des deux
/// consommateurs doit distinguer un cas de plus.
/// </para>
/// </remarks>
public sealed class LinePipeService(string pipeName)
{
    public string PipeName { get; } = pipeName;

    /// <summary>Démarre le serveur (instance principale). Le rappel a lieu sur un thread de pool.</summary>
    public void StartServer(Action<string> onLine) => _ = RunServerAsync(onLine, CancellationToken.None);

    public async Task RunServerAsync(Action<string> onLine, CancellationToken token, int timeoutMs = 2000)
    {
        bool faulted = false;
        while (!App.IsExiting && !token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(timeoutMs);
                using var reader = PipeTransport.Reader(server);
                var line = await reader.ReadLineAsync(deadline.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(line)) onLine(line);
                faulted = false;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!faulted) LogService.Warn(ex, $"Pipe {PipeName} interrompu, réécoute");
                faulted = true;
                try { await Task.Delay(1000, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>Envoie une ligne à l'instance principale. Faux si échec ou dépassement du délai.</summary>
    public bool TrySend(string line, int timeoutMs = 2000)
    {
        if (Send(line, timeoutMs, out var error)) return true;

        LogService.Warn(error!, $"Relais vers l'instance principale par {PipeName}");
        return false;
    }

    /// <summary>
    /// Même envoi, sans une ligne au journal.
    /// </summary>
    /// <remarks>
    /// Réservé au raccourci de démarrage (<see cref="StartupRelay"/>), qui s'exécute <b>avant</b>
    /// <c>LogService.Init()</c> : initialiser le journal pour tracer cet envoi lui rendrait une
    /// part du coût qu'il existe justement pour éviter. Un échec reste journalisé, par le chemin
    /// lent sur lequel on retombe.
    /// </remarks>
    public bool TrySendSilently(string line, int timeoutMs) => Send(line, timeoutMs, out _);

    private bool Send(string line, int timeoutMs, out Exception? error)
    {
        try
        {
            SendAsync(line, timeoutMs).GetAwaiter().GetResult();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    private async Task SendAsync(string line, int timeoutMs)
    {
        using var deadline = new CancellationTokenSource(timeoutMs);
        using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        await client.ConnectAsync(deadline.Token).ConfigureAwait(false);
        await PipeTransport.WriteLineAsync(client, line, deadline.Token).ConfigureAwait(false);
    }
}
