using DockPad.Services;

namespace DockPad.Tests.StreamDock;

/// <summary>
/// Un abonné défaillant à la grille ne bloque ni les suivants ni le retour de la notification.
/// </summary>
public class TileEventsTests
{
    [Fact]
    public void GridChanged_ExceptionDUnAbonne_AutresAppeles()
    {
        var calls = new List<int>();
        Action first = () => calls.Add(1);
        Action failing = () => throw new InvalidOperationException("Subscriber failed.");
        Action last = () => calls.Add(3);
        TileEvents.GridChanged += first;
        TileEvents.GridChanged += failing;
        TileEvents.GridChanged += last;
        try
        {
            var exception = Record.Exception(TileEvents.RaiseGridChanged);

            Assert.Null(exception);
            Assert.Equal([1, 3], calls);
        }
        finally
        {
            TileEvents.GridChanged -= first;
            TileEvents.GridChanged -= failing;
            TileEvents.GridChanged -= last;
        }
    }
}
