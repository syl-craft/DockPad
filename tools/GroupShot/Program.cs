using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockPad;
using DockPad.Models;
using DockPad.Services;
using DockPad.Services.Localization;
using DockPad.Views;

// Fixtures uniquement, fenêtre non affichée : aucun raccourci réel n'est lu ni lancé.
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException("GroupShot <output.png> [dark]");
        var fixture = Path.Combine(Path.GetTempPath(), "dockpad-groupshot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        Environment.SetEnvironmentVariable(AppPaths.OverrideVariable, fixture);
        var app = new App(); app.InitializeComponent();
        ThemeService.Apply(args.ElementAtOrDefault(1) == "dark");

        var colors = new[] { Colors.CornflowerBlue, Colors.MediumSeaGreen, Colors.DarkOrange,
            Colors.MediumPurple, Colors.IndianRed, Colors.SteelBlue };
        var names = new[] { "Dossiers", "Terminal", "Navigateur", "Code", "Données", "Notes" };
        var icons = colors.Select((color, i) => WriteIcon(fixture, i, color)).ToArray();
        ShortcutEntry Item(int index) => new()
        {
            Name = names[index], Command = "fixture-no-launch", IconPath = icons[index],
            Type = (ShortcutType)(index % 5),
        };
        ShortcutEntry Group(TileLayout layout, int col, params int?[] indices) => new()
        {
            Layout = layout, Col = col, Name = col == 2 ? "Développement" : "Outils",
            GroupColor = col == 2 ? "#14B8A6" : null,
            Children = indices.Select(i => i is { } n ? Item(n) : null).ToList(),
        };
        var single = Item(0);
        ShortcutService.Save([
            single, Group(TileLayout.Quad, 1, 0, 1, 2, 3),
            Group(TileLayout.TwoPlusFour, 2, 0, 1, 2, 3, 4, 5),
            Group(TileLayout.Quad, 3, 0, null, 2, null),
            Group(TileLayout.TwoPlusFour, 4, 0, null, null, null, 4, null),
        ]);
        var window = new QuickAccessWindow();
        var grid = (ItemsControl)window.FindName("ShortcutsGrid");
        var preview = Preview(window, grid.ItemsSource.Cast<TileCell>().Take(6).ToArray());
        Save(preview, args[0]);

        // Vérifier les vrais gabarits, leurs liaisons et les proportions à la taille de production.
        var buttons = Descendants<Button>(preview).ToList();
        var small = buttons.Where(b => b.DataContext is TileCell { Col: 2, Slot: >= 2 }).ToList();
        Require(small.Count == 4, "Four small buttons must be rendered");
        Require(small.All(b => b.ActualHeight is > 22 and < 25 && b.ActualWidth is > 24 and < 27), "Small slot dimensions");
        var bands = Descendants<Border>(preview).Where(b => b.Name == "GroupBand").ToArray();
        Require(bands.Length == 4, "Each group has one color band");
        Require(bands.All(b => b.ActualHeight > 85), "Group bands span icons and title");
        Require(((SolidColorBrush)bands.Single(b => b.DataContext is TileCell { Col: 2 }).Background).Color
            == (Color)ColorConverter.ConvertFromString("#14B8A6"), "Custom group color is rendered");
        Require(buttons.Where(b => b.DataContext is TileCell { Slot: not null }).All(b =>
            !Descendants<Border>(b).Any(border => border.HorizontalAlignment == HorizontalAlignment.Right)),
            "Children have no individual color band");
        var editDialog = new GroupNameDialog("Développement", "#14B8A6");
        Require(editDialog.GroupColor == "#14B8A6", "Editing starts with the saved color");
        editDialog.Close();
        Require(buttons.Count(b => b.DataContext is TileCell { Col: 1, Slot: not null }) == 4, "Quad button count");

        var fullGroupChild = buttons.First(b => b.DataContext is TileCell { Col: 2, Slot: 0 });
        var opening = (ContextMenuEventArgs)Activator.CreateInstance(typeof(ContextMenuEventArgs),
            BindingFlags.NonPublic | BindingFlags.Instance, null, [fullGroupChild, true], null)!;
        fullGroupChild.RaiseEvent(opening);
        Require(((MenuItem)fullGroupChild.ContextMenu.Items[0]).Header is TextBlock { Text: "Dossiers" },
            "Child menu starts with the shortcut name");
        Require(!opening.Handled, "Opening the child menu must not cancel the context menu event");
        var layoutMenu = fullGroupChild.ContextMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Header, Loc.T("Group_Layout")));
        Require(!((MenuItem)layoutMenu.Items[0]).IsEnabled, "Full group cannot become single");
        Require(!((MenuItem)layoutMenu.Items[1]).IsEnabled, "Six children cannot fit into quad");
        Require(((MenuItem)layoutMenu.Items[2]).IsChecked, "Current layout is checked");

        // Le choix de destination passe par les événements Click réels du bouton et du menu.
        var moveMenu = fullGroupChild.ContextMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Header, Loc.T("Group_Move")));
        ((MenuItem)moveMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var cells = grid.ItemsSource.Cast<TileCell>().ToArray();
        Require(cells[3].Children[1].IsMoveTarget, "Empty child is highlighted");
        Require(!cells[2].IsMoveTarget, "Parent cannot be replaced by its own child");
        var destinations = Preview(window, cells.Take(6).ToArray());
        var target = Descendants<Button>(destinations).First(b => b.DataContext is TileCell { Col: 3, Slot: 1 });
        target.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var saved = ShortcutService.Load();
        Require(TileGroupService.Get(saved, new(0, 0, 2, 0)) is null, "Source child is removed");
        Require(TileGroupService.Get(saved, new(0, 0, 3, 1))?.Name == "Dossiers", "Destination receives child");
        Require(((FrameworkElement)window.FindName("MoveBanner")).Visibility == Visibility.Collapsed, "Move mode ends");

        // Le pied de carte porte le groupe entier, pas un de ses enfants.
        var groupPreview = Preview(window, grid.ItemsSource.Cast<TileCell>().Take(6).ToArray());
        var title = Descendants<Button>(groupPreview).Single(b => b.Name == "GroupTitle" && b.DataContext is TileCell { Col: 2 });
        Require(((TileCell)title.DataContext).Entry!.IsGroup, "Title carries the whole group");
        Require(title.Cursor == System.Windows.Input.Cursors.Arrow, "Locked group uses the normal cursor");
        window.ToggleTileLock();
        Require(title.Cursor == System.Windows.Input.Cursors.SizeAll, "Unlocking enables the move cursor immediately");
        window.ToggleTileLock();
        Require(title.Cursor == System.Windows.Input.Cursors.Arrow, "Locking restores the normal cursor immediately");
        var titleOpening = (ContextMenuEventArgs)Activator.CreateInstance(typeof(ContextMenuEventArgs),
            BindingFlags.NonPublic | BindingFlags.Instance, null, [title, true], null)!;
        title.RaiseEvent(titleOpening);
        Require(((MenuItem)title.ContextMenu.Items[0]).Header is TextBlock { Text: "Développement" },
            "Group menu starts with the group name");
        var groupMenu = title.ContextMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Header, Loc.T("Group_Actions")));
        var wholeMove = groupMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Header, Loc.T("Group_MoveWhole")));
        wholeMove.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var wholeDestinations = Preview(window, grid.ItemsSource.Cast<TileCell>().Take(6).ToArray());
        var emptyRoot = Descendants<Button>(wholeDestinations).Single(b => b.DataContext is TileCell { Col: 5 });
        emptyRoot.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        saved = ShortcutService.Load();
        Require(TileGroupService.Get(saved, new(0, 0, 2)) is null, "Whole move frees the original card");
        Require(TileGroupService.Get(saved, new(0, 0, 5)) is { Name: "Développement", IsGroup: true }, "Whole move keeps name and container");

        // Dépôt d'un groupe sur l'icône d'un autre : échange des deux cartes, sans imbrication.
        var sourceGroup = TileGroupService.Get(saved, new(0, 0, 5))!;
        typeof(QuickAccessWindow).GetField("_dragSource", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, sourceGroup);
        typeof(QuickAccessWindow).GetField("_dragEntries", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, saved);
        var dragPreview = Preview(window, grid.ItemsSource.Cast<TileCell>().Take(6).ToArray());
        var childTarget = Descendants<Button>(dragPreview).Single(b => b.DataContext is TileCell { Col: 1, Slot: 0 });
        DragEventArgs DragArgs(RoutedEvent routedEvent)
        {
            var drag = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                [new DataObject(sourceGroup), DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, childTarget, new Point(2, 2)], null)!;
            drag.RoutedEvent = routedEvent;
            return drag;
        }
        var over = DragArgs(DragDrop.DragOverEvent);
        childTarget.RaiseEvent(over);
        Require(over.Effects == DragDropEffects.Move, "Whole-group drop onto a child is allowed");
        childTarget.RaiseEvent(DragArgs(DragDrop.DropEvent));
        saved = ShortcutService.Load();
        Require(TileGroupService.Get(saved, new(0, 0, 1)) is { Name: "Développement", IsGroup: true }, "Dragged group reaches target card");
        Require(TileGroupService.Get(saved, new(0, 0, 1))!.GroupColor == "#14B8A6", "Color survives group moves and persistence");
        Require(TileGroupService.Get(saved, new(0, 0, 5)) is { Name: "Outils", IsGroup: true }, "Target group is preserved at source");
        Require(TileGroupService.Leaves(saved).Count() == 15, "No shortcut is lost during either move");
        Console.WriteLine("Group names, layout, menu moves and whole-group drop checks passed.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static Border Preview(QuickAccessWindow window, TileCell[] cells)
    {
        var panel = new FrameworkElementFactory(typeof(System.Windows.Controls.Primitives.UniformGrid));
        panel.SetValue(System.Windows.Controls.Primitives.UniformGrid.ColumnsProperty, 3);
        var items = new ItemsControl
        {
            ItemsSource = cells, ItemTemplateSelector = (DataTemplateSelector)window.FindResource("TileSelector"),
            ItemsPanel = new ItemsPanelTemplate(panel), Resources = window.Resources,
        };
        var border = new Border { Padding = new Thickness(18), Child = items,
            Background = (Brush)Application.Current.FindResource("Brush.Surface") };
        border.Measure(new Size(390, double.PositiveInfinity));
        border.Arrange(new Rect(0, 0, 390, border.DesiredSize.Height));
        border.UpdateLayout();
        return border;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var next in Descendants<T>(child)) yield return next;
        }
    }

    private static string WriteIcon(string directory, int index, Color color)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRoundedRectangle(new SolidColorBrush(color), null, new Rect(0, 0, 36, 36), 7, 7);
            var label = new FormattedText((index + 1).ToString(), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 24, Brushes.White, 1);
            drawing.DrawText(label, new Point((36 - label.Width) / 2, (36 - label.Height) / 2));
        }
        var path = Path.Combine(directory, $"icon-{index}.png");
        var bitmap = new RenderTargetBitmap(36, 36, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        Write(bitmap, path);
        return path;
    }

    private static void Save(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * 2),
            (int)Math.Ceiling(element.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(element); Write(bitmap, path);
    }

    private static void Write(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
