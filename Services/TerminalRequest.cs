using System.IO;
using System.Text;

namespace DockPad.Services;

/// <summary>
/// Une demande <c>--terminal "&lt;dossier&gt;" -- &lt;commande&gt;</c> : sa lecture, et sa forme dans le tube.
/// </summary>
/// <remarks>
/// <b>Compilé deux fois</b> : dans DockPad et dans le lanceur natif <c>DockPadTerminal.exe</c>. Il ne
/// dépend donc de rien d'autre que la bibliothèque de base — NativeAOT, pas de WPF. Les noms du
/// tube et du mutex vivent ici pour la même raison : un nom qui divergerait ferait échouer le
/// relais en silence.
/// </remarks>
public static class TerminalRequest
{
    public const string Flag = "--terminal";

    /// <summary>
    /// La demande déjà lue, sous sa forme du tube : ce que l'instance résidente transmet au lanceur
    /// stable quand une mise à jour la remplace.
    /// </summary>
    public const string EncodedFlag = "--terminal-request";
    public const string PipeName = "DockPad_TerminalPipe";

    /// <summary>
    /// Nom du mutex d'instance unique, posé par <c>App</c> ; <c>StartupRelay</c> le reprend.
    /// </summary>
    public const string SingleInstanceMutex = "DockPad_SingleInstance";

    /// <summary>
    /// Un dossier à ouvrir, et la commande à y lancer (vide pour une simple console).
    /// </summary>
    public readonly record struct Request(string Folder, string Command);

    /// <summary>
    /// La demande portée par la ligne de commande, ou <c>null</c> si ce n'est pas un lancement <c>--terminal</c>.
    /// </summary>
    public static Request? Parse(string[] args)
    {
        int encoded = Array.IndexOf(args, EncodedFlag);
        if (encoded >= 0) return encoded + 1 < args.Length ? Decode(args[encoded + 1]) : null;

        int flag = Array.IndexOf(args, Flag);
        if (flag < 0 || flag + 1 >= args.Length) return null;

        string folder = NormalizeFolder(args[flag + 1]);
        int separator = Array.IndexOf(args, "--", flag + 2);
        string command = separator < 0 ? "" : JoinArguments(args.Skip(separator + 1));

        return new Request(folder, command);
    }

    /// <summary>
    /// Le dossier sans son <c>\.</c> final, et une racine de lecteur gardée avec sa barre.
    /// </summary>
    public static string NormalizeFolder(string folder)
    {
        string trimmed = folder.Trim().Trim('"');
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return trimmed;
        }
    }

    /// <summary>
    /// La ligne de commande qui redonne cette demande à <see cref="Parse"/>.
    /// </summary>
    public static string[] ToArgs(Request request) => [EncodedFlag, Encode(request)];

    /// <summary>
    /// Recompose des arguments en une ligne de commande : un argument qui ne tient pas qu'en lettres,
    /// chiffres et <c>_ - . : \ / =</c> est remis entre guillemets, pour qu'un <c>;</c> ou un
    /// <c>&amp;</c> reste un caractère et ne sépare pas deux commandes dans le shell qui la reçoit.
    /// </summary>
    public static string JoinArguments(IEnumerable<string> args) => string.Join(" ", args.Select(Quote));

    private static string Quote(string arg)
    {
        if (arg != "" && arg.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or ':' or '\\' or '/' or '=')) return arg;

        var quoted = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\') { backslashes++; continue; }
            // Les barres qui précèdent un guillemet sont doublées, le guillemet échappé.
            quoted.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
            backslashes = 0;
            quoted.Append(c);
        }
        quoted.Append('\\', backslashes * 2).Append('"');
        return quoted.ToString();
    }

    /// <summary>
    /// La demande en une ligne pour le tube : le dossier, une tabulation, la commande. Un chemin
    /// Windows ne peut pas contenir de tabulation.
    /// </summary>
    public static string Encode(Request request) => $"{request.Folder}\t{request.Command}";

    public static Request? Decode(string payload)
    {
        int tab = payload.IndexOf('\t');
        return tab < 0 ? null : new Request(payload[..tab], payload[(tab + 1)..]);
    }
}
