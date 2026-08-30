using System.IO;
using System.IO.Pipes;
using System.Threading;

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

    private bool _faulted;

    /// <summary>Démarre le serveur (instance principale). Le rappel a lieu sur un thread de pool.</summary>
    public void StartServer(Action<string> onLine)
    {
        var thread = new Thread(() =>
        {
            while (!App.IsExiting)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, maxNumberOfServerInstances: 1);
                    server.WaitForConnection();
                    _faulted = false;
                    using var reader = new StreamReader(server);
                    var line = reader.ReadLine();
                    if (!string.IsNullOrWhiteSpace(line)) onLine(line);
                }
                catch (Exception ex)
                {
                    // Pipe cassé ou fermeture : on retente. Un seul WRN par série d'échecs, et
                    // backoff pour ne pas spinner si l'échec est persistant.
                    if (!_faulted) { LogService.Warn(ex, $"Pipe {PipeName} interrompu, réécoute"); _faulted = true; }
                    Thread.Sleep(1000);
                }
            }
        })
        { IsBackground = true, Name = $"DockPad_{PipeName}Server" };
        thread.Start();
    }

    /// <summary>Envoie une ligne à l'instance principale. Faux si échec ou dépassement du délai.</summary>
    public bool TrySend(string line, int timeoutMs = 2000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(line);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Warn(ex, $"Relais vers l'instance principale par {PipeName}");
            return false;
        }
    }
}
