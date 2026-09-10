using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DockPad.Services;
using DockPad.Views;

namespace DockPad.Tests;

public class TileHintOverlayTests
{
    [Fact]
    public async Task SwitchingSides_RendersTheKeysThatWillActuallyExecute_AndClearsOldBadges()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var grid = new UniformGrid { Rows = 4, Columns = 6 };
                var overlay = new TileHintOverlay(grid);
                foreach (var firstHalf in new[] { true, false, true })
                {
                    overlay.Show(firstHalf);
                    Assert.Equal(Visibility.Visible, grid.Visibility);
                    Assert.Equal(24, grid.Children.Count);
                    Assert.Equal(24, grid.Children.Cast<Grid>().Sum(c => c.Children.Count));
                    for (var key = 0; key <= 11; key++)
                    {
                        var (row, col) = TileHintMap.CellFor(key, firstHalf);
                        var cell = (Grid)grid.Children[row * 6 + col];
                        var text = Assert.Single(cell.Children.OfType<Border>()
                            .Select(b => b.Child).OfType<TextBlock>());
                        Assert.Equal(key switch { 10 => "↑", 11 => "↓", _ => key.ToString() }, text.Text);
                    }
                }
                overlay.Hide();
                Assert.Equal(Visibility.Collapsed, grid.Visibility);
                Assert.All(grid.Children.Cast<Grid>(), cell => Assert.Empty(cell.Children));
                done.SetResult();
            }
            catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
