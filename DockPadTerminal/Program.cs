using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using DockPad.Services;

namespace DockPad;

/// <summary>
/// <c>DockPadTerminal.exe --terminal "%V\." -- &lt;commande&gt;</c> : ce que lancent les prédéfinis terminal
/// du clic droit. Passe la demande à l'instance résidente de DockPad, déjà chaude, qui ouvre le
/// terminal comme pour une tuile ; sans elle, laisse <c>DockPad.exe --terminal</c> s'en charger.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (TerminalRequest.Parse(args) is not { } request) return 2;

        if (InstanceIsRunning() && TrySend(TerminalRequest.Encode(request))) return 0;

        // Pas d'instance qui réponde : DockPad calcule et lance lui-même, sans démarrer l'application.
        var start = new ProcessStartInfo(DockPadExe()) { UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var dockPad = Process.Start(start);
        return dockPad == null ? 1 : 0;
    }

    /// <summary>
    /// Le lanceur stable de Velopack, au-dessus de <c>current\</c>, qui attend la fin d'une mise à jour ;
    /// à défaut, le <c>DockPad.exe</c> voisin.
    /// </summary>
    private static string DockPadExe()
    {
        string here = AppContext.BaseDirectory.TrimEnd('\\');
        string neighbour = Path.Combine(here, "DockPad.exe");
        if (!string.Equals(Path.GetFileName(here), "current", StringComparison.OrdinalIgnoreCase)) return neighbour;

        if (Path.GetDirectoryName(here) is not { } root) return neighbour;
        string stable = Path.Combine(root, "DockPad.exe");
        return File.Exists(stable) ? stable : neighbour;
    }

    /// <summary>
    /// Une instance tient le mutex : sans cette question, <c>Connect</c> attendrait tout son délai
    /// sur un tube que personne n'écoute.
    /// </summary>
    private static bool InstanceIsRunning()
    {
        try
        {
            using var existing = Mutex.OpenExisting(TerminalRequest.SingleInstanceMutex);
            return true;
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Écrit la ligne dans le tube et attend le « ok » de DockPad, le protocole de <c>LinePipeService</c>.
    /// </summary>
    private static bool TrySend(string line)
    {
        try
        {
            using var deadline = new CancellationTokenSource(millisecondsDelay: 1000);
            using var pipe = new NamedPipeClientStream(".", TerminalRequest.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.ConnectAsync(deadline.Token).GetAwaiter().GetResult();
            pipe.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), deadline.Token).AsTask().GetAwaiter().GetResult();

            using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64, leaveOpen: true);
            return reader.ReadLineAsync(deadline.Token).AsTask().GetAwaiter().GetResult() == "ok";
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
