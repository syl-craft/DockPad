namespace DockPad.Services.UpdateHost;

/// <summary>
/// Les requêtes acceptées restent en mémoire jusqu'au traitement ou au transfert au lanceur stable.
/// </summary>
public sealed class PendingRelays(Action<Action> schedule, Action<string[]> forward)
{
    private readonly object _gate = new();
    private readonly Dictionary<long, (string[] Args, Action Handle)> _pending = [];
    private long _next;
    private bool _handoff;
    public void Accept(string[] args, Action handle)
    {
        long id;
        lock (_gate)
        {
            if (_handoff) { forward(args); return; }
            id = ++_next;
            _pending.Add(id, (args, handle));
        }
        schedule(() => Run(id));
    }
    private void Run(long id)
    {
        Action handle;
        lock (_gate)
        {
            if (!_pending.Remove(id, out var request)) return;
            handle = request.Handle;
        }
        handle();
    }
    // Appelé sur le thread d'interface, sans gestionnaire d'interface concurrent.
    public void BeginHandoff()
    {
        lock (_gate)
        {
            _handoff = true;
            foreach (var pair in _pending.ToArray())
            {
                forward(pair.Value.Args);
                _pending.Remove(pair.Key);
            }
        }
    }
    public void Resume()
    {
        lock (_gate)
        {
            _handoff = false;
            foreach (var id in _pending.Keys.ToArray()) schedule(() => Run(id));
        }
    }
}
