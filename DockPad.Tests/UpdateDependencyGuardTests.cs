using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DockPad.Tests;

public class UpdateDependencyGuardTests
{
    [Fact]
    public void GenericUpdatesDoNotDependOnTheApplication()
    {
        var root = RepoRoot();
        var files = Directory.GetFiles(Path.Combine(root, "Services", "Updates"), "*.cs", SearchOption.AllDirectories)
            .Concat(new[] { "UpdatesDialog.xaml.cs", "UpdateBlockersDialog.xaml.cs" }
                .Select(name => Path.Combine(root, "Dialogs", name)));
        var forbidden = new Regex(@"\b(?:Loc\s*\.|LogService\b|AppSettingsService\b|AppPaths\b|AppInfo\b|AppDialog\b|App\s*\.|AppInstallation\b|DockPad\.Services\.(?!Updates\b))");
        var violations = files.SelectMany(file => File.ReadLines(file)
            .Select((line, index) => (line, index))
            .Where(item => forbidden.IsMatch(item.line))
            .Select(item => $"{Path.GetRelativePath(root, file)}:{item.index + 1}: {item.line.Trim()}"));

        Assert.Empty(violations);
    }

    private static string RepoRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path) ?? throw new InvalidOperationException(), ".."));
}
