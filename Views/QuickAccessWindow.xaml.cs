using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockPad.Models;
using DockPad.Services;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace DockPad;

public partial class QuickAccessWindow : Window
{
    private const int GridRows = 4;
    private const int GridCols = 6;

    private int _currentPage = 0;

    private IntPtr _hwnd;
    private Point _dragStartPoint;
    private ShortcutEntry? _dragSource;

    private readonly List<UIElement> _hintElements = [];
    private bool? _hintIsCtrl; // null = caché, true = premier trigger, false = second trigger
    private ModifierKeys _triggerFirst  = ModifierKeys.Control;
    private ModifierKeys _triggerSecond = ModifierKeys.Shift;

    public QuickAccessWindow()
    {
        InitializeComponent();
        PopulateGrid();
        UpdateHotkeyDisplay();
        UpdateTriggerMods();

        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        TxtVersion.Text = v != null ? $"v{v.Major}.{v.Minor}.{v.Build}" : "";
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        RegisterHotkey();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.IsExiting)
        {
            e.Cancel = true;
            ClearSearch();
            Hide();
            return;
        }
        UnregisterHotkey();
        base.OnClosing(e);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        Dispatcher.BeginInvoke(() => SearchBox.Focus());
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        if (SearchPopup.IsOpen && !IsDescendant(e.OriginalSource as DependencyObject, SearchBox))
            SearchPopup.IsOpen = false;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (_hintIsCtrl != null) { _hintIsCtrl = null; HideHintOverlay(); }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (SearchBox.Text.Length > 0) return;

        // Premier trigger seul → overlay gauche
        if (IsAloneModifier(e, _triggerFirst) && _hintIsCtrl != true)
        {
            _hintIsCtrl = true;
            ShowHintOverlay(isCtrl: true);
            return;
        }

        // Second trigger seul → overlay droite
        if (IsAloneModifier(e, _triggerSecond) && _hintIsCtrl != false)
        {
            _hintIsCtrl = false;
            ShowHintOverlay(isCtrl: false);
            return;
        }

        // Avec le trigger Alt, les touches arrivent en Key.System (SystemKey = vraie touche)
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Premier trigger + touche (0-9, ↑, ↓) → exécuter gauche
        if (IsModHeld(_triggerFirst) && !IsModHeld(_triggerSecond) &&
            GetHintKey(key) is int n1)
        {
            e.Handled = true;
            _hintIsCtrl = null;
            HideHintOverlay();
            ExecuteByHintKey(n1, isCtrl: true);
            return;
        }

        // Second trigger + touche (0-9, ↑, ↓) → exécuter droite
        if (IsModHeld(_triggerSecond) && !IsModHeld(_triggerFirst) &&
            GetHintKey(key) is int n2)
        {
            e.Handled = true;
            _hintIsCtrl = null;
            HideHintOverlay();
            ExecuteByHintKey(n2, isCtrl: false);
            return;
        }

