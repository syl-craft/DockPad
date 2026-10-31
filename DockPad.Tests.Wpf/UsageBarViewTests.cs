using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DockPad.Models;
using DockPad.Services;
using DockPad.Services.StreamDock;
using DockPad.Services.Usage;
using DockPad.Views;

namespace DockPad.Tests;

/// <summary>
/// La barre seule, sans fenêtre : ses conteneurs ne sont pas recréés par une mesure de charge ni par un relevé
/// identique, et seule une ligne « autre fournisseur » est un geste de sélection.
/// </summary>
public class UsageBarViewTests
{
    private static readonly DateTime Now = new(2026, 8, 20, 12, 0, 0);

    private sealed class FixedProvider(string id, int? session, int? week, string? name = null, string notice = "",
        string sessionLabel = "", string weekLabel = "")
        : IUsageProvider
    {
        public string Id => id;
        public string Name => name ?? id;

        public AiProbe Probe() => new() { Available = true, DisplayName = id };

        public Task<AiUsage?> ReadAsync(CancellationToken ct) => Task.FromResult<AiUsage?>(new AiUsage
        {
            ProviderId = id, Name = name ?? id, Glyph = "X", AccentColor = "#123456", QuotaNotice = notice,
            Session = session == null ? null : new UsageWindow { UsedPct = session.Value, Label = sessionLabel },
            Week = week == null ? null : new UsageWindow { UsedPct = week.Value, Label = weekLabel },
        });
    }

    private sealed class Rig
    {
        public required UsageViewModel Usage { get; init; }
        public required SystemLoadFeed Feed { get; init; }
        public required FakeSystemLoadMonitor Monitor { get; init; }
        public required ManualTimeProvider Time { get; init; }
        public required UsageBarViewModel Model { get; init; }
        public required UsageBar Control { get; init; }

        public void Layout()
        {
            Control.Measure(new Size(108, 390));
            Control.Arrange(new Rect(0, 0, 108, 390));
            Control.UpdateLayout();
        }

        /// <summary>
        /// Une mesure : le minuteur avance, la mesure se publie sur le thread de la fenêtre.
        /// </summary>
        public void Measure(SystemLoad load)
        {
            Monitor.Value = load;
            Time.Advance(TimeSpan.FromSeconds(5));
            Feed.PendingSample.Wait(TimeSpan.FromSeconds(10));
            WpfTestHost.Flush();
            Layout();
        }
    }

