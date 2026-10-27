using System.Threading;
using System.Windows.Threading;
using DockPad.Services;

namespace DockPad.Tests.StreamDock;

/// <summary>
/// Page inchangée, origine publiée, isolation des abonnés et accès limité au thread du Dispatcher fourni.
/// </summary>
public class ShortcutPageStateTests
{
    [Theory]
    [InlineData(PageChangeOrigin.DockPad)]
    [InlineData(PageChangeOrigin.StreamDock)]
    public void Set_MemePage_NeLevePas(PageChangeOrigin origin)
    {
        var state = new ShortcutPageState();
        state.Set(2, PageChangeOrigin.DockPad);
        int calls = 0;
        state.Changed += (_, _) => calls++;

        state.Set(2, origin);

        Assert.Equal(2, state.Page);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(PageChangeOrigin.DockPad)]
    [InlineData(PageChangeOrigin.StreamDock)]
    public void Set_AutrePage_LeveAvecLOrigine(PageChangeOrigin origin)
    {
        var state = new ShortcutPageState();
        var changes = new List<(int Page, PageChangeOrigin Origin, int CurrentPage)>();
        state.Changed += (page, source) =>
        {
            changes.Add((page, source, state.Page));
        };
        Assert.Equal(0, state.Page);

        state.Set(3, origin);

        Assert.Equal(3, state.Page);
        Assert.Equal([(3, origin, 3)], changes);
    }

    [Fact]
    public void Set_ExceptionDUnAbonne_AutresAppeles()
    {
        var state = new ShortcutPageState();
        var changes = new List<(int Page, PageChangeOrigin Origin)>();
        state.Changed += (_, _) => throw new InvalidOperationException("Subscriber failed.");
        state.Changed += (page, origin) => changes.Add((page, origin));

        state.Set(3, PageChangeOrigin.StreamDock);

        Assert.Equal(3, state.Page);
        Assert.Equal([(3, PageChangeOrigin.StreamDock)], changes);
    }

    [Fact]
    public void Set_ThreadDuDispatcher_ModifieEtNotifieSurCeThread()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var state = new ShortcutPageState(dispatcher);
        int notificationThread = 0;
        state.Changed += (_, _) => notificationThread = Environment.CurrentManagedThreadId;

        state.Set(3, PageChangeOrigin.StreamDock);

        Assert.Equal(3, state.Page);
        Assert.Equal(Environment.CurrentManagedThreadId, notificationThread);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Set_AutreThread_RefuseSansModifierNiNotifier(int page)
    {
        var state = new ShortcutPageState(Dispatcher.CurrentDispatcher);
        int calls = 0;
        state.Changed += (_, _) => calls++;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            error = Record.Exception(() => state.Set(page, PageChangeOrigin.StreamDock));
        });

        thread.Start();
        thread.Join();

        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(0, state.Page);
        Assert.Equal(0, calls);
    }
}
