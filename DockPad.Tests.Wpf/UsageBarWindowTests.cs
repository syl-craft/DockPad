using System.IO;
using System.Windows;
using DockPad.Models;
using DockPad.Services;
using DockPad.Services.Usage;
using DockPad.Views;

namespace DockPad.Tests;

/// <summary>
/// La barre dans QuickAccessWindow : fenêtre construite sans être affichée, mise en page forcée, config suivie par
/// l'événement de <see cref="UsageConfigService"/>. Les relevés et le moniteur de charge sont factices, et le profil de
/// l'utilisateur n'est jamais écrit : la config est enregistrée dans un fichier temporaire.
/// </summary>
public class UsageBarWindowTests
{
    private static readonly DateTime Now = new(2026, 8, 20, 12, 0, 0);

    private sealed class CountingProvider(string id, int session = 62) : IUsageProvider
    {
        private int _reads;

        public string Id => id;
        public string Name => id;
        public int Reads => Volatile.Read(ref _reads);

        public AiProbe Probe() => new() { Available = true, DisplayName = id };

        public Task<AiUsage?> ReadAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _reads);
            return Task.FromResult<AiUsage?>(new AiUsage
            {
                ProviderId = id, Name = id, Glyph = "X", AccentColor = "#123456",
                Session = new UsageWindow { UsedPct = session }, Week = new UsageWindow { UsedPct = 44 },
            });
        }
    }

    private static FrameworkElement Tile(Rig rig, int index) =>
        rig.Window.TileAt(index) ?? throw new InvalidOperationException($"Tile {index} was not rendered.");

    private static UsageConfig ConfigFor(UsagePlacement placement, bool enabled = true)
    {
        var config = new UsageConfig { Placement = placement, Enabled = enabled };
        config.Providers.Add(new AiProviderEntry { Id = "claude", Name = "claude", Order = 0 });
        config.Providers.Add(new AiProviderEntry { Id = "codex", Name = "codex", Order = 1 });
        return config;
    }

    private sealed class Rig(UsageConfig config, params CountingProvider[] providers) : IDisposable
    {
        private readonly string _configPath = Path.Combine(TestProfile.Root, $"usage-{Guid.NewGuid():N}.json");
        private UsageViewModel? _usage;
        private QuickAccessWindow? _window;

        public UsageConfig Config { get; } = config;

        public UsageViewModel Usage => _usage ?? throw new InvalidOperationException("Call Build first.");
        public QuickAccessWindow Window => _window ?? throw new InvalidOperationException("Call Build first.");
        public UsageBar Bar => Window.UsageBar;
        public FrameworkElement Root => (FrameworkElement)Window.Content;

        /// <summary>
        /// Détache la fenêtre des événements statiques : sans cela elle réagirait aux changements de langue des autres tests.
        /// </summary>
        public void Dispose()
        {
            _window?.DetachFromStatics();
            File.Delete(_configPath);
        }

        public void Build()
        {
            var service = new UsageService(providers.Select(p => (IUsageProvider)p).ToList());
            _usage = new UsageViewModel(service, () => Config, () => Now);
            _window = new QuickAccessWindow(_usage, new FakeSystemLoadMonitor(), new ManualTimeProvider());
            Layout();
        }

        /// <summary>
        /// Plusieurs passages : la géométrie de la barre est posée au LayoutUpdated qui suit la première mesure.
        /// </summary>
        public void Layout()
        {
            for (var pass = 0; pass < 4; pass++)
            {
                Root.Measure(new Size(Window.Width, double.PositiveInfinity));
                Root.Arrange(new Rect(0, 0, Window.Width, Root.DesiredSize.Height));
                Root.UpdateLayout();
            }
        }

        /// <summary>
        /// Enregistre la config dans un fichier temporaire, ce qui lève <see cref="UsageConfigService.Changed"/>.
        /// </summary>
        public void Publish()
        {
            UsageConfigService.Save(Config, _configPath);
            WpfTestHost.Flush();
            Layout();
        }

        public void ChangePlacement(UsagePlacement placement)
        {
            Config.Placement = placement;
            Publish();
        }
    }

    [Fact]
    public Task Fenetre_BarreVisibleAvantLePremierReleve() => WpfTestHost.Run(() =>
    {
        using var rig = new Rig(ConfigFor(UsagePlacement.Right), new CountingProvider("claude"));
        rig.Build();

        Assert.Equal(Visibility.Visible, rig.Bar.Visibility);
        Assert.Empty(rig.Usage.Snapshots);
        Assert.Equal(Visibility.Collapsed, rig.Window.UsageBanner.Visibility);
    });

    [Fact]
    public Task Fenetre_EnBas_BarreRepliee() => WpfTestHost.Run(() =>
    {
        using var rig = new Rig(ConfigFor(UsagePlacement.Bottom), new CountingProvider("claude"));
        rig.Build();
        WpfTestHost.PumpUntilCompleted(rig.Usage.RefreshAsync());
        rig.Layout();

        Assert.Equal(Visibility.Collapsed, rig.Bar.Visibility);
        Assert.Equal(850, rig.Window.Width);
    });

    [Fact]
    public Task Fenetre_UsageDesactive_NiBandeauNiBarre() => WpfTestHost.Run(() =>
    {
        using var rig = new Rig(ConfigFor(UsagePlacement.Right, enabled: false), new CountingProvider("claude"));
        rig.Build();
        WpfTestHost.PumpUntilCompleted(rig.Usage.RefreshAsync());
        rig.Layout();

        Assert.Equal(Visibility.Collapsed, rig.Bar.Visibility);
        Assert.Equal(Visibility.Collapsed, rig.Window.UsageBanner.Visibility);
    });

    [Fact]
    public Task Fenetre_ChangementDePosition_AucuneLecture() => WpfTestHost.Run(() =>
    {
        var claude = new CountingProvider("claude");
        var codex = new CountingProvider("codex");
        using var rig = new Rig(ConfigFor(UsagePlacement.Bottom), claude, codex);
        rig.Build();
        WpfTestHost.PumpUntilCompleted(rig.Usage.RefreshAsync());

        rig.ChangePlacement(UsagePlacement.Right);
        Assert.Equal(Visibility.Visible, rig.Bar.Visibility);
        Assert.Equal(Visibility.Collapsed, rig.Window.UsageBanner.Visibility);

        rig.ChangePlacement(UsagePlacement.Bottom);
        Assert.Equal(Visibility.Collapsed, rig.Bar.Visibility);
        Assert.Equal(Visibility.Visible, rig.Window.UsageBanner.Visibility);

        Assert.Equal(1, claude.Reads);
        Assert.Equal(1, codex.Reads);
    });

    [Fact]
    public Task Fenetre_AllerRetour_RendLaLargeur() => WpfTestHost.Run(() =>
    {
        using var rig = new Rig(ConfigFor(UsagePlacement.Bottom), new CountingProvider("claude"));
        rig.Build();
        var startWidth = rig.Window.Width;
        var startMinWidth = rig.Window.MinWidth;

        rig.ChangePlacement(UsagePlacement.Right);
        Assert.Equal(startWidth + 118, rig.Window.Width);
        Assert.Equal(startMinWidth + 118, rig.Window.MinWidth);

        rig.ChangePlacement(UsagePlacement.Bottom);
        Assert.Equal(startWidth, rig.Window.Width);
        Assert.Equal(startMinWidth, rig.Window.MinWidth);
    });

    [Fact]
    public Task Fenetre_RedimensionneeEntreDeux_RetireSeulementLAjout() => WpfTestHost.Run(() =>
    {
        using var rig = new Rig(ConfigFor(UsagePlacement.Bottom), new CountingProvider("claude"));
        rig.Build();

        rig.ChangePlacement(UsagePlacement.Right);
        rig.Window.Width = 1000;
        rig.ChangePlacement(UsagePlacement.Bottom);

        Assert.Equal(882, rig.Window.Width);
        Assert.Equal(850, rig.Window.MinWidth);
    });

    [Fact]
    public Task Fenetre_DemarrageADroite_LargeurAugmenteDesLaPremiereMiseEnPage() => WpfTestHost.Run(() =>
    {
        using var rig = new Rig(ConfigFor(UsagePlacement.Right), new CountingProvider("claude"));
        rig.Build();

        Assert.Equal(968, rig.Window.Width);
        Assert.Equal(968, rig.Window.MinWidth);
    });

    [Fact]
    public Task Fenetre_ADroite_BarreAligneeSurLesTuiles() => WpfTestHost.Run(() =>
    {
        using var rig = new Rig(ConfigFor(UsagePlacement.Right), new CountingProvider("claude"));
        rig.Build();

        var first = Tile(rig, 0);
        var last = Tile(rig, 23);
        var endOfFirstRow = Tile(rig, 5);
        var barOrigin = rig.Bar.TranslatePoint(new Point(0, 0), rig.Root);
        var firstOrigin = first.TranslatePoint(new Point(0, 0), rig.Root);
        var lastBottom = last.TranslatePoint(new Point(0, last.ActualHeight), rig.Root);
        var rowRightEdge = endOfFirstRow.TranslatePoint(new Point(endOfFirstRow.ActualWidth, 0), rig.Root).X;

        Assert.Equal(first.ActualWidth, rig.Bar.ActualWidth, precision: 0);
        Assert.InRange(barOrigin.Y, firstOrigin.Y - 1, firstOrigin.Y + 1);
        Assert.InRange(barOrigin.Y + rig.Bar.ActualHeight, lastBottom.Y - 1, lastBottom.Y + 1);
        Assert.InRange(barOrigin.X, rowRightEdge + 9, rowRightEdge + 11);
    });

    [Fact]
    public Task Fenetre_ADroite_PaginationResteSousLesTuiles() => WpfTestHost.Run(() =>
    {
        using var rig = new Rig(ConfigFor(UsagePlacement.Right), new CountingProvider("claude"));
        rig.Build();

        var first = Tile(rig, 0);
        var endOfFirstRow = Tile(rig, 5);
        var tilesLeft = first.TranslatePoint(new Point(0, 0), rig.Root).X;
        var tilesRight = endOfFirstRow.TranslatePoint(new Point(endOfFirstRow.ActualWidth, 0), rig.Root).X;
        var pagination = rig.Window.PaginationBar;
        var paginationCenter = pagination.TranslatePoint(new Point(pagination.ActualWidth / 2, 0), rig.Root).X;

        Assert.InRange(paginationCenter, (tilesLeft + tilesRight) / 2 - 1, (tilesLeft + tilesRight) / 2 + 1);
    });

    [Fact]
    public Task Fenetre_AutoRechoisi_LeveLaSuspensionSansRelecture() => WpfTestHost.Run(() =>
    {
        var config = ConfigFor(UsagePlacement.Right);
        config.FocusMode = UsageFocusMode.AutoClosestToLimit;
        var claude = new CountingProvider("claude", session: 10);
        var codex = new CountingProvider("codex", session: 90);
        using var rig = new Rig(config, claude, codex);
        rig.Build();
        WpfTestHost.PumpUntilCompleted(rig.Usage.RefreshAsync());
        rig.Usage.Cycle(+1);
        Assert.False(rig.Usage.IsAutoActive);

        UsageConfigService.NotifyAutoChosen();
        WpfTestHost.Flush();

        Assert.True(rig.Usage.IsAutoActive);
        Assert.Equal("codex", rig.Usage.SoloProviderId);
        Assert.Equal(1, claude.Reads);   // le seul relevé du test : le choix n'en lance pas d'autre
    });

    [Fact]
    public Task Fenetre_Fermee_NeSuitPlusLeChoixAuto() => WpfTestHost.Run(() =>
    {
        var config = ConfigFor(UsagePlacement.Right);
        config.FocusMode = UsageFocusMode.AutoClosestToLimit;
        using var rig = new Rig(config, new CountingProvider("claude", session: 10), new CountingProvider("codex", session: 90));
        rig.Build();
        WpfTestHost.PumpUntilCompleted(rig.Usage.RefreshAsync());
        rig.Usage.Cycle(+1);

        rig.Window.DetachFromStatics();
        UsageConfigService.NotifyAutoChosen();
        WpfTestHost.Flush();

        Assert.False(rig.Usage.IsAutoActive);
    });
}