    private static Rig Build(SideStripStyle style, FixedProvider? claude = null)
    {
        var config = new UsageConfig { Placement = UsagePlacement.Right, BarStyle = style };
        config.Providers.Add(new AiProviderEntry { Id = "claude", Name = "claude", Order = 0 });
        config.Providers.Add(new AiProviderEntry { Id = "codex", Name = "codex", Order = 1 });
        var service = new UsageService([claude ?? new FixedProvider("claude", 62, 44), new FixedProvider("codex", null, 27)]);
        var usage = new UsageViewModel(service, () => config, () => Now);
        WpfTestHost.PumpUntilCompleted(usage.RefreshAsync());

        var monitor = new FakeSystemLoadMonitor();
        var time = new ManualTimeProvider();
        var feed = new SystemLoadFeed(monitor, time, new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        feed.Start();
        feed.PendingSample.Wait(TimeSpan.FromSeconds(10));
        WpfTestHost.Flush();

        var model = new UsageBarViewModel(usage, feed, () => Now);
        var rig = new Rig
        {
            Usage = usage, Feed = feed, Monitor = monitor, Time = time, Model = model,
            Control = new UsageBar { ViewModel = model },
        };
        rig.Layout();
        return rig;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    private static ItemsControl ItemsOf(UsageBar bar, object source) =>
        Descendants<ItemsControl>(bar).Where(items => ReferenceEquals(items.ItemsSource, source)).First();

    [Fact]
    public Task Colonnes_Mesure_GardeLeConteneurDesColonnesDeQuota() => WpfTestHost.Run(() =>
    {
        var rig = Build(SideStripStyle.Columns);
        var columns = ItemsOf(rig.Control, rig.Model.Columns);
        var quotaBefore = columns.ItemContainerGenerator.ContainerFromIndex(0);
        var secondQuotaBefore = columns.ItemContainerGenerator.ContainerFromIndex(1);

        rig.Measure(new SystemLoad(40, 62));

        Assert.Same(quotaBefore, columns.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Same(secondQuotaBefore, columns.ItemContainerGenerator.ContainerFromIndex(1));
        Assert.Equal("40", ((BarColumn)columns.Items[^2]).NumberText);
    });

    [Fact]
    public Task Reservoir_Mesure_GardeLeConteneurDesPastillesDeQuota() => WpfTestHost.Run(() =>
    {
        var rig = Build(SideStripStyle.Reservoir);
        var dots = ItemsOf(rig.Control, rig.Model.Reservoir?.Dots ?? throw new InvalidOperationException("No reservoir."));
        var quotaDotBefore = dots.ItemContainerGenerator.ContainerFromIndex(0);

        rig.Measure(new SystemLoad(40, 62));

        Assert.Same(quotaDotBefore, dots.ItemContainerGenerator.ContainerFromIndex(0));
    });

    [Fact]
    public Task Logos_ReleveIdentique_GardeLesConteneurs() => WpfTestHost.Run(() =>
    {
        var rig = Build(SideStripStyle.Columns);
        var logos = ItemsOf(rig.Control, rig.Model.Logos);
        var before = logos.ItemContainerGenerator.ContainerFromIndex(0);
        var secondBefore = logos.ItemContainerGenerator.ContainerFromIndex(1);

        WpfTestHost.PumpUntilCompleted(rig.Usage.RefreshAsync());
        rig.Layout();

        Assert.Same(before, logos.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Same(secondBefore, logos.ItemContainerGenerator.ContainerFromIndex(1));
    });

    [Fact]
    public Task Heros_SecondeFenetreDeLaCarte_NEstPasUnBouton() => WpfTestHost.Run(() =>
    {
        var rig = Build(SideStripStyle.HeroStack);
        var focusId = rig.Usage.SoloProviderId;

        var rowButtons = Descendants<Button>(rig.Control)
            .Select(button => button.DataContext)
            .OfType<BarHeroRow>()
            .ToList();

        Assert.Equal("claude", focusId);
        Assert.NotNull(rig.Model.HeroCard?.SecondWindow);
        Assert.DoesNotContain(rowButtons, row => row.ProviderId == focusId);
        Assert.Contains(rowButtons, row => row.ProviderId == "codex");
    });

    /// <summary>
    /// L'infobulle de l'élément ou, à défaut, celle du premier ancêtre qui en porte une dans la barre.
    /// </summary>
    private static string TooltipOf(FrameworkElement element, UsageBar bar)
    {
        for (DependencyObject? current = element; current != null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { ToolTip: { } tip }) return tip.ToString() ?? "";
            if (ReferenceEquals(current, bar)) break;
        }
        return "";
    }

    [Theory]
    [InlineData(SideStripStyle.HeroStack)]
    [InlineData(SideStripStyle.Rings)]
    [InlineData(SideStripStyle.Reservoir)]
    public Task NomRaccourci_PorteUneInfobulleAvecLeNomComplet(SideStripStyle style) => WpfTestHost.Run(() =>
    {
        const string fullName = "Assistant de programmation internationale";
        var rig = Build(style, new FixedProvider("claude", 62, 44, name: fullName));

        var shortened = rig.Model.HeroCard?.Name ?? throw new InvalidOperationException("No hero card.");
        var texts = Descendants<TextBlock>(rig.Control).Where(text => text.Text == shortened).ToList();

        Assert.NotEmpty(texts);
        Assert.All(texts, text => Assert.Contains(fullName, TooltipOf(text, rig.Control)));
    });

    [Theory]
    [InlineData(SideStripStyle.HeroStack)]
    [InlineData(SideStripStyle.Rings)]
    [InlineData(SideStripStyle.Reservoir)]
    public Task TitreDeNoticeRaccourci_PorteUneInfobulleAvecLaNoticeComplete(SideStripStyle style) => WpfTestHost.Run(() =>
    {
        const string notice = "Quota indisponible pour ce fournisseur parce que le service ne répond plus — réessai dans 4 min";
        var rig = Build(style, new FixedProvider("claude", null, null, notice: notice));

        var title = rig.Model.HeroCard?.NoticeTitle ?? throw new InvalidOperationException("No hero card.");
        var texts = Descendants<TextBlock>(rig.Control).Where(text => text.Text == title).ToList();

        Assert.NotEmpty(texts);
        Assert.All(texts, text => Assert.Contains(notice, TooltipOf(text, rig.Control)));
    });

    [Fact]
    public Task AnneauxLegende_DeuxFenetresNommees_PorteLesDeuxNomsComplets() => WpfTestHost.Run(() =>
    {
        var rig = Build(SideStripStyle.Rings, new FixedProvider("claude", 38, 75, sessionLabel: "Gemini", weekLabel: "Claude · GPT"));

        var legend = rig.Model.Rings?.Legend ?? throw new InvalidOperationException("No rings.");
        var texts = Descendants<TextBlock>(rig.Control).Where(text => text.Text == legend).ToList();

        Assert.NotEmpty(texts);
        Assert.All(texts, text =>
        {
            var tooltip = TooltipOf(text, rig.Control);
            Assert.Contains("Gemini", tooltip);
            Assert.Contains("Claude · GPT", tooltip);
        });
    });
}
