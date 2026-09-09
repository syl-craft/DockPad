using System.Threading;

namespace DockPad.Services;

/// <summary>Le tube visé par un lancement, et ce qu'on lui envoie.</summary>
public readonly record struct RelayRequest(string PipeName, string Payload);

/// <summary>
/// Le raccourci de démarrage : rendre la main à l'instance résidente <b>avant</b> de construire
/// quoi que ce soit.
/// </summary>
/// <remarks>
/// <para>
/// Windows lance un <c>DockPad.exe</c> entier au clic sur un lien, pour un travail qui tient en une
/// ligne écrite dans un tube. Mesuré sur cette machine, à froid écarté :
/// </para>
/// <list type="table">
///   <item><description>démarrage du runtime .NET seul — <b>~85 ms</b>, incompressible</description></item>
///   <item><description>+ connexion au tube et envoi — <b>~2 ms</b>, le seul travail utile</description></item>
///   <item><description>+ <c>Application</c> WPF et <c>App.xaml</c> — <b>~80 ms</b></description></item>
///   <item><description>+ le prologue de <c>OnStartup</c> (journal, culture, thème, SystemEvents) — <b>~140 ms</b></description></item>
/// </list>
/// <para>
/// Soit ~300 ms dont <b>~240 dépensés pour rien</b>. Ce type décide, avant WPF, si le lancement se
/// résume à une ligne dans un tube — auquel cas le processus sort sans avoir rien construit.
/// </para>
/// <para>
/// <b>Le mutex est consulté d'abord, et ce n'est pas une optimisation de plus.</b> Sur un tube dont
/// personne n'écoute, <c>Connect</c> attend tout son délai avant d'échouer : sans cette question
/// préalable, un clic sur un lien alors que DockPad ne tourne pas paierait deux secondes d'attente
/// avant de démarrer l'application.
/// </para>
/// <para>
/// <b>Les trois noms de tube vivent ici</b>, et les serveurs les lisent. Un nom qui divergerait ne
/// lèverait pas : le raccourci échouerait en silence et l'application redeviendrait lente pour
/// toujours, sans un mot au journal. La parade est structurelle, pas un test.
/// </para>
/// </remarks>
public static class StartupRelay
{
    public const string UrlPipeName = UrlPipeService.PipeName;
    public const string InjectPipeName = "DockPad_InjectPipe";

    /// <summary>
    /// Tube du <b>second lancement</b> : « montre-toi ». Un tube de plus plutôt qu'un préfixe dans
    /// un tube existant — les trois flux n'ont rien à voir.
    /// </summary>
    public const string ShowPipeName = "DockPad_ShowPipe";

    /// <summary>La seule charge utile que le tube d'affichage transporte.</summary>
    public const string ShowRequest = "show";

    /// <summary>Nom du mutex d'instance unique, posé par <c>App</c> et lu ici.</summary>
    public const string SingleInstanceMutex = "DockPad_SingleInstance";

    /// <summary>
    /// Ce que ce lancement demanderait à une instance déjà présente, ou <c>null</c> s'il doit
    /// vivre par lui-même. <b>Décision pure</b> : aucun fichier, aucun tube, aucun WPF.
    /// </summary>
    /// <remarks>
    /// <b>Le mode MCP ne se relaie jamais</b> : c'est un serveur stdio, il doit démarrer, et il
    /// coexiste volontairement avec l'instance résidente. Le relayer le rendrait muet.
    /// </remarks>
    public static RelayRequest? Plan(string[] args)
    {
        if (args.Contains("--mcp")) return null;

        if (Arg(args, "--url") is { } url) return new RelayRequest(UrlPipeName, url);
        if (Arg(args, "--inject-secrets") is { } path) return new RelayRequest(InjectPipeName, path);

        // Tout le reste est un second lancement : « remonte la fenêtre ». Même règle que le chemin
        // lent, y compris pour un « --url » sans valeur, qui n'en porte pas.
        return new RelayRequest(ShowPipeName, ShowRequest);
    }

    /// <summary>
    /// Tente de rendre la main à l'instance résidente. Vrai si elle a pris la demande : le
    /// processus n'a plus rien à faire.
    /// </summary>
    /// <remarks>
    /// Faux couvre les deux cas, et <c>App</c> les distingue par son propre mutex : soit aucune
    /// instance ne tourne — démarrage normal —, soit une instance tourne mais n'a pas répondu, et
    /// c'est le repli. <b>Le relais n'est donc plus retenté dans <c>OnStartup</c></b> : le
    /// retenter ferait payer deux fois le délai d'attente au cas pathologique.
    /// </remarks>
    public static bool TryRelay(string[] args, int timeoutMs = 2000)
    {
        if (Plan(args) is not { } request) return false;
        if (!InstanceIsRunning(SingleInstanceMutex)) return false;

        // Envoi SANS journal : LogService.Init() n'a pas encore eu lieu, et l'initialiser ici
        // rendrait au chemin rapide une partie du coût qu'il existe pour éviter. Un échec est de
        // toute façon journalisé par le chemin lent, sur lequel on retombe.
        return new LinePipeService(request.PipeName).TrySendSilently(request.Payload, timeoutMs);
    }

    /// <summary>
    /// Une instance résidente tient-elle ce mutex ?
    /// </summary>
    /// <remarks>
    /// <b>La question la plus importante du fichier.</b> Répondue « oui » à tort, un lancement
    /// attendrait le délai entier d'un tube que personne n'écoute ; répondue « oui » toujours,
    /// DockPad ne démarrerait plus du tout. Le nom est un paramètre pour qu'elle se vérifie sur un
    /// mutex de test, sans dépendre de ce qui tourne sur la machine au moment du test.
    /// </remarks>
    public static bool InstanceIsRunning(string mutexName)
    {
        try
        {
            using var existing = Mutex.OpenExisting(mutexName);
            return true;
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch (UnauthorizedAccessException)
        {
            // Le mutex existe mais appartient à une autre session : il y a bien une instance,
            // simplement pas la nôtre. Le tube échouera, et le chemin lent tranchera.
            return false;
        }
    }

    /// <summary>
    /// Valeur d'un argument nommé, ou <c>null</c> s'il est absent ou en dernière position.
    /// </summary>
    /// <remarks>
    /// <b>Une seule implémentation</b>, partagée avec <c>App.OnStartup</c> : le chemin rapide et le
    /// chemin lent doivent lire la ligne de commande exactement pareil. Deux copies, et le jour où
    /// l'une apprend une forme que l'autre ignore, un lancement part sur le mauvais tube.
    /// </remarks>
    public static string? Arg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name)
                return args[i + 1];
        return null;
    }
}
