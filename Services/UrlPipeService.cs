namespace DockPad.Services;

/// <summary>
/// Relais d'URL entre instances : l'instance principale écoute sur un named pipe, les instances
/// secondaires (lancées par Windows avec <c>--url</c>) lui transmettent l'URL.
/// </summary>
/// <remarks>
/// Façade sur <see cref="LinePipeService"/>, dont la mécanique est partagée avec le pipe
/// d'injection de secrets. L'API n'a pas changé : les appelants n'ont pas bougé.
/// </remarks>
public static class UrlPipeService
{
    public const string PipeName = "DockPad_UrlPipe";

    private static readonly LinePipeService Pipe = new(PipeName);

    /// <summary>Démarre le serveur (instance principale). onUrl est appelé sur un thread de pool.</summary>
    public static void StartServer(Action<string> onUrl) => Pipe.StartServer(onUrl);

    /// <summary>Envoie une URL à l'instance principale. false si échec ou timeout.</summary>
    public static bool TrySend(string url, int timeoutMs = 2000) => Pipe.TrySend(url, timeoutMs);
}
