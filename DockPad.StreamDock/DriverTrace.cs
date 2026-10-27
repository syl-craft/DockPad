using System.Diagnostics;

namespace DockPad.StreamDock;

/// <summary>
/// Diagnostics du pilote, best effort : un listener qui lève ou un ToString qui échoue ne remonte jamais à l'appelant.
/// </summary>
internal static class DriverTrace
{
    public static void Warning(string context, Exception? error = null)
    {
        try
        {
            Trace.TraceWarning(error == null ? context : $"{context}: {error}");
        }
        catch (Exception)
        {
            // Rien à faire : le diagnostic est lui-même en échec, et l'appelant peut être un rappel Windows.
        }
    }
}
