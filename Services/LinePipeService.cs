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
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(line);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }
}
