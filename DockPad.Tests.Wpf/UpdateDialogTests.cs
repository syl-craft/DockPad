using Mue.Wpf;
using DockPad.Services.UpdateHost;
using System.Windows;
using System.Windows.Controls;
using DockPad.Services.Localization;
using Mue;

namespace DockPad.Tests;

public class UpdateDialogTests
{
    [Fact]
    public async Task IncompleteClosureStaysInTheSameWindowAndForceIsExplicit() => await WpfTestHost.Run(() =>
    {
        var attempts = new List<(int Pid, bool Force)>();
        UpdateBlocker[] blockers = [
            new() { Pid = 1, Name = "DockPad", IsRelay = true, ParentName = "Claude Code", Selected = true },
            new() { Pid = 2, Name = "Editor" },
        ];
        var window = new UpdateBlockersDialog(UpdateBlockerQuery.ForDirectory("unused"), blockers,
            host: new DockPadUpdateDialogHost(), shutdownBehavior: UpdateShutdownBehavior.Refuse, refresh: () => Task.FromResult<IReadOnlyList<UpdateBlocker>>(blockers),
            close: (blocker, force) => { attempts.Add((blocker.Pid, force)); return Task.FromResult(UpdateBlockerCloseResult.StillRunning); });
        var button = (Button)window.FindName("CloseButton");
        var heading = (TextBlock)window.FindName("Heading");
        Assert.Equal(Loc.F("Update_CloseCount", 1), button.Content);

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(new[] { (1, false) }, attempts);
        Assert.Equal(Loc.F("Update_IncompleteTitle", 1), heading.Text);
        Assert.Equal(new[] { 1 }, ((ItemsControl)window.FindName("RemainingProcesses")).Items.Cast<UpdateBlocker>().Select(b => b.Pid));
        Assert.Equal(2, Assert.Single(((ItemsControl)window.FindName("Processes")).Items.Cast<UpdateBlockerGroup>()).Processes.Single().Pid);
        Assert.Equal(Visibility.Visible, ((Border)window.FindName("ForceWarning")).Visibility);
        Assert.Equal(Loc.T("Update_ForceInstall"), button.Content);

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(new[] { (1, false), (1, true) }, attempts);
        window.Close();
    });

    [Fact]
    public async Task SuccessfulSelectedClosuresReturnToTheNormalListWithUncheckedPrograms() => await WpfTestHost.Run(() =>
    {
        var editor = new UpdateBlocker { Pid = 2, Name = "Editor" };
        var window = new UpdateBlockersDialog(UpdateBlockerQuery.ForDirectory("unused"),
            [new() { Pid = 1, Name = "DockPad", IsRelay = true }, editor],
            host: new DockPadUpdateDialogHost(), shutdownBehavior: UpdateShutdownBehavior.Refuse, refresh: () => Task.FromResult<IReadOnlyList<UpdateBlocker>>([editor]), close: (_, _) => Task.FromResult(UpdateBlockerCloseResult.Closed));
        var button = (Button)window.FindName("CloseButton");

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(Visibility.Collapsed, ((Border)window.FindName("ForceWarning")).Visibility);
        Assert.Equal(Visibility.Visible, ((ItemsControl)window.FindName("Processes")).Visibility);
        Assert.Equal(Loc.F("Update_CloseCount", 0), button.Content);
        Assert.False(button.IsEnabled);
        window.Close();
    });

    [Fact]
    public async Task SelectingAGroupUpdatesThePrimaryCountWithoutAConfirmationDialog() => await WpfTestHost.Run(() =>
    {
        UpdateBlocker[] blockers = [new() { Pid = 1, Name = "Editor" }, new() { Pid = 2, Name = "Editor" }];
        var window = new UpdateBlockersDialog(UpdateBlockerQuery.ForDirectory("unused"), blockers,
            host: new DockPadUpdateDialogHost(), shutdownBehavior: UpdateShutdownBehavior.Refuse, refresh: () => Task.FromResult<IReadOnlyList<UpdateBlocker>>(blockers), close: (_, _) => Task.FromResult(UpdateBlockerCloseResult.StillRunning));
        var button = (Button)window.FindName("CloseButton");
        Assert.False(button.IsEnabled);
        var group = Assert.Single(((ItemsControl)window.FindName("Processes")).Items.Cast<UpdateBlockerGroup>());

        group.Selected = true;

        Assert.True(button.IsEnabled);
        Assert.Equal(Loc.F("Update_CloseCount", 2), button.Content);
        window.Close();
    });
}
