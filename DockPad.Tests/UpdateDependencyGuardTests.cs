using System.IO;
using System.Runtime.CompilerServices;

namespace DockPad.Tests;

public class UpdateDependencyGuardTests
{
    [Fact]
    public void GenericUpdatesLiveOnlyInMue()
    {
        var root = RepoRoot();
        Assert.False(Directory.Exists(Path.Combine(root, "Services", "Updates")));
        foreach (var name in new[] { "UpdatesDialog.xaml", "UpdatesDialog.xaml.cs", "UpdateBlockersDialog.xaml", "UpdateBlockersDialog.xaml.cs" })
            Assert.False(File.Exists(Path.Combine(root, "Dialogs", name)));
    }

    private static string RepoRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path) ?? throw new InvalidOperationException(), ".."));
}
