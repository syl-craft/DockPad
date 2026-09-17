using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DockPad.Models;
using DockPad.Services;
using DockPad.Views;

namespace DockPad;

public partial class QuickAccessWindow
{
    private TileAddress? _moveSource;
    private bool _moveWholeGroup;

    private static TileCell GroupCell(ShortcutEntry group)
    {
        var children = new List<TileCell>();
        for (int slot = 0; slot < TileGroupService.Capacity(group.Layout); slot++)
        {
            var entry = group.Children?.ElementAtOrDefault(slot);
            children.Add(new TileCell
            {
                Page = group.Page, Row = group.Row, Col = group.Col, Slot = slot, Entry = entry,
                Icon = entry is null ? null : IconStoreService.LoadImage(
                    IconStoreService.ResolveProfilePath(entry.IconProfilePath) ?? entry.IconPath),
                Tooltip = entry is null ? Loc.T("Quick_Tile_Add") : $"{entry.Name}\n[{TypeLabel(entry.Type)}] {entry.Command}",
                Band = entry is null ? null : TypeBandBrush(entry.Type),
                ChildIconSize = group.Layout == TileLayout.TwoPlusFour && slot >= 2 ? 18 : 24,
            });
        }
        return new TileCell { Page = group.Page, Row = group.Row, Col = group.Col, Entry = group, Children = children };
    }

    private void MarkMoveTargets(List<TileCell> cells, List<ShortcutEntry> entries)
    {
        MoveBanner.Visibility = _moveSource is null ? Visibility.Collapsed : Visibility.Visible;
        if (_moveSource is not { } from) return;
        foreach (var cell in cells)
        {
            cell.IsMoveTarget = TileGroupService.CanMove(entries, from, cell.Address);
            foreach (var child in cell.Children)
                child.IsMoveTarget = !_moveWholeGroup && TileGroupService.CanMove(entries, from, child.Address);
        }
    }

    private void BeginMove(TileAddress address, bool wholeGroup = false)
    {
        ClearSearch();
        _moveSource = address;
        _moveWholeGroup = wholeGroup;
        PopulateGrid();
    }

    private void CancelMove()
    {
        _moveSource = null;
        _moveWholeGroup = false;
        MoveBanner.Visibility = Visibility.Collapsed;
    }

    private void CancelMove_Click(object sender, RoutedEventArgs e)
    {
        CancelMove();
        PopulateGrid();
    }

    private bool CompleteMove(TileCell cell)
    {
        if (_moveSource is not { } from) return false;
        var to = _moveWholeGroup ? cell.Address.Root : cell.Address;
        var result = TileGroupService.Mutate(Files, all => TileGroupService.MoveCore(all, from, to));
        if (!result.Ok) { AppDialog.Info(result.Error!, owner: this); return true; }
        CancelMove();
        PopulateGrid();
        return true;
    }

