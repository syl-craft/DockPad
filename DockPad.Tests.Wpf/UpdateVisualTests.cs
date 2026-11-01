using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DockPad.Models;
using DockPad.Services;
using DockPad.Services.Updates;
using DockPad.Services.Usage;

namespace DockPad.Tests;

public class UpdateVisualTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task GroupCountHasAVisibleBackgroundInBothThemes(bool dark) => WpfTestHost.Run(() =>
    {
        ThemeService.Apply(dark);
        var window = new UpdateBlockersDialog("unused", Enumerable.Range(1, 3)
            .Select(pid => new UpdateBlocker { Pid = pid, Name = "Editor" }).ToArray(),
            refresh: () => Task.FromResult<IReadOnlyList<UpdateBlocker>>([]), close: (_, _) => Task.FromResult(false));
        try
        {
            Layout(window);
            var count = Descendants((DependencyObject)window.Content).OfType<TextBlock>().Single(t => t.Text == "3");
            var badge = Assert.IsType<Border>(VisualTreeHelper.GetParent(count));
            var badgeColor = Assert.IsType<SolidColorBrush>(badge.Background).Color;
            var cardColor = Assert.IsType<SolidColorBrush>(window.FindResource("Brush.SurfaceCard")).Color;
            Assert.True(Math.Abs(badgeColor.R - cardColor.R) >= 12, "Le compteur doit se détacher du fond de sa carte.");
        }
        finally { window.Close(); ThemeService.Apply(false); }
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task FooterHoverIsDecoratedOnlyWhenAReleaseExists(bool release, bool dark) => WpfTestHost.Run(() =>
    {
        ThemeService.Apply(dark);
        var usage = new UsageViewModel(new UsageService([]), () => new UsageConfig { Enabled = false });
        using var updates = new UpdateService(new FixtureBackend(release));
        WpfTestHost.PumpUntilCompleted(updates.CheckAsync());
        var window = new QuickAccessWindow(usage, new FakeSystemLoadMonitor(), updates: updates);
        try
        {
            Layout(window);
            var button = (Button)window.FindName("TxtVersion");
            var badge = Assert.IsType<Border>(button.Template.FindName("Badge", button));
            var before = Assert.IsType<SolidColorBrush>(badge.Background).Color;
            var hoverKey = typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null)
                as DependencyPropertyKey ?? throw new InvalidOperationException("WPF mouse-over key not found");
            button.SetValue(hoverKey, true);
            var after = Assert.IsType<SolidColorBrush>(badge.Background).Color;
            if (release)
            {
                Assert.True(button.Padding.Left >= 9 && button.Padding.Right >= 9);
                Assert.NotEqual(before, after);
            }
            else Assert.Equal(before, after);
        }
        finally { window.DetachFromStatics(); window.Close(); ThemeService.Apply(false); }
    });

    private static void Layout(Window window)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(window.Width, 800));
        root.Arrange(new Rect(0, 0, window.Width, 800));
        root.UpdateLayout();
    }

    private sealed class FixtureBackend(bool release) : IUpdateBackend
    {
        public bool IsInstalled => true;
        public string CurrentVersion => "1.31.1";
        public string ContentDirectory => "unused";
        public Task<UpdateRelease?> CheckAsync() => Task.FromResult<UpdateRelease?>(release ? new("1.32.0", "", new object()) : null);
        public Task DownloadAsync(UpdateRelease value, Action<int> progress, CancellationToken token) => throw new NotSupportedException();
        public void Apply(UpdateRelease value) => throw new NotSupportedException();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