        // Flèches ← / → seules → page précédente / suivante
        if (Keyboard.Modifiers == ModifierKeys.None && key is Key.Left or Key.Right)
        {
            e.Handled = true;
            GoToAdjacentPage(key == Key.Right ? 1 : -1);
        }
    }

    private void GoToAdjacentPage(int delta)
    {
        // Même règle d'affichage que UpdatePagination : pages avec contenu ou config
        var all       = ShortcutService.Load();
        var configs   = PageConfigService.Load();
        int maxUsed   = all.Count     > 0 ? all.Max(s => s.Page)      : -1;
        int maxConfig = configs.Count > 0 ? configs.Max(p => p.Index) : -1;
        int lastShown = Math.Max(Math.Max(maxUsed, maxConfig), _currentPage);

        int target = _currentPage + delta;
        if (target < 0 || target > lastShown) return; // pas de bouclage aux extrémités
        GoToPage(target);
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (_hintIsCtrl == null) return;

        if (IsModifierReleased(e, _triggerFirst) || IsModifierReleased(e, _triggerSecond))
        {
            _hintIsCtrl = null;
            HideHintOverlay();
        }
    }

    private static string? GetIconInitialDir(string iconPath, string? iconProfilePath)
    {
        if (!string.IsNullOrEmpty(iconPath))
        {
            var dir = Path.GetDirectoryName(iconPath);
            if (Directory.Exists(dir)) return dir;
        }
        var profileAbs = IconCacheService.ResolveProfilePath(iconProfilePath);
        if (!string.IsNullOrEmpty(profileAbs))
        {
            var dir = Path.GetDirectoryName(profileAbs);
            if (Directory.Exists(dir)) return dir;
        }
        return null;
    }

    private static bool IsDescendant(DependencyObject? child, DependencyObject parent)
    {
        while (child != null)
        {
            if (child == parent) return true;
            child = VisualTreeHelper.GetParent(child);
        }
        return false;
    }

    private void RegisterHotkey()
    {
        var (mods, key) = SettingsService.LoadHotkey();
        HotkeyService.RegisterHotKey(_hwnd, HotkeyService.HotkeyId, mods | HotkeyService.MOD_NOREPEAT, key);
    }

    private void UnregisterHotkey()
    {
        HotkeyService.UnregisterHotKey(_hwnd, HotkeyService.HotkeyId);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // WM_KEYDOWN / WM_SYSKEYDOWN : mémorise le flag "touche étendue" (bit 24 du lParam).
        // Les vraies flèches sont étendues ; les mêmes VK émis par le pavé numérique
        // (Shift+chiffre ou NumLock off) ne le sont pas — WPF ne l'expose pas dans KeyEventArgs.
        if (msg == HotkeyService.WM_HOTKEY && wParam.ToInt32() == HotkeyService.HotkeyId)
        {
            WindowState = WindowState.Normal;
            Show();
            Activate();
            Dispatcher.BeginInvoke(() => SearchBox.Focus());
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu is ContextMenu menu)
        {
            menu.PlacementTarget = btn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OpenContextMenuManager_Click(object sender, RoutedEventArgs e)
    {
        var win = new ContextMenuManagerWindow();
        win.Show();
    }

    private void Presets_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PresetsDialog { Owner = this };
        dialog.ShowDialog();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;

        UnregisterHotkey();
        RegisterHotkey();
        UpdateHotkeyDisplay();
        UpdateTriggerMods();
    }

    private void Browsers_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new BrowserConfigDialog { Owner = this };
        dialog.ShowDialog();
    }

    private void UpdateHotkeyDisplay()
    {
        var (mods, vk) = SettingsService.LoadHotkey();
        var parts = new List<string>();
        if ((mods & HotkeyService.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mods & HotkeyService.MOD_ALT)     != 0) parts.Add("Alt");
        if ((mods & HotkeyService.MOD_SHIFT)   != 0) parts.Add("Shift");
        if ((mods & HotkeyService.MOD_WIN)     != 0) parts.Add("Win");

        parts.Add(HotkeyService.KeyName(vk));

        TxtHotkey.Text = string.Join(" + ", parts);
    }

    private void OpenConfigFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.GetDirectoryName(ShortcutService.FilePath)!;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
    }

    private void BackupConfig_Click(object sender, RoutedEventArgs e)
    {
        var backupDir = Path.Combine(
            Path.GetDirectoryName(ShortcutService.FilePath)!, ".backup");
        Directory.CreateDirectory(backupDir);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        foreach (var src in new[] { ShortcutService.FilePath, PageConfigService.FilePath,
                                    BrowserConfigService.FilePath })
        {
            if (!File.Exists(src)) continue;
            var dest = Path.Combine(backupDir,
                $"{Path.GetFileNameWithoutExtension(src)}_{timestamp}{Path.GetExtension(src)}");
            File.Copy(src, dest);
        }

        AppDialog.Info($"Sauvegarde créée dans :\n{backupDir}", owner: this);
    }

    private void PopulateGrid()
    {
        ShortcutsGrid.Children.Clear();
        var all = ShortcutService.Load();

        UpdatePagination(all);

        var pageEntries = all.Where(s => s.Page == _currentPage).ToList();

        for (int row = 0; row < GridRows; row++)
        {
            for (int col = 0; col < GridCols; col++)
            {
                var entry = pageEntries.FirstOrDefault(s => s.Row == row && s.Col == col);
                var btn = entry is { Name.Length: > 0 }
                    ? CreateTile(entry)
                    : CreateEmptyTile(row, col);

                Grid.SetRow(btn, row);
                Grid.SetColumn(btn, col);
                ShortcutsGrid.Children.Add(btn);
            }
        }
    }

    private void UpdatePagination(List<ShortcutEntry> all)
    {
        PaginationBar.Children.Clear();

        var configs     = PageConfigService.Load();
        int maxUsed     = all.Count     > 0 ? all.Max(s => s.Page)      : -1;
        int maxConfig   = configs.Count > 0 ? configs.Max(p => p.Index) : -1;
        // Uniquement les pages avec contenu + la page courante si elle est plus loin
        int lastShown   = Math.Max(Math.Max(maxUsed, maxConfig), _currentPage);

        for (int p = 0; p <= lastShown; p++)
        {
            int page   = p;
            var config = configs.FirstOrDefault(c => c.Index == page);
            bool active = page == _currentPage;

            var btn = BuildPageButton(page, config, active, lastShown);
            btn.Click += (_, _) => GoToPage(page);
            PaginationBar.Children.Add(btn);
        }

        // Bouton "+" pour ajouter une page au-delà des existantes
        var addBtn = new Button
        {
            Content  = "+",
            Style    = (Style)FindResource("PageButton"),
            ToolTip  = "Ajouter une page",
            FontSize = 15,
            FontWeight = FontWeights.Light,
        };
        addBtn.Click += (_, _) => GoToPage(lastShown + 1);
        PaginationBar.Children.Add(addBtn);
    }

    private Button BuildPageButton(int page, PageConfig? config, bool active, int lastShown)
    {
        object content;
        if (config != null && (!string.IsNullOrEmpty(config.IconPath) || !string.IsNullOrEmpty(config.IconProfilePath)))
        {
            string iconDisp = !string.IsNullOrEmpty(config.IconPath) && File.Exists(config.IconPath)
                ? config.IconPath
                : IconCacheService.ResolveProfilePath(config.IconProfilePath) ?? "";
            var src = LoadIcon(iconDisp);
            content = src != null
                ? (object)new Image { Source = src, Width = 18, Height = 18, Stretch = Stretch.Uniform }
                : (page + 1).ToString();
        }
        else
        {
            content = (page + 1).ToString();
        }

        var btn = new Button
        {
            Content = content,
            Style   = (Style)FindResource(active ? "PageButtonActive" : "PageButton"),
            ToolTip = $"Page {page + 1}",
        };

        var menu = new ContextMenu();

        if (config != null && (!string.IsNullOrEmpty(config.IconPath) || !string.IsNullOrEmpty(config.IconProfilePath)))
        {
            var removeIcon = new MenuItem { Header = "🗑 Supprimer l'icône" };
            removeIcon.Click += (_, _) => ClearPageIcon(page);
            menu.Items.Add(removeIcon);
        }
        var changeIcon = new MenuItem { Header = "🖼 Changer l'icône" };
        changeIcon.Click += (_, _) => ChangePageIcon(page);
        menu.Items.Add(changeIcon);

        menu.Items.Add(new Separator());

        var moveLeft = new MenuItem { Header = "← Déplacer à gauche", IsEnabled = page > 0 };
        moveLeft.Click += (_, _) => MovePage(page, page - 1);
        menu.Items.Add(moveLeft);

        var moveRight = new MenuItem { Header = "→ Déplacer à droite", IsEnabled = page < lastShown };
        moveRight.Click += (_, _) => MovePage(page, page + 1);
        menu.Items.Add(moveRight);

        menu.Items.Add(new Separator());

        var deletePage = new MenuItem { Header = "🗑 Supprimer la page" };
        deletePage.Click += (_, _) => DeletePage(page);
        menu.Items.Add(deletePage);

        btn.ContextMenu = menu;
        return btn;
    }

    private void GoToPage(int page)
    {
        _currentPage = page;
        PopulateGrid();
    }

    private void ChangePageIcon(int pageIndex)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Choisir une icône pour la page",
            Filter = "Images et exécutables|*.png;*.ico;*.bmp;*.jpg;*.jpeg;*.exe;*.dll|Tous les fichiers|*.*",
        };
        var configs = PageConfigService.Load();
        var current = configs.FirstOrDefault(p => p.Index == pageIndex);
        var initDir = current != null ? GetIconInitialDir(current.IconPath, current.IconProfilePath) : null;
        if (initDir != null) dlg.InitialDirectory = initDir;

        if (dlg.ShowDialog() != true) return;

        var configs2 = PageConfigService.Load();
        var config   = configs2.FirstOrDefault(p => p.Index == pageIndex);
        if (config == null) { config = new PageConfig { Index = pageIndex }; configs2.Add(config); }
        config.IconPath        = dlg.FileName;
        config.IconProfilePath = IconCacheService.CopyToProfile(dlg.FileName);
        PageConfigService.Save(configs2);
        PopulateGrid();
    }

    private void ClearPageIcon(int pageIndex)
    {
        var configs = PageConfigService.Load();
        var config  = configs.FirstOrDefault(p => p.Index == pageIndex);
        if (config == null) return;
        config.IconPath        = "";
        config.IconProfilePath = null;
        PageConfigService.Save(configs);
        PopulateGrid();
    }

    private void MovePage(int fromIndex, int toIndex)
    {
        var shortcuts = ShortcutService.Load();
        var configs   = PageConfigService.Load();

        // Swap les entrées
        foreach (var s in shortcuts)
        {
            if      (s.Page == fromIndex) s.Page = toIndex;
            else if (s.Page == toIndex)   s.Page = fromIndex;
        }

        // Swap les configs
        var fromCfg = configs.FirstOrDefault(p => p.Index == fromIndex);
        var toCfg   = configs.FirstOrDefault(p => p.Index == toIndex);
        if (fromCfg != null) fromCfg.Index = toIndex;
        if (toCfg   != null) toCfg.Index   = fromIndex;

        ShortcutService.Save(shortcuts);
        PageConfigService.Save(configs);

        if      (_currentPage == fromIndex) _currentPage = toIndex;
        else if (_currentPage == toIndex)   _currentPage = fromIndex;

        PopulateGrid();
    }

    private void DeletePage(int pageIndex)
    {
        var shortcuts    = ShortcutService.Load();
        int entryCount   = shortcuts.Count(s => s.Page == pageIndex);
        string msg = entryCount > 0
            ? $"Supprimer la page {pageIndex + 1} et ses {entryCount} raccourci(s) ?"
            : $"Supprimer la page {pageIndex + 1} ?";

        if (!AppDialog.Confirm(msg, "Supprimer la page", this))
            return;

        // Supprimer les entrées de cette page et décaler les suivantes
        shortcuts.RemoveAll(s => s.Page == pageIndex);
        foreach (var s in shortcuts.Where(s => s.Page > pageIndex))
            s.Page--;

        var configs = PageConfigService.Load();
        configs.RemoveAll(p => p.Index == pageIndex);
        foreach (var c in configs.Where(c => c.Index > pageIndex))
            c.Index--;

        ShortcutService.Save(shortcuts);
        PageConfigService.Save(configs);

        int newMax = shortcuts.Count > 0 ? shortcuts.Max(s => s.Page) : 0;
        _currentPage = Math.Min(_currentPage > pageIndex ? _currentPage - 1 : _currentPage, newMax);

        PopulateGrid();
    }

    private Button CreateTile(ShortcutEntry entry)
    {
        var icon = new Image
        {
            Width = 36,
            Height = 36,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 0, 6),
            Source = LoadIcon(IconCacheService.ResolveProfilePath(entry.IconProfilePath) ?? entry.IconPath)
        };

        var label = new TextBlock
        {
            Text = entry.Name,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 94,
            FontSize = 11,
        };

        var btn = new Button
        {
            Style = (Style)FindResource("TileButton"),
            ToolTip = $"[{TypeLabel(entry.Type)}] {entry.Command}",
            DataContext = entry,
            Tag = TypeBandBrush(entry.Type),
            Content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { icon, label }
            }
        };

        btn.Click += Tile_Click;
        btn.MouseEnter += TileHover_Enter;
        btn.MouseLeave += TileHover_Leave;
        btn.PreviewMouseLeftButtonDown += TileDrag_MouseDown;
        btn.PreviewMouseMove += TileDrag_MouseMove;
        btn.AllowDrop = true;
        btn.DragOver  += TileDrop_DragOver;
        btn.DragLeave += TileDrop_DragLeave;
        btn.Drop      += TileDrop_Drop;

        var menu = new ContextMenu();
        var changeIcon = new MenuItem { Header = "🖼 Changer l'icône" };
        changeIcon.Click += (_, _) => ChangeIcon(btn, entry);
        var edit = new MenuItem { Header = "✏ Modifier" };
        edit.Click += (_, _) => EditTile(entry);
        var duplicate = new MenuItem { Header = "⧉ Dupliquer" };
        duplicate.Click += (_, _) => DuplicateTile(entry);
        var delete = new MenuItem { Header = "🗑 Supprimer" };
        delete.Click += (_, _) => DeleteTile(entry);
        var moveToPage = BuildMoveToPageMenu(entry);
        menu.Items.Add(changeIcon);
        menu.Items.Add(new Separator());
        menu.Items.Add(edit);
        menu.Items.Add(duplicate);
        menu.Items.Add(moveToPage);

        if (entry.Type == ShortcutType.OpenFolder)
            BuildFolderContextMenuSection(menu, entry.Command);

        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        btn.ContextMenu = menu;

        return btn;
    }

    private Button CreateEmptyTile(int row, int col)
    {
        var btn = new Button
        {
            Style = (Style)FindResource("EmptyTileButton"),
            Content = new TextBlock
            {
                Text = "+",
                FontSize = 22,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            }
        };
        btn.AllowDrop = true;
        btn.DragOver  += TileDrop_DragOver;
        btn.DragLeave += TileDrop_DragLeave;
        btn.Drop      += TileDrop_Drop;

        var menu = new ContextMenu();
        var add = new MenuItem { Header = "➕ Ajouter" };
        add.Click += (_, _) => AddTile(row, col);
        menu.Items.Add(add);
        btn.ContextMenu = menu;

        return btn;
    }

    private void AddTile(int row, int col)
    {
        var dlg = new ShortcutDialog(row: row, col: col) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        dlg.Entry.Page = _currentPage;
        var all = ShortcutService.Load();
        all.Add(dlg.Entry);
        ShortcutService.Save(all);
        PopulateGrid();
    }

    private void EditTile(ShortcutEntry entry)
    {
        var dlg = new ShortcutDialog(entry) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var all = ShortcutService.Load();
        var existing = all.FirstOrDefault(s => s.Page == entry.Page && s.Row == entry.Row && s.Col == entry.Col);
        if (existing != null)
        {
            existing.Name            = dlg.Entry.Name;
            existing.Type            = dlg.Entry.Type;
            existing.Command         = dlg.Entry.Command;
            existing.IconPath        = dlg.Entry.IconPath;
            existing.IconProfilePath = dlg.Entry.IconProfilePath;
            existing.Terminal        = dlg.Entry.Terminal;
            existing.ProcessSwitch   = dlg.Entry.ProcessSwitch;
        }
        ShortcutService.Save(all);
        PopulateGrid();
    }

    private void DuplicateTile(ShortcutEntry entry)
    {
        var all = ShortcutService.Load();
        var occupied = all
            .Where(s => s.Page == _currentPage)
            .Select(s => (s.Row, s.Col))
            .ToHashSet();

        // Case vide la plus proche sur la page courante (distance de Chebyshev)
        (int row, int col)? nearest = null;
        int bestDist = int.MaxValue;
        for (int r = 0; r < GridRows; r++)
        {
            for (int c = 0; c < GridCols; c++)
            {
                if (occupied.Contains((r, c))) continue;
                int dist = Math.Max(Math.Abs(r - entry.Row), Math.Abs(c - entry.Col));
                if (dist < bestDist) { bestDist = dist; nearest = (r, c); }
            }
        }

        if (nearest is null)
        {
            AppDialog.Info("Page pleine. Naviguez vers une autre page pour dupliquer.", "Dupliquer", this);
            return;
        }

        all.Add(new ShortcutEntry
        {
            Page     = _currentPage,
            Row      = nearest.Value.row, Col = nearest.Value.col,
            Name     = entry.Name,    Type     = entry.Type,
            Command  = entry.Command, IconPath = entry.IconPath,
            Terminal = entry.Terminal == null ? null : new TerminalConfig
            {
                ExePath           = entry.Terminal.ExePath,
                StartingDirectory = entry.Terminal.StartingDirectory,
                RunCommand        = entry.Terminal.RunCommand,
                NewTab            = entry.Terminal.NewTab,
                ExtraArgs         = entry.Terminal.ExtraArgs,
            },
            ProcessSwitch = entry.ProcessSwitch == null ? null : new ProcessSwitchConfig
            {
                ProcessName = entry.ProcessSwitch.ProcessName,
                Executable  = entry.ProcessSwitch.Executable,
                Parameters  = entry.ProcessSwitch.Parameters,
            },
        });
        ShortcutService.Save(all);
        PopulateGrid();
    }

    private MenuItem BuildMoveToPageMenu(ShortcutEntry entry)
    {
        var moveMenu = new MenuItem { Header = "↗ Déplacer vers la page" };

        var all     = ShortcutService.Load();
        var configs = PageConfigService.Load();
        int maxUsed   = all.Count     > 0 ? all.Max(s => s.Page)      : -1;
        int maxConfig = configs.Count > 0 ? configs.Max(p => p.Index) : -1;
        int lastPage  = Math.Max(maxUsed, maxConfig) + 1; // inclut une page vide

        for (int p = 0; p <= lastPage; p++)
        {
            if (p == _currentPage) continue; // pas la page courante

            int targetPage = p;
            var config     = configs.FirstOrDefault(c => c.Index == p);
            bool hasFreeSlot = !all.Any(s => s.Page == p && s.Row == entry.Row && s.Col == entry.Col);

            // Construire le header avec icône si disponible
            object header;
            if (config != null && (!string.IsNullOrEmpty(config.IconPath) || !string.IsNullOrEmpty(config.IconProfilePath)))
            {
                string iconDisp2 = !string.IsNullOrEmpty(config.IconPath) && File.Exists(config.IconPath)
                    ? config.IconPath
                    : IconCacheService.ResolveProfilePath(config.IconProfilePath) ?? "";
                var src = LoadIcon(iconDisp2);
                if (src != null)
                {
                    var sp = new StackPanel { Orientation = Orientation.Horizontal };
                    sp.Children.Add(new Image { Source = src, Width = 14, Height = 14,
                        Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 6, 0) });
                    sp.Children.Add(new TextBlock { Text = $"Page {p + 1}" });
                    header = sp;
                }
                else header = $"Page {p + 1}";
            }
            else header = $"Page {p + 1}";

            var item = new MenuItem { Header = header };

            // Page pleine = toutes les cases occupées
            var occupiedOnPage = all.Where(s => s.Page == targetPage).Select(s => (s.Row, s.Col)).ToHashSet();
            bool pageFull = Enumerable.Range(0, GridRows).SelectMany(r => Enumerable.Range(0, GridCols).Select(c => (r, c)))
                                      .All(cell => occupiedOnPage.Contains(cell));
            if (pageFull)
            {
                item.IsEnabled = false;
                item.ToolTip   = "La page est pleine";
            }
            else
            {
                item.Click += (_, _) => MoveTileToPage(entry, targetPage);
            }

            moveMenu.Items.Add(item);
        }

        if (moveMenu.Items.Count == 0)
            moveMenu.IsEnabled = false;

        return moveMenu;
    }

    private void MoveTileToPage(ShortcutEntry entry, int targetPage)
    {
        var all      = ShortcutService.Load();
        var existing = all.FirstOrDefault(s => s.Page == entry.Page && s.Row == entry.Row && s.Col == entry.Col);
        if (existing == null) return;

        // Chercher la case cible : même position si libre, sinon première case disponible
        var occupied = all.Where(s => s.Page == targetPage).Select(s => (s.Row, s.Col)).ToHashSet();
        (int row, int col) dest = (entry.Row, entry.Col);
        if (occupied.Contains(dest))
        {
            bool found = false;
            for (int r = 0; r < GridRows && !found; r++)
                for (int c = 0; c < GridCols && !found; c++)
                    if (!occupied.Contains((r, c))) { dest = (r, c); found = true; }
        }

        existing.Page = targetPage;
        existing.Row  = dest.row;
        existing.Col  = dest.col;
        ShortcutService.Save(all);
        PopulateGrid();
    }

    private void BuildFolderContextMenuSection(ContextMenu menu, string folderPath)
    {
        var entries = RegistryService.LoadForTarget(ContextMenuTarget.FolderBackground)
            .Where(e => !string.IsNullOrEmpty(e.Command))
            .ToList();

        if (entries.Count == 0) return;

        menu.Items.Add(new Separator());

        foreach (var e in entries)
        {
            var item = new MenuItem { Header = e.DisplayName };

            var icon = LoadIcon(e.IconPath);
            if (icon != null)
                item.Icon = new Image { Source = icon, Width = 16, Height = 16, Stretch = Stretch.Uniform };

            // Substitue %V par le chemin du dossier (déjà entre guillemets dans la commande)
            string cmd = e.Command.Replace("%V", folderPath);
            item.Click += (_, _) =>
            {
                try
                {
                    var (exe, args) = ParseCommand(cmd);
                    Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Services.LogService.Error(ex, $"Exécution d'une entrée du menu contextuel dossier : {cmd}");
                    AppDialog.Error($"Impossible d'exécuter :\n{ex.Message}", owner: this);
                }
            };
            menu.Items.Add(item);
        }
    }

    private void DeleteTile(ShortcutEntry entry)
    {
        if (!AppDialog.Confirm($"Supprimer « {entry.Name} » ?", owner: this)) return;

        var all = ShortcutService.Load();
        all.RemoveAll(s => s.Page == entry.Page && s.Row == entry.Row && s.Col == entry.Col);
        ShortcutService.Save(all);
        PopulateGrid();
    }

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not ShortcutEntry entry) return;
        ExecuteEntry(entry);
    }

    private void ExecuteEntry(ShortcutEntry entry)
    {
        try
        {
            switch (entry.Type)
            {
                case ShortcutType.OpenFolder:
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{entry.Command}\"")
                        { UseShellExecute = true });
                    break;
                case ShortcutType.OpenUrl:
                    Process.Start(new ProcessStartInfo(entry.Command) { UseShellExecute = true });
                    break;
                case ShortcutType.OpenTerminal:
                    ExecuteTerminal(entry);
                    break;
                case ShortcutType.SwitchToProcess:
                    if (entry.ProcessSwitch != null)
                        ProcessSwitchService.SwitchOrLaunch(entry.ProcessSwitch);
                    break;
                case ShortcutType.RunCommand:
                default:
                    var (exe, args) = ParseCommand(entry.Command);
                    Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
                    break;
            }
        }
        catch (Exception ex)
        {
            Services.LogService.Error(ex, $"Exécution du raccourci « {entry.Name} » ({entry.Type})");
            AppDialog.Error($"Impossible d'exécuter :\n{ex.Message}", owner: this);
        }
    }

    private static void ExecuteTerminal(ShortcutEntry entry)
    {
        if (entry.Terminal is { ExePath.Length: > 0 } cfg)
        {
            var args = TerminalDetectionService.BuildArgs(cfg);
            Process.Start(new ProcessStartInfo(cfg.ExePath, args) { UseShellExecute = true });
            return;
        }

        // Fallback legacy : entry.Command = chemin du dossier, auto-détection du terminal
        string folder = entry.Command;
        foreach (var term in new[] { "wt.exe", "pwsh.exe", "powershell.exe", "cmd.exe" })
        {
            try
            {
                string args = term switch
                {
                    "wt.exe"         => $"-w 0 new-tab --startingDirectory \"{folder}\"",
                    "pwsh.exe"       => $"-NoExit -Command Set-Location \"{folder}\"",
                    "powershell.exe" => $"-NoExit -Command Set-Location \"{folder}\"",
                    _                => $"/k cd /d \"{folder}\"",
                };
                Process.Start(new ProcessStartInfo(term, args) { UseShellExecute = true });
                return;
            }
            catch { }
        }
        throw new InvalidOperationException("Aucun terminal trouvé (wt, pwsh, powershell, cmd).");
    }

    private static string TypeLabel(ShortcutType t) => t switch
    {
        ShortcutType.OpenFolder      => "Dossier",
        ShortcutType.OpenUrl         => "Navigateur",
        ShortcutType.OpenTerminal    => "Terminal",
        ShortcutType.SwitchToProcess => "Processus",
        _                            => "Commande",
    };

    private static readonly SolidColorBrush TileDefaultBackground = new(Colors.White);

    private void TileHover_Enter(object sender, MouseEventArgs e)
    {
        if (sender is not Button { Tag: SolidColorBrush band } btn) return;
        var c = band.Color;
        btn.Background   = new SolidColorBrush(Color.FromArgb(60, c.R, c.G, c.B));
        btn.BorderBrush  = band;
    }

    private void TileHover_Leave(object sender, MouseEventArgs e)
    {
        if (sender is not Button btn) return;
        btn.Background  = TileDefaultBackground;
        btn.BorderBrush = DefaultBorder;
    }

    private static readonly SolidColorBrush BandRunCommand      = new(Color.FromRgb(0xA8, 0xCC, 0xEA)); // bleu pastel
    private static readonly SolidColorBrush BandOpenFolder      = new(Color.FromRgb(0xF5, 0xCC, 0x80)); // ambre pastel
    private static readonly SolidColorBrush BandOpenUrl         = new(Color.FromRgb(0x92, 0xC6, 0x90)); // vert pastel
    private static readonly SolidColorBrush BandOpenTerminal    = new(Color.FromRgb(0xC4, 0xAD, 0xE0)); // violet pastel
    private static readonly SolidColorBrush BandSwitchToProcess = new(Color.FromRgb(0xF4, 0xA4, 0xA4)); // rouge pastel

    private static SolidColorBrush TypeBandBrush(ShortcutType t) => t switch
    {
        ShortcutType.OpenFolder      => BandOpenFolder,
        ShortcutType.OpenUrl         => BandOpenUrl,
        ShortcutType.OpenTerminal    => BandOpenTerminal,
        ShortcutType.SwitchToProcess => BandSwitchToProcess,
        _                            => BandRunCommand,
    };

    private void ChangeIcon(Button btn, ShortcutEntry entry)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choisir une icône",
            Filter = "Images et exécutables|*.png;*.ico;*.bmp;*.jpg;*.jpeg;*.exe;*.dll|Tous les fichiers|*.*"
        };

        var initDir = GetIconInitialDir(entry.IconPath, entry.IconProfilePath);
        if (initDir != null) dlg.InitialDirectory = initDir;

        if (dlg.ShowDialog() != true) return;

        string profilePath = IconCacheService.CopyToProfile(dlg.FileName) ?? "";

        entry.IconPath        = dlg.FileName;
        entry.IconProfilePath = profilePath;

        var all = ShortcutService.Load();
        var existing = all.FirstOrDefault(s => s.Page == entry.Page && s.Row == entry.Row && s.Col == entry.Col);
        if (existing != null)
        {
            existing.IconPath        = dlg.FileName;
            existing.IconProfilePath = profilePath;
        }
        ShortcutService.Save(all);

        if (btn.Content is StackPanel sp && sp.Children[0] is Image img)
            img.Source = LoadIcon(IconCacheService.ResolveProfilePath(profilePath) ?? dlg.FileName);
    }

    private void TileDrag_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void TileDrag_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not Button dragBtn || dragBtn.DataContext is not ShortcutEntry entry) return;

        var pos  = e.GetPosition(null);
        var diff = _dragStartPoint - pos;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragSource = entry;
        DragDrop.DoDragDrop((Button)sender, entry, DragDropEffects.Move);
        _dragSource = null;
    }

    private static readonly Brush DragOverBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
    private static readonly Brush DefaultBorder  = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));

    private void TileDrop_DragOver(object sender, DragEventArgs e)
    {
        if (_dragSource != null)
            e.Effects = DragDropEffects.Move;
        else if (IsExplorerDrop(e))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;

        if (sender is Button btn) btn.BorderBrush = e.Effects != DragDropEffects.None ? DragOverBrush : DefaultBorder;
        e.Handled = true;
    }

    private void TileDrop_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Button btn) btn.BorderBrush = DefaultBorder;
    }

    private void TileDrop_Drop(object sender, DragEventArgs e)
    {
        if (sender is Button b) b.BorderBrush = DefaultBorder;
        if (sender is not Button targetBtn) return;

        int targetRow = Grid.GetRow(targetBtn);
        int targetCol = Grid.GetColumn(targetBtn);

        // Drop depuis l'Explorateur Windows
        if (_dragSource == null && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files?.Length > 0)
            {
                if (Directory.Exists(files[0]))
                    CreateFolderShortcutFromDrop(files[0], targetRow, targetCol);
                else if (Path.GetExtension(files[0]).Equals(".url", StringComparison.OrdinalIgnoreCase))
                    CreateUrlShortcutFromDrop(files[0], targetRow, targetCol);
            }
            return;
        }

        // Drag & drop interne entre tuiles
        if (_dragSource == null) return;
        if (_dragSource.Row == targetRow && _dragSource.Col == targetCol) return;

        var all    = ShortcutService.Load();
        var source = all.FirstOrDefault(s => s.Page == _currentPage && s.Row == _dragSource.Row && s.Col == _dragSource.Col);
        var target = all.FirstOrDefault(s => s.Page == _currentPage && s.Row == targetRow       && s.Col == targetCol);

        if (source == null) return;

        if (target != null)
            (source.Row, source.Col, target.Row, target.Col) =
            (target.Row, target.Col, source.Row, source.Col);
        else
            (source.Row, source.Col) = (targetRow, targetCol);

        ShortcutService.Save(all);
        PopulateGrid();
    }

    private static bool IsExplorerDrop(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return false;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files?.Length > 0)
            return Directory.Exists(files[0]) ||
                   Path.GetExtension(files[0]).Equals(".url", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private void CreateFolderShortcutFromDrop(string folderPath, int row, int col)
    {
        string name = Path.GetFileName(folderPath);
        if (string.IsNullOrEmpty(name)) name = folderPath.TrimEnd('\\', '/');

        var entry = new ShortcutEntry
        {
            Page            = _currentPage,
            Row             = row,
            Col             = col,
            Name            = name,
            Type            = ShortcutType.OpenFolder,
            Command         = folderPath,
            IconProfilePath = EnsureDefaultFolderIcon(),
        };

        SaveDroppedEntry(entry, row, col);
    }

    private void CreateUrlShortcutFromDrop(string urlFilePath, int row, int col)
    {
        string? url = null;
        string? title = null;
        try
        {
            foreach (var line in File.ReadLines(urlFilePath))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    url = line[4..];
                else if (line.StartsWith("Title=", StringComparison.OrdinalIgnoreCase))
                    title = line[6..];
            }
        }
        catch { }

        if (string.IsNullOrEmpty(url)) return;
        title ??= Path.GetFileNameWithoutExtension(urlFilePath);

        var entry = new ShortcutEntry
        {
            Page            = _currentPage,
            Row             = row,
            Col             = col,
            Name            = title,
            Type            = ShortcutType.OpenUrl,
            Command         = url,
            IconProfilePath = EnsureDefaultBrowserIcon(),
        };

        SaveDroppedEntry(entry, row, col);
    }

    private void SaveDroppedEntry(ShortcutEntry entry, int row, int col)
    {
        var all      = ShortcutService.Load();
        var existing = all.FirstOrDefault(s => s.Page == _currentPage && s.Row == row && s.Col == col);

        if (existing != null)
        {
            // Case occupée : ouvrir le dialog pré-rempli
            entry.Page = _currentPage;
            var dlg = new ShortcutDialog(entry) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            all.Remove(existing);
            all.Add(dlg.Entry);
        }
        else
        {
            all.Add(entry);
        }

        ShortcutService.Save(all);
        PopulateGrid();
    }

    private static string? EnsureDefaultFolderIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/folder.png"));
            if (info == null) return null;
            using var ms = new System.IO.MemoryStream();
            info.Stream.CopyTo(ms);
            return IconCacheService.CacheBytes(ms.ToArray(), ".png");
        }
        catch { return null; }
    }

    private static string? EnsureDefaultBrowserIcon()
    {
        try
        {
            using var userChoice = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");
            var progId = userChoice?.GetValue("ProgId") as string;
            if (string.IsNullOrEmpty(progId)) return null;

            using var cmdKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(
                $@"{progId}\shell\open\command");
            var cmd = cmdKey?.GetValue(null) as string;
            if (string.IsNullOrEmpty(cmd)) return null;

            var (exe, _) = ParseCommand(cmd);
            return File.Exists(exe) ? IconCacheService.CopyToProfile(exe) : null;
        }
        catch { return null; }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        var all = ShortcutService.Load();
        if (IconCacheService.SyncAll(all))
            ShortcutService.Save(all);

        var pages = PageConfigService.Load();
        if (IconCacheService.SyncAllPages(pages))
            PageConfigService.Save(pages);

        PopulateGrid();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void HideToSystray_Click(object sender, RoutedEventArgs e)
    {
        ClearSearch();
        Hide();
    }

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        if (!AppDialog.Confirm("Quitter DockPad ?", owner: this))
            return;

        App.Exit();
    }

    private void EditConfig_Click(object sender, RoutedEventArgs e)
    {
        ShortcutService.OpenInEditor();
    }

    private static (string exe, string args) ParseCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            if (end > 0)
                return (command[1..end], command[(end + 1)..].Trim());
        }
        int space = command.IndexOf(' ');
        return space > 0
            ? (command[..space], command[(space + 1)..])
            : (command, "");
    }

    private static BitmapSource? LoadIcon(string iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath)) return null;
        try
        {
            string path = iconPath.Split(',')[0].Trim('"').Trim();
            if (!File.Exists(path)) return null;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".ico" or ".png" or ".bmp" or ".jpg" or ".jpeg")
                return new BitmapImage(new Uri(path));

            if (ext is ".exe" or ".dll")
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is null) return null;
                using var bmp = icon.ToBitmap();
                var handle = bmp.GetHbitmap();
                try
                {
                    return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        handle, IntPtr.Zero, Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                }
                finally { DeleteObject(handle); }
            }
        }
        catch { }
        return null;
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    // ── Triggers dynamiques ───────────────────────────────────────────────────

    private void UpdateTriggerMods()
    {
        // Configuration explicite (Options) prioritaire
        var (first, second) = SettingsService.LoadTriggerMods();
        var modFirst  = ParseTriggerMod(first);
        var modSecond = ParseTriggerMod(second);
        if (modFirst != null && modSecond != null && modFirst != modSecond)
        {
            _triggerFirst  = modFirst.Value;
            _triggerSecond = modSecond.Value;
            return;
        }

        // Auto : éviter le modificateur du raccourci global
        var (mods, _) = SettingsService.LoadHotkey();
        if ((mods & HotkeyService.MOD_CONTROL) != 0)
        {
            // Hotkey Ctrl → triggers Shift / Alt
            _triggerFirst  = ModifierKeys.Shift;
            _triggerSecond = ModifierKeys.Alt;
        }
        else
        {
            // Hotkey Alt ou Shift → triggers Ctrl / Shift
            _triggerFirst  = ModifierKeys.Control;
            _triggerSecond = ModifierKeys.Shift;
        }
    }

    private static ModifierKeys? ParseTriggerMod(string name) => name switch
    {
        "Ctrl"  => ModifierKeys.Control,
        "Alt"   => ModifierKeys.Alt,
        "Shift" => ModifierKeys.Shift,
        _       => null
    };

    // Vérifie si le modificateur est pressé seul (sans autre modificateur)
    private static bool IsAloneModifier(KeyEventArgs e, ModifierKeys mod) => mod switch
    {
        ModifierKeys.Control => e.Key is Key.LeftCtrl  or Key.RightCtrl  && Keyboard.Modifiers == ModifierKeys.Control,
        ModifierKeys.Shift   => e.Key is Key.LeftShift or Key.RightShift && Keyboard.Modifiers == ModifierKeys.Shift,
        ModifierKeys.Alt     => (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt)
                                && Keyboard.Modifiers == ModifierKeys.Alt,
        _ => false
    };

    private static bool IsModHeld(ModifierKeys mod) => (Keyboard.Modifiers & mod) != 0;

    private static bool IsModifierReleased(KeyEventArgs e, ModifierKeys mod) => mod switch
    {
        ModifierKeys.Control => e.Key is Key.LeftCtrl  or Key.RightCtrl,
        ModifierKeys.Shift   => e.Key is Key.LeftShift or Key.RightShift,
        ModifierKeys.Alt     => e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt,
        _ => false
    };

    // ── Overlay raccourcis clavier ────────────────────────────────────────────

    // Mapping : 1-9 en lecture (gauche→droite, haut→bas) dans une zone 3×3 (rows 0-2) ;
    // ligne du bas (row 3) : 0, ↑ (keyNum 10), ↓ (keyNum 11)
    // Ctrl → cols 0-2 (gauche), Shift → cols 3-5 (droite)
    private static (int Row, int Col) HintKeyToCell(int keyNum, bool isCtrl)
    {
        int baseCol = isCtrl ? 0 : 3;
        if (keyNum == 0)  return (3, baseCol);     // 0 → sous le 1
        if (keyNum == 10) return (3, baseCol + 1); // ↑
        if (keyNum == 11) return (3, baseCol + 2); // ↓
        int row = (keyNum - 1) / 3;
        int col = (keyNum - 1) % 3 + baseCol;
        return (row, col);
    }

    private void ShowHintOverlay(bool isCtrl)
    {
        HideHintOverlay();

        for (int row = 0; row < GridRows; row++)
        {
            for (int col = 0; col < GridCols; col++)
            {
                if (isCtrl ? col >= 3 : col < 3) continue; // côté inactif

                // Rows 0-2 : chiffres 1-9 ; row 3 : 0, ↑, ↓
                string label = row < 3
                    ? (row * 3 + col % 3 + 1).ToString()
                    : (col % 3) switch { 0 => "0", 1 => "↑", _ => "↓" };

                AddHintOverlayElement(row, col, new Border
                {
                    Margin = new Thickness(5),
                    Background = new SolidColorBrush(Color.FromArgb(0x55, 0x60, 0x60, 0x60)),
                    IsHitTestVisible = false,
                    CornerRadius = new CornerRadius(6),
                    SnapsToDevicePixels = true, UseLayoutRounding = true,
                });

                AddHintOverlayElement(row, col, new Border
                {
                    Width = 20, Height = 20,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(12, 12, 0, 0),
                    Background = new SolidColorBrush(Color.FromArgb(0xBB, 0x55, 0x55, 0x55)),
                    CornerRadius = new CornerRadius(4),
                    IsHitTestVisible = false,
                    SnapsToDevicePixels = true, UseLayoutRounding = true,
                    Child = new TextBlock
                    {
                        Text = label,
                        FontSize = 11, FontWeight = FontWeights.SemiBold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    }
                });
            }
        }
    }

    private void AddHintOverlayElement(int row, int col, UIElement element)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, col);
        ShortcutsGrid.Children.Add(element);
        _hintElements.Add(element);
    }

    private void HideHintOverlay()
    {
        foreach (var el in _hintElements)
            ShortcutsGrid.Children.Remove(el);
        _hintElements.Clear();
    }

    private void ExecuteByHintKey(int keyNum, bool isCtrl)
    {
        var (row, col) = HintKeyToCell(keyNum, isCtrl);
        var entry = ShortcutService.Load()
            .FirstOrDefault(s => s.Page == _currentPage && s.Row == row && s.Col == col);
        if (entry != null)
            ExecuteEntry(entry);
    }

    // Retourne 0-9 pour les chiffres, 10 pour ↑, 11 pour ↓, null sinon.
    private static int? GetHintKey(Key key)
    {
        // Pavé numérique : Shift « annule » temporairement NumLock (comportement Windows)
        // et les chiffres arrivent en touches de navigation NON-étendues (End, Up, PgDn…).
        // On les remappe en chiffres — idem quand NumLock est éteint. Les vraies touches
        // de navigation, elles, sont étendues (bit 24 du lParam) et gardent leur rôle.
        // Le flag est lu sur le message clavier EN COURS via CurrentKeyboardMessage —
        // WPF ne l'expose pas dans KeyEventArgs, et un hook WndProc/ThreadPreprocessMessage
        // arrive trop tard ou dans le mauvais ordre par rapport au traitement clavier WPF.
        bool extended = (ComponentDispatcher.CurrentKeyboardMessage.lParam.ToInt64() & 0x0100_0000) != 0;
        if (!extended)
        {
            int? numpad = key switch
            {
                Key.Insert => 0, Key.End  => 1, Key.Down  => 2, Key.Next  => 3,
                Key.Left   => 4, Key.Clear => 5, Key.Right => 6, Key.Home => 7,
                Key.Up     => 8, Key.Prior => 9,
                _ => null
            };
            if (numpad != null) return numpad;
        }

        if (key == Key.Up)   return 10; // ↑ → row 3, 2e case de la zone
        if (key == Key.Down) return 11; // ↓ → row 3, 3e case de la zone

        // VK_0=0x30...VK_9=0x39, VK_NUMPAD0=0x60...VK_NUMPAD9=0x69
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk is >= 0x30 and <= 0x39) return vk - 0x30;
        if (vk is >= 0x60 and <= 0x69) return vk - 0x60;
        return null;
    }

    // ── Recherche ────────────────────────────────────────────────────────────

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            SearchPopup.IsOpen = false;
            return;
        }

        var results = ShortcutService.Load()
            .Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Name)
            .Select(s => new SearchResultItem(s, LoadIcon(IconCacheService.ResolveProfilePath(s.IconProfilePath) ?? s.IconPath)))
            .ToList();

        SearchResults.ItemsSource = results;
        SearchPopup.IsOpen = results.Count > 0;
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when SearchPopup.IsOpen && SearchResults.Items.Count > 0:
                SearchResults.SelectedIndex = 0;
                (SearchResults.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
                e.Handled = true;
                break;
            case Key.Enter when SearchPopup.IsOpen:
                int idx = SearchResults.SelectedIndex >= 0 ? SearchResults.SelectedIndex : 0;
                if (SearchResults.Items.Count > 0 && SearchResults.Items[idx] is SearchResultItem hit)
                    ExecuteSearchResult(hit);
                e.Handled = true;
                break;
            case Key.Escape:
                ClearSearch();
                e.Handled = true;
                break;
        }
    }

    private void SearchResults_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when SearchResults.SelectedItem is SearchResultItem item:
                ExecuteSearchResult(item);
                e.Handled = true;
                break;
            case Key.Escape:
                SearchBox.Focus();
                SearchBox.SelectAll();
                SearchPopup.IsOpen = false;
                e.Handled = true;
                break;
            case Key.Up when SearchResults.SelectedIndex <= 0:
                SearchBox.Focus();
                e.Handled = true;
                break;
            case Key.Back when SearchBox.Text.Length > 0:
                SearchBox.Text = SearchBox.Text[..^1];
                SearchBox.Focus();
                SearchBox.CaretIndex = SearchBox.Text.Length;
                e.Handled = true;
                break;
            case Key.Back:
                SearchBox.Focus();
                e.Handled = true;
                break;
        }
    }

    private void SearchResults_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (SearchResults.SelectedItem is SearchResultItem item)
            ExecuteSearchResult(item);
    }

    private void ExecuteSearchResult(SearchResultItem result)
    {
        ClearSearch();
        ExecuteEntry(result.Entry);
    }

    private void ClearSearch()
    {
        SearchBox.Text = "";
        SearchPopup.IsOpen = false;
        if (_hintIsCtrl != null) { _hintIsCtrl = null; HideHintOverlay(); }
    }

    private sealed record SearchResultItem(ShortcutEntry Entry, BitmapSource? Icon)
    {
        public string TypeLabel => Entry.Type switch
        {
            ShortcutType.OpenFolder   => "Dossier",
            ShortcutType.OpenUrl      => "URL",
            ShortcutType.OpenTerminal => "Terminal",
            _                         => "Commande",
        };

        public SolidColorBrush TypeBrush => Entry.Type switch
        {
            ShortcutType.OpenFolder      => new SolidColorBrush(Color.FromRgb(0xF5, 0xCC, 0x80)),
            ShortcutType.OpenUrl         => new SolidColorBrush(Color.FromRgb(0x92, 0xC6, 0x90)),
            ShortcutType.OpenTerminal    => new SolidColorBrush(Color.FromRgb(0xC4, 0xAD, 0xE0)),
            ShortcutType.SwitchToProcess => new SolidColorBrush(Color.FromRgb(0xF4, 0xA4, 0xA4)),
            _                            => new SolidColorBrush(Color.FromRgb(0xA8, 0xCC, 0xEA)),
        };
    }
}
