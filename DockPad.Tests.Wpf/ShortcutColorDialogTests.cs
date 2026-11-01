using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DockPad.Models;
using DockPad.Services;
using DockPad.Services.Usage;
using DockPad.Views;

namespace DockPad.Tests;

public class ShortcutColorDialogTests
{
    private static T Control<T>(ShortcutDialog dialog, string name) where T : FrameworkElement =>
        Assert.IsType<T>(dialog.FindName(name));

    private static void ChooseType(ShortcutDialog dialog, ShortcutType type)
    {
        var combo = Control<ComboBox>(dialog, "CmbType");
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Where(item => Equals(item.Tag, type.ToString())).Single();
    }

    private static void Save(ShortcutDialog dialog)
    {
        Exception? failure = null;
        dialog.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                var save = typeof(ShortcutDialog).GetMethod("Save_Click", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException();
                save.Invoke(dialog, [dialog, new RoutedEventArgs()]);
            }
            catch (Exception ex) { failure = ex; dialog.Close(); }
        }));
        var saved = dialog.ShowDialog();
        Assert.Null(failure);
        Assert.True(saved);
    }

    [Theory]
    [InlineData("\"C:\\dev\\DockPad\"", @"C:\dev\DockPad")]
    [InlineData("  \"C:\\dev\\DockPad\"  ", @"C:\dev\DockPad")]
    [InlineData("  \"C:\\dev\\Nom avec espaces\"  ", @"C:\dev\Nom avec espaces")]
    public Task NouveauDossier_CheminCopie_MemeCouleurQueLOngletTerminal(string folder, string terminalFolder) => WpfTestHost.Run(() =>
    {
        var dialog = new ShortcutDialog();
        try
        {
            ChooseType(dialog, ShortcutType.OpenFolder);
            Control<TextBox>(dialog, "TxtCommand").Text = folder;
            Assert.Equal(TerminalDetectionService.Expand("{color}", terminalFolder, command: ""),
                Control<TextBox>(dialog, "TxtBandColor").Text);
        }
        finally { dialog.Close(); }
    });

    [Fact]
    public Task ModificationExistanteSansCouleur_EnregistrerResteSansCouleur() => WpfTestHost.Run(() =>
    {
        var dialog = new ShortcutDialog(new ShortcutEntry
        {
            Name = "Ancien", Type = ShortcutType.OpenFolder, Command = @"C:\dev\DockPad",
        });

        Save(dialog);

        Assert.Null(dialog.Entry.Color);
        Assert.DoesNotContain("\"color\"", System.Text.Json.JsonSerializer.Serialize(dialog.Entry));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CouleurDuType_ApercuIdentiqueALaBandeDuTypeChoisi(bool dark) => WpfTestHost.Run(() =>
    {
        var previousTheme = ThemeService.CurrentIsDark;
        ThemeService.Apply(dark);
        var dialog = new ShortcutDialog();
        try
        {
            var occupied = typeof(QuickAccessWindow).GetMethod("Occupied", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException();
            foreach (var type in Enum.GetValues<ShortcutType>())
            {
                ChooseType(dialog, type);
                Control<TextBox>(dialog, "TxtBandColor").Text = "#123456";
                Control<Button>(dialog, "BtnTypeColor").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                var cell = Assert.IsType<TileCell>(occupied.Invoke(null, [new ShortcutEntry { Type = type }]));
                Assert.Equal(Assert.IsType<SolidColorBrush>(cell.Band).Color,
                    Assert.IsType<SolidColorBrush>(Control<Border>(dialog, "BdBandColor").Background).Color);
            }
            // Le choix « couleur du type » suit aussi un changement ultérieur de type.
            ChooseType(dialog, ShortcutType.OpenFolder);
            Assert.Equal("#FFF5CC80", Assert.IsType<SolidColorBrush>(Control<Border>(dialog, "BdBandColor").Background).Color.ToString());
        }
        finally { dialog.Close(); ThemeService.Apply(previousTheme); }
    });

    [Fact]
    public Task NouveauDossier_CouleurTerminalSuitLeChemin_JusquAuChoixManuel() => WpfTestHost.Run(() =>
    {
        var dialog = new ShortcutDialog();
        ChooseType(dialog, ShortcutType.OpenFolder);
        var folder = Control<TextBox>(dialog, "TxtCommand");
        folder.Text = @"C:\dev\DockPad";
        var color = Control<TextBox>(dialog, "TxtBandColor");
        Assert.Equal(TerminalDetectionService.TabColor("DockPad"), color.Text);
        folder.Text = @"C:\dev\Autre\";
        Assert.Equal(TerminalDetectionService.TabColor("Autre"), color.Text);

        var palette = Control<WrapPanel>(dialog, "PnlBandColors");
        var swatch = palette.Children.OfType<Button>().Where(button => Equals(button.Tag, "#188038")).Single();
        swatch.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        folder.Text = @"C:\dev\Troisième";
        Assert.Equal("#188038", color.Text);

        Control<Button>(dialog, "BtnTypeColor").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        folder.Text = @"C:\dev\Encore";
        Assert.Equal("", color.Text);
        dialog.Close();
    });

    [Theory]
    [InlineData(null)]
    [InlineData("#123456")]
    public Task ModificationExistante_NeRecalculePasLaCouleur(string? existingColor) => WpfTestHost.Run(() =>
    {
        var dialog = new ShortcutDialog(new ShortcutEntry
        {
            Name = "Ancien", Type = ShortcutType.OpenFolder, Command = @"C:\dev\DockPad", Color = existingColor,
        });
        Assert.Equal(existingColor ?? "", Control<TextBox>(dialog, "TxtBandColor").Text);
        Assert.Equal(existingColor, dialog.Entry.Color);
        Control<TextBox>(dialog, "TxtCommand").Text = @"C:\autre";
        Assert.Equal(existingColor ?? "", Control<TextBox>(dialog, "TxtBandColor").Text);
        dialog.Close();
    });

    [Fact]
    public Task NouvelleTuileProposeeParDepot_ContinueDeSuivreLeDossier() => WpfTestHost.Run(() =>
    {
        var dialog = new ShortcutDialog(new ShortcutEntry
        {
            Name = "Dossier déposé", Type = ShortcutType.OpenFolder, Command = @"C:\dev\DockPad",
            Color = TerminalDetectionService.TabColor("DockPad"),
        }, isNew: true);
        Control<TextBox>(dialog, "TxtCommand").Text = @"C:\dev\Autre";
        Assert.Equal(TerminalDetectionService.TabColor("Autre"), Control<TextBox>(dialog, "TxtBandColor").Text);
        dialog.Close();
    });

    [Fact]
    public Task Nouveau_AutresTypesGardentLaCouleurDuType() => WpfTestHost.Run(() =>
    {
        var dialog = new ShortcutDialog();
        foreach (var type in Enum.GetValues<ShortcutType>().Where(type => type != ShortcutType.OpenFolder))
        {
            ChooseType(dialog, type);
            Control<TextBox>(dialog, "TxtCommand").Text = @"C:\dev\DockPad";
            Assert.Equal("", Control<TextBox>(dialog, "TxtBandColor").Text);
        }
        dialog.Close();
    });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#abcdef")]
    public Task Enregistrer_ConserveLaCouleurAutomatiqueOuLeChoixManuel(string? choice) => WpfTestHost.Run(() =>
    {
        var dialog = new ShortcutDialog();
        ChooseType(dialog, ShortcutType.OpenFolder);
        Control<TextBox>(dialog, "TxtName").Text = "Dossier";
        Control<TextBox>(dialog, "TxtCommand").Text = @"C:\dev\DockPad";
        if (choice == "")
            Control<Button>(dialog, "BtnTypeColor").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        else if (choice != null)
            Control<TextBox>(dialog, "TxtBandColor").Text = choice;

        Save(dialog);
        var expected = choice == null ? TerminalDetectionService.TabColor("DockPad")
            : choice == "" ? null : choice.ToUpperInvariant();
        Assert.Equal(expected, dialog.Entry.Color);
    });

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public Task DepotDossier_CouleurTerminal_DansUneCaseOuUnGroupe(int? slot) => WpfTestHost.Run(() =>
    {
        var files = TileStore.FilesFor(TileTarget.Shortcuts);
        var before = ShortcutService.Load(files.EntriesPath);
        var entries = new List<ShortcutEntry>();
        if (slot != null)
            Assert.True(ShortcutActionService.GroupSetCore(entries, [], 0, 0, 0, TileLayout.Quad, "G", null).Ok);
        ShortcutService.Save(entries, files.EntriesPath);
        var usage = new UsageViewModel(new UsageService([]), () => new UsageConfig { Enabled = false });
        var window = new QuickAccessWindow(usage, new FakeSystemLoadMonitor());
        try
        {
            var folder = Path.Combine(TestProfile.Root, "Dossier déposé");
            Directory.CreateDirectory(folder);
            var drop = typeof(QuickAccessWindow).GetMethod("CreateFolderShortcutFromDrop", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException();
            drop.Invoke(window, [folder, 0, 0, slot]);
            var entry = TileGroupService.Get(ShortcutService.Load(files.EntriesPath), new(0, 0, 0, slot))
                ?? throw new InvalidOperationException();
            Assert.Equal(ShortcutType.OpenFolder, entry.Type);
            Assert.Equal(TerminalDetectionService.TabColor(TerminalDetectionService.FolderName(folder)), entry.Color);
        }
        finally
        {
            window.DetachFromStatics();
            ShortcutService.Save(before, files.EntriesPath);
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task BandeSimpleAffichee_RespecteLaCouleurDansLesDeuxThemes(bool dark) => WpfTestHost.Run(() =>
    {
        var previousTheme = ThemeService.CurrentIsDark;
        ThemeService.Apply(dark);
        var usage = new UsageViewModel(new UsageService([]), () => new UsageConfig { Enabled = false });
        var window = new QuickAccessWindow(usage, new FakeSystemLoadMonitor());
        try
        {
            Assert.Equal(dark, ThemeService.CurrentIsDark);
            var template = Assert.IsType<DataTemplate>(window.FindResource("TileTemplate"));
            var occupied = typeof(QuickAccessWindow).GetMethod("Occupied", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException();
            foreach (var color in new string?[] { "#123456", null })
            {
                var entry = new ShortcutEntry { Type = ShortcutType.OpenFolder, Name = "Dossier", Color = color };
                var button = Assert.IsType<Button>(template.LoadContent());
                button.DataContext = Assert.IsType<TileCell>(occupied.Invoke(null, [entry]));
                button.Measure(new Size(108, 90));
                button.Arrange(new Rect(0, 0, 108, 90));
                WpfTestHost.Flush();
                var frame = Assert.IsType<Border>(VisualTreeHelper.GetChild(button, 0));
                var grid = Assert.IsType<Grid>(frame.Child);
                var band = Assert.Single(grid.Children.OfType<Border>());
                Assert.Equal(4, band.Width);
                Assert.Equal(color == null ? "#FFF5CC80" : "#FF123456",
                    Assert.IsType<SolidColorBrush>(band.Background).Color.ToString());
            }
        }
        finally { window.DetachFromStatics(); window.Close(); ThemeService.Apply(previousTheme); }
    });
}
