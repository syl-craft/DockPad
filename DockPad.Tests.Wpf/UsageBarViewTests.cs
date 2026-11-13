using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DockPad.Services.Localization;
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
        string sessionLabel = "", string weekLabel = "", string usageUrl = "")
        : IUsageProvider
    {
        public string Id => id;
        public string Name => name ?? id;

        public AiProbe Probe() => new() { Available = true, DisplayName = id };

        public Task<AiUsage?> ReadAsync(CancellationToken ct) => Task.FromResult<AiUsage?>(new AiUsage
        {
            ProviderId = id, Name = name ?? id, Glyph = "X", AccentColor = "#123456", QuotaNotice = notice,
            UsageUrl = usageUrl,
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

    [Theory]
    [InlineData(SideStripStyle.Columns)]
    [InlineData(SideStripStyle.HeroStack)]
    [InlineData(SideStripStyle.Rings)]
    [InlineData(SideStripStyle.Reservoir)]
    public Task Cartes_LiaisonsDesTroisQuotasDansChaqueStyle(SideStripStyle style) => WpfTestHost.Run(() =>
    {
        var rig = Build(style);
        try
        {
            var cards = Descendants<FrameworkElement>(rig.Control)
                .Select(UsageHover.GetCard).OfType<UsageHoverCard>().ToList();
            Assert.Equal(3, cards.Count);
            Assert.Equal(2, cards.Where(card => card.ProviderId == "claude").Count());
            Assert.Contains(cards, card => card.ProviderId == "codex");
        }
        finally
        {
            rig.Model.Dispose();
            rig.Feed.Stop();
            rig.Usage.Stop();
        }
    });

    [Fact]
    public Task Cartes_Bandeau_LiaisonsDesDeuxJauges() => WpfTestHost.Run(() =>
    {
        var rig = Build(SideStripStyle.Columns);
        try
        {
            var panel = new UsagePanel { DataContext = rig.Usage };
            panel.Measure(new Size(698, 90));
            panel.Arrange(new Rect(0, 0, 698, 90));
            panel.UpdateLayout();
            var cards = Descendants<FrameworkElement>(panel).Select(UsageHover.GetCard).OfType<UsageHoverCard>().ToList();
            Assert.Equal(2, cards.Count);
            Assert.All(cards, card => Assert.Equal("claude", card.ProviderId));
        }
        finally
        {
            rig.Model.Dispose();
            rig.Feed.Stop();
            rig.Usage.Stop();
        }
    });

    private static ToolTipEventArgs RaiseTooltip(FrameworkElement element)
    {
        var args = Activator.CreateInstance(typeof(ToolTipEventArgs),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            binder: null, args: [true], culture: null) as ToolTipEventArgs
            ?? throw new InvalidOperationException("Cannot create tooltip event arguments.");
        element.RaiseEvent(args);
        return args;
    }

    [Fact]
    public Task Carte_Vue_LongNomEtChangementDeTheme() => WpfTestHost.Run(() =>
    {
        var previousTheme = ThemeService.CurrentIsDark;
        var previousCulture = Loc.Current;
        try
        {
            Loc.SetCulture(System.Globalization.CultureInfo.GetCultureInfo("fr"));
            var quota = new UsageWindow { UsedPct = 85, ResetsAt = Now.AddHours(2).AddMinutes(5), Label = "Gemini" };
            var usage = new AiUsage
            {
                ProviderId = "codex", Name = "Assistant de programmation internationale", Glyph = "X", AccentColor = "#123456",
                Session = quota, Week = new UsageWindow { UsedPct = 15, ResetsAt = Now.AddDays(4).AddHours(23) },
                UsageUrl = "https://example.com/usage",
            };
            var card = UsageHoverCard.Create(usage, quota, 15, Now);
            var view = new UsageHoverCardView { DataContext = card };
            Brush? previousBackground = null;
            foreach (var dark in new[] { false, true })
            {
                ThemeService.Apply(dark);
                WpfTestHost.Flush();
                view.Measure(new Size(250, double.PositiveInfinity));
                view.Arrange(new Rect(new Point(), view.DesiredSize));
                view.UpdateLayout();
                Assert.Equal(250, view.ActualWidth);
                var border = Assert.IsType<Border>(view.Content);
                Assert.Same(Application.Current.FindResource("Brush.SurfaceCard"), border.Background);
                if (previousBackground != null) Assert.NotSame(previousBackground, border.Background);
                previousBackground = border.Background;
                var texts = Descendants<TextBlock>(view).Select(text => text.Text).ToList();
                Assert.Contains(card.Header, texts);
                Assert.Contains(card.ResetRelative, texts);
                Assert.Contains(card.Note, texts);
            }
        }
        finally
        {
            ThemeService.Apply(previousTheme);
            Loc.SetCulture(previousCulture);
        }
    });

    [Fact]
    public Task Cartes_InfobullesDesTextesEnfantsSupprimees_ChargeConservee() => WpfTestHost.Run(() =>
    {
        var rig = Build(SideStripStyle.Columns);
        try
        {
            var quotaText = Descendants<TextBlock>(rig.Control)
                .Where(text => text.DataContext is BarColumn { ProviderId.Length: > 0 }).First();
            var loadText = Descendants<TextBlock>(rig.Control)
                .Where(text => text.DataContext is BarColumn { ProviderId.Length: 0 }).First();
            Assert.True(RaiseTooltip(quotaText).Handled);
            Assert.False(RaiseTooltip(loadText).Handled);

            var panel = new UsagePanel { DataContext = rig.Usage };
            panel.Measure(new Size(698, 90));
            panel.Arrange(new Rect(0, 0, 698, 90));
            panel.UpdateLayout();
            var gaugeText = Descendants<TextBlock>(panel)
                .Where(text => text.DataContext is UsageGaugeItem && text.ToolTip != null).First();
            Assert.True(RaiseTooltip(gaugeText).Handled);
        }
        finally
        {
            rig.Model.Dispose();
            rig.Feed.Stop();
            rig.Usage.Stop();
        }
    });

    [Theory]
    [InlineData(SideStripStyle.Reservoir)]
    [InlineData(SideStripStyle.Rings)]
    public Task PetiteJauge_BoutonAccessible_ChoisitLaFenetre(SideStripStyle style) => WpfTestHost.Run(() =>
    {
        var rig = Build(style);
        var button = Descendants<Button>(rig.Control)
            .Where(candidate => candidate.DataContext is BarRing { ProviderId: "codex" }).First();

        Assert.True(button.Focusable);
        Assert.True(button.IsTabStop);
        Assert.Equal(System.Windows.Input.Cursors.Hand, button.Cursor);
        Assert.False(string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(button)));
        Assert.Equal(Loc.F("UsageBar_Enlarge_Tooltip", ((BarRing)button.DataContext).Tooltip), button.ToolTip);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        rig.Layout();

        Assert.Equal("codex", rig.Usage.SoloProviderId);
        Assert.Equal("27%", rig.Model.Reservoir?.LevelText);
        Assert.Equal("27%", rig.Model.Rings?.CenterText);
    });

    [Theory]
    [InlineData(SideStripStyle.Reservoir, BarGaugeKind.Cpu, "CPU")]
    [InlineData(SideStripStyle.Reservoir, BarGaugeKind.Memory, "RAM")]
    public Task PetiteJaugeCharge_ClicMetLaChargeEnGrand(SideStripStyle style, BarGaugeKind kind, string label) => WpfTestHost.Run(() =>
    {
        var rig = Build(style);
        var button = Descendants<Button>(rig.Control)
            .Where(candidate => candidate.DataContext is BarRing ring && ring.Selection?.Kind == kind).First();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        rig.Layout();

        Assert.Equal(label, rig.Model.FeaturedGauge?.Name);
        var logo = Descendants<Button>(rig.Control)
            .Where(candidate => candidate.DataContext is BarFeaturedGauge).Single();
        Assert.False(logo.IsEnabled);
        Assert.Equal(System.Windows.Input.Cursors.Arrow, logo.Cursor);
        Assert.Equal(label, System.Windows.Automation.AutomationProperties.GetName(logo));
    });

    [Theory]
    [InlineData(SideStripStyle.Reservoir)]
    public Task Charge_Mesure_GardeLesBoutonsEtActualiseLeurInfobulle(SideStripStyle style) => WpfTestHost.Run(() =>
    {
        var rig = Build(style);
        var before = Descendants<Button>(rig.Control).Where(button => button.DataContext is BarRing
            { Selection.Kind: BarGaugeKind.Cpu or BarGaugeKind.Memory }).ToArray();
        var tooltip = before[0].ToolTip;

        rig.Measure(new SystemLoad(40, 62));

        var after = Descendants<Button>(rig.Control).Where(button => button.DataContext is BarRing
            { Selection.Kind: BarGaugeKind.Cpu or BarGaugeKind.Memory }).ToArray();
        Assert.Equal(2, after.Length);
        Assert.Same(before[0], after[0]);
        Assert.Same(before[1], after[1]);
        Assert.NotEqual(tooltip, after[0].ToolTip);
        Assert.Contains("40", after[0].ToolTip.ToString());
    });

    [Fact]
    public Task Anneaux_DoubleCharge_Accessible_CycleEtResteAffiche() => WpfTestHost.Run(() =>
    {
        var rig = Build(SideStripStyle.Rings);
        var button = Descendants<Button>(rig.Control).Where(candidate => candidate.DataContext is BarLoad).Single();
        Assert.Equal("CPU et mémoire", System.Windows.Automation.AutomationProperties.GetName(button));
        Assert.Equal("Afficher en grand", button.ToolTip);
        Assert.True(button.Focusable);
        Assert.True(button.IsTabStop);
        Assert.Equal(System.Windows.Input.Cursors.Hand, button.Cursor);
        Assert.DoesNotContain(Descendants<Button>(rig.Control), candidate => candidate.DataContext is BarRing
            { Selection.Kind: BarGaugeKind.Cpu or BarGaugeKind.Memory });
        Assert.DoesNotContain(Descendants<ProviderIcon>(rig.Control), icon => icon.DataContext is BarFeaturedGauge);
        foreach (var label in new[] { "CPU", "RAM", "claude" })
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            rig.Layout();
            Assert.Equal(label, rig.Model.FeaturedGauge?.Name);
            Assert.Same(button, Descendants<Button>(rig.Control).Where(candidate => candidate.DataContext is BarLoad).Single());
            Assert.Equal(Visibility.Visible, button.Visibility);
            Assert.Single(Descendants<TextBlock>(rig.Control), text => text.Text == label);
            if (label != "claude") Assert.Equal("", rig.Model.Rings?.Legend);
        }

        rig.Measure(new SystemLoad(40, 62));
        Assert.Same(button, Descendants<Button>(rig.Control).Where(candidate => candidate.DataContext is BarLoad).Single());
        Assert.Contains(Descendants<TextBlock>(button), text => text.Text == "40%");
        Assert.Equal("CPU / RAM", rig.Model.Load?.Legend);
    });

    [Fact]
    public Task PastilleVide_SansActionNiNomAction() => WpfTestHost.Run(() =>
    {
        var rig = Build(SideStripStyle.Reservoir, new FixedProvider("claude", 62, null));
        var empty = Descendants<Button>(rig.Control)
            .Where(button => button.DataContext is BarRing { IsEmpty: true }).Single();

        Assert.False(empty.Focusable);
        Assert.False(empty.IsTabStop);
        Assert.Null(empty.ToolTip);
        Assert.Equal("", System.Windows.Automation.AutomationProperties.GetName(empty));
    });

    [Theory]
    [InlineData(SideStripStyle.Reservoir, "https://example.test/usage", true)]
    [InlineData(SideStripStyle.Rings, "https://example.test/usage", true)]
    [InlineData(SideStripStyle.Reservoir, "file:///C:/test", false)]
    [InlineData(SideStripStyle.Rings, "", false)]
    public Task GrandLogo_UrlValideSeulement(SideStripStyle style, string url, bool enabled) => WpfTestHost.Run(() =>
    {
        var rig = Build(style, new FixedProvider("claude", 62, 44, usageUrl: url));
        var button = Descendants<Button>(rig.Control)
            .Where(candidate => candidate.DataContext is BarFeaturedGauge).Single();
        Assert.Equal(enabled, button.IsEnabled);
        Assert.Equal(enabled ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow, button.Cursor);
        Assert.True(button.Focusable);
        Assert.True(button.IsTabStop);
        Assert.False(string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(button)));
    });

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
    public Task Reservoir_ReleveIdentique_GardeLesInstancesDeQuota() => WpfTestHost.Run(() =>
    {
        var rig = Build(SideStripStyle.Reservoir);
        var before = rig.Model.Reservoir?.Dots[0];
        Assert.NotNull(before);

        WpfTestHost.PumpUntilCompleted(rig.Usage.RefreshAsync());
        rig.Layout();

        Assert.Same(before, rig.Model.Reservoir?.Dots[0]);
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