    private async void GroupChild_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (CellOf(sender) is not { } cell || CompleteMove(cell)) return;
        if (cell.Entry is { } entry) ExecuteEntry(entry);
        else await AddTile(cell.Row, cell.Col, cell.Slot);
    }

    private void GroupTitle_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (CellOf(sender) is { } cell) CompleteMove(cell);
    }

    private void GroupChild_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (CellOf(sender)?.Entry is null) EmptyTile_ContextMenuOpening(sender, e);
        else Tile_ContextMenuOpening(sender, e);
    }

    private void Group_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is TileCell { Slot: not null }) return;
        if (sender is not FrameworkElement { ContextMenu: { } menu } || CellOf(sender) is not { } cell) return;
        menu.Items.Clear();
        AppendTileMenuTitle(menu, cell.GroupName);
        AppendGroupMenu(menu, cell);
    }

    private static void AppendTileMenuTitle(ContextMenu menu, string name)
    {
        var title = new TextBlock
        {
            Text = name, FontWeight = FontWeights.SemiBold,
            MaxWidth = 280, TextWrapping = TextWrapping.Wrap,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        menu.Items.Add(new MenuItem { Header = title, IsEnabled = false });
        menu.Items.Add(new Separator());
    }

    private void AppendGroupMenu(ContextMenu menu, TileCell cell)
    {
        var rootAddress = cell.Address.Root;
        var root = TileGroupService.Get(ShortcutService.Load(Files.EntriesPath), rootAddress);
        var layoutMenu = new MenuItem { Header = Loc.T("Group_Layout") };
        int count = root is null ? 0 : root.IsGroup ? root.Children!.Count(c => c is not null) : 1;
        foreach (var layout in Enum.GetValues<TileLayout>())
        {
            int excess = count - TileGroupService.Capacity(layout);
            var option = new MenuItem
            {
                Header = layout switch
                {
                    TileLayout.Simple => Loc.T("Group_Layout_Simple"),
                    TileLayout.Quad => Loc.T("Group_Layout_Quad"),
                    _ => Loc.T("Group_Layout_TwoPlusFour"),
                },
                IsCheckable = true,
                IsChecked = (root?.Layout ?? TileLayout.Simple) == layout,
                IsEnabled = excess <= 0,
                ToolTip = excess > 0 ? Loc.F("Group_TooMany", excess) : null,
            };
            ToolTipService.SetShowOnDisabled(option, true);
            option.Click += (_, _) =>
            {
                var result = TileGroupService.Mutate(Files, all => TileGroupService.ChangeLayoutCore(all, rootAddress, layout));
                if (!result.Ok) AppDialog.Info(result.Error!, owner: this);
                CancelMove();
                PopulateGrid();
            };
            layoutMenu.Items.Add(option);
        }
        if (menu.Items.Count > 0 && menu.Items[menu.Items.Count - 1] is not Separator) menu.Items.Add(new Separator());
        menu.Items.Add(layoutMenu);

        if (cell.Entry is { } selected && !selected.IsGroup)
        {
            var move = new MenuItem { Header = Loc.T("Group_Move") };
            var choose = new MenuItem { Header = Loc.T("Group_ChooseDestination") };
            choose.Click += (_, _) => BeginMove(cell.Address);
            move.Items.Add(choose);
            menu.Items.Add(move);
        }
        if (root?.IsGroup != true) return;

        var groupMenu = new MenuItem { Header = Loc.T("Group_Actions") };
        var rename = new MenuItem { Header = Loc.T("Group_Rename") };
        rename.Click += (_, _) =>
        {
            var dialog = new GroupNameDialog(string.IsNullOrWhiteSpace(root.Name) ? Loc.T("Group_Actions") : root.Name,
                root.GroupColor) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            var result = TileGroupService.Mutate(Files, all => TileGroupService.UpdateCore(all, rootAddress, dialog.GroupName, dialog.GroupColor));
            if (!result.Ok) AppDialog.Error(result.Error!, owner: this);
            PopulateGrid();
        };
        groupMenu.Items.Add(rename);
        var moveGroup = new MenuItem { Header = Loc.T("Group_MoveWhole") };
        moveGroup.Click += (_, _) => BeginMove(rootAddress, wholeGroup: true);
        groupMenu.Items.Add(moveGroup);
        groupMenu.Items.Add(BuildMoveToPageMenu(root));
        var transfer = new MenuItem { Header = Loc.T(_mode.IsFavorites ? "Quick_Tile_MoveToShortcuts" : "Quick_Tile_MoveToFavorites") };
        transfer.Click += (_, _) => TransferTile(root);
        groupMenu.Items.Add(transfer);
        var duplicate = new MenuItem { Header = Loc.T("Quick_Tile_Duplicate") };
        duplicate.Click += (_, _) => DuplicateTile(root);
        groupMenu.Items.Add(duplicate);
        var delete = new MenuItem { Header = Loc.T("Group_Delete") };
        delete.Click += (_, _) =>
        {
            if (!AppDialog.Confirm(Loc.F("Group_ConfirmDelete", count), owner: this)) return;
            var result = ShortcutActionService.Delete(root.Page, root.Row, root.Col, Files);
            if (!result.Ok) AppDialog.Error(result.Error!, owner: this);
            PopulateGrid();
        };
        groupMenu.Items.Add(delete);
        menu.Items.Add(groupMenu);
    }

    // Le raccourci clavier d'une case composée propose ses enfants, sans lancer le groupe.
    private void OpenGroupLauncher(ShortcutEntry group)
    {
        var menu = new ContextMenu { PlacementTarget = ShortcutsGrid, Placement = PlacementMode.Center };
        foreach (var child in group.Children?.OfType<ShortcutEntry>() ?? [])
        {
            var item = new MenuItem { Header = child.Name };
            var icon = IconStoreService.LoadImage(IconStoreService.ResolveProfilePath(child.IconProfilePath) ?? child.IconPath);
            if (icon is not null) item.Icon = new Image { Source = icon, Width = 16, Height = 16, Stretch = Stretch.Uniform };
            item.Click += (_, _) => ExecuteEntry(child);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = Loc.T("Group_Empty"), IsEnabled = false });
        menu.IsOpen = true;
        if (menu.Items[0] is MenuItem first) first.Focus();
    }
}
