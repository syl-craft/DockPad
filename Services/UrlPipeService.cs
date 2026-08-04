using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace DockPad.Services;

/// <summary>
/// Relais d'URL entre instances : l'instance principale écoute sur un named pipe,
/// les instances secondaires (lancées par Windows avec --url) lui transmettent l'URL.
/// </summary>
public static class UrlPipeService
{
    public const string PipeName = "DockPad_UrlPipe";

    /// <summary>Démarre le serveur (instance principale). onUrl est appelé sur un thread de pool.</summary>
    public static void StartServer(Action<string> onUrl)
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
                    using var reader = new StreamReader(server);
                    var url = reader.ReadLine();
                    if (!string.IsNullOrWhiteSpace(url)) onUrl(url);
                }
                catch { /* pipe cassé ou fermeture : on retente */ }
            }
        })
        { IsBackground = true, Name = "DockPad_UrlPipeServer" };
        thread.Start();
    }

    /// <summary>Envoie une URL à l'instance principale. false si échec ou timeout.</summary>
    public static bool TrySend(string url, int timeoutMs = 2000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(url);
            return true;
        }
        catch { return false; }
    }
}
