using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// Actions sur la grille de raccourcis, partagées UI ↔ MCP.
/// Cœurs purs (testables, sans IO) + enveloppes (verrou + load/save + icônes).
/// </summary>
public static class ShortcutActionService
{
    public const int GridRows = 4;
    public const int GridCols = 6;

    /// <summary>Partagé : un HttpClient par appel épuiserait les sockets.</summary>
    private static readonly FaviconService Favicon = new();

    // ───────────── Enveloppes (verrou + fichiers) ─────────────

    public static ActionResult GetGrid(int? page = null)
    {
        lock (ConfigLock.Gate)
            return GetGridCore(ShortcutService.Load(), PageConfigService.Load(), page);
    }

    /// <summary>
    /// Ajoute un lot de tuiles, en allant chercher l'icône des tuiles web qui n'en ont pas.
    /// </summary>
    /// <remarks>
    /// Le téléchargement a lieu <b>avant</b> le verrou, jamais dedans : <see cref="ConfigLock.Gate"/>
    /// est le verrou global des configs, et l'y tenir le temps d'un appel réseau bloquerait
    /// l'interface et toute requête MCP concurrente.
    /// </remarks>
    public static async Task<ActionResult> AddAsync(List<ShortcutAddItem> items)
    {
        var favicons = await ResolveFaviconsAsync(items).ConfigureAwait(false);

        lock (ConfigLock.Gate)
        {
            var all = ShortcutService.Load();
            var configs = PageConfigService.Load();
            var result = AddCore(all, configs, items);
            if (!result.Ok) return result;

            // Icônes : fournie → copie profil ; absente → favicon du site, puis icône de l'exe
            // associé (comme les dialogs). AddCore ajoute dans l'ordre des items, d'où l'index.
            int i = 0;
            foreach (var s in all.TakeLast(items.Count))
            {
                if (favicons.TryGetValue(i++, out var stored) && string.IsNullOrEmpty(s.IconPath))
                    s.IconProfilePath = stored;
                ApplyIcon(s);
            }
            ShortcutService.Save(all);
            return result;
        }
    }

    /// <summary>
    /// Variante bloquante, pour les appelants sans contexte asynchrone — le serveur MCP, qui
    /// travaille sur un thread de pipe. À ne pas appeler depuis le thread d'interface.
    /// </summary>
    public static ActionResult Add(List<ShortcutAddItem> items) =>
        AddAsync(items).GetAwaiter().GetResult();

    /// <summary>
    /// Icône de site pour les items qui la méritent, indexée par leur position dans le lot.
    /// </summary>
    private static async Task<Dictionary<int, string>> ResolveFaviconsAsync(List<ShortcutAddItem> items)
    {
        var found = new Dictionary<int, string>();
        bool enabled = SettingsService.LoadAutoFavicon();

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (!FaviconService.ShouldFetch(enabled, item.Type, item.IconPath, item.Command)) continue;

            if (await Favicon.TryFetchIntoStoreAsync(item.Command, CancellationToken.None)
                    .ConfigureAwait(false) is { } stored)
                found[i] = stored;
        }
        return found;
    }

    /// <summary>
    /// Modifie une tuile, en allant chercher l'icône du site si elle devient une tuile web sans icône.
    /// </summary>
    /// <remarks>
    /// L'état de la tuile est lu une première fois <b>hors du verrou</b> pour décider s'il faut
    /// télécharger. Deux modifications simultanées de la même case peuvent donc faire télécharger
    /// une icône qui ne servira pas — un fichier de plus dans le store dédupliqué, contre un appel
    /// réseau sous le verrou global. Le compromis est vite vu.
    /// </remarks>
    public static async Task<ActionResult> UpdateAsync(int page, int row, int col, ShortcutUpdate changes)
    {
        string? favicon = await ResolveFaviconForUpdateAsync(page, row, col, changes).ConfigureAwait(false);

        lock (ConfigLock.Gate)
        {
            var all = ShortcutService.Load();
            var result = UpdateCore(all, page, row, col, changes);
            if (!result.Ok) return result;
            if (changes.IconPath != null || changes.Command != null || changes.Type != null)
            {
                var s = all.First(s => s.Page == page && s.Row == row && s.Col == col);
                if (changes.IconPath != null) { s.IconPath = changes.IconPath; s.IconProfilePath = null; }
                if (favicon != null && string.IsNullOrEmpty(s.IconPath)) s.IconProfilePath = favicon;
                ApplyIcon(s);
            }
            ShortcutService.Save(all);
            return result;
        }
    }

    /// <summary>Variante bloquante — voir <see cref="Add"/>.</summary>
    public static ActionResult Update(int page, int row, int col, ShortcutUpdate changes) =>
        UpdateAsync(page, row, col, changes).GetAwaiter().GetResult();

    private static async Task<string?> ResolveFaviconForUpdateAsync(
        int page, int row, int col, ShortcutUpdate changes)
    {
        ShortcutEntry? existing;
        lock (ConfigLock.Gate)
            existing = ShortcutService.Load()
                .FirstOrDefault(s => s.Page == page && s.Row == row && s.Col == col);

        if (existing is null) return null;

        // L'état tel qu'il sera après la modification : seuls les champs fournis changent.
        var type = changes.Type ?? existing.Type;
        var command = changes.Command ?? existing.Command;
        var icon = changes.IconPath ?? existing.IconPath;

        return FaviconService.ShouldFetch(SettingsService.LoadAutoFavicon(), type, icon, command)
            ? await Favicon.TryFetchIntoStoreAsync(command, CancellationToken.None).ConfigureAwait(false)
            : null;
    }

    public static ActionResult Move(int page, int row, int col, int toPage, int? toRow = null, int? toCol = null)
    {
        lock (ConfigLock.Gate)
        {
            var all = ShortcutService.Load();
            var configs = PageConfigService.Load();
            var result = MoveCore(all, configs, page, row, col, toPage, toRow, toCol);
            if (result.Ok) ShortcutService.Save(all);
            return result;
        }
    }

    public static ActionResult Delete(int page, int row, int col)
    {
        lock (ConfigLock.Gate)
        {
            var all = ShortcutService.Load();
            var result = DeleteCore(all, page, row, col);
            if (result.Ok) ShortcutService.Save(all);
            return result;
        }
    }

    /// <summary>Utilisée par l'UI uniquement (⧉ Dupliquer) — non exposée côté MCP.</summary>
    public static ActionResult Duplicate(int page, int row, int col)
    {
        lock (ConfigLock.Gate)
        {
            var all = ShortcutService.Load();
            var result = DuplicateCore(all, page, row, col);
            if (result.Ok) ShortcutService.Save(all);
            return result;
        }
    }

    // ───────────── Cœurs purs ─────────────

    public static ActionResult GetGridCore(List<ShortcutEntry> all, List<PageConfig> configs, int? page)
    {
        int maxUsed   = all.Count     > 0 ? all.Max(s => s.Page)      : -1;
        int maxConfig = configs.Count > 0 ? configs.Max(p => p.Index) : -1;
        int lastShown = Math.Max(Math.Max(maxUsed, maxConfig), 0);

        if (page is { } p && (p < 0 || p > lastShown))
            return ActionResult.Fail($"Page {p} inexistante (pages 0 à {lastShown}).");

        var pages = new List<object>();
        for (int i = 0; i <= lastShown; i++)
        {
            if (page is { } f && f != i) continue;
            var occupied = all.Where(s => s.Page == i).Select(s => (s.Row, s.Col)).ToHashSet();
            var free = new List<object>();
            for (int r = 0; r < GridRows; r++)
                for (int c = 0; c < GridCols; c++)
                    if (!occupied.Contains((r, c))) free.Add(new { row = r, col = c });
            pages.Add(new { index = i, tileCount = occupied.Count, freeCells = free });
        }

        var shortcuts = all
            .Where(s => page is null || s.Page == page)
            .OrderBy(s => s.Page).ThenBy(s => s.Row).ThenBy(s => s.Col)
            .Select(s => new { page = s.Page, row = s.Row, col = s.Col, name = s.Name,
                               type = s.Type.ToString(), command = s.Command,
                               iconPath = s.IconPath,
                               iconProfilePath = s.IconProfilePath })
            .ToList();

        return ActionResult.Success(new { gridRows = GridRows, gridCols = GridCols, pages, shortcuts });
    }

    public static ActionResult AddCore(List<ShortcutEntry> all, List<PageConfig> configs, List<ShortcutAddItem> items)
    {
        if (items is not { Count: > 0 })
            return ActionResult.Fail("Aucun raccourci à ajouter.");

        // Bornée sur l'état initial : un lot ciblant une page qui n'existe pas encore
        // échoue en entier, même si un item antérieur du même lot créerait cette page.
        int lastShown = LastShown(all, configs);

        var errors = new List<string>();
        var staged = new List<ShortcutEntry>();
        // occupation simulée : existant + items déjà placés dans ce lot
        var occupied = all.Select(s => (s.Page, s.Row, s.Col)).ToHashSet();

        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            string id = $"item {i + 1} ({(string.IsNullOrWhiteSpace(it.Name) ? "sans nom" : it.Name)})";

            if (string.IsNullOrWhiteSpace(it.Name)) { errors.Add($"{id} : nom requis."); continue; }
            if (string.IsNullOrWhiteSpace(it.Command)) { errors.Add($"{id} : commande requise."); continue; }
            if (it.Row.HasValue != it.Col.HasValue) { errors.Add($"{id} : row et col vont ensemble."); continue; }

            int page = it.Page ?? 0;
            if (page < 0) { errors.Add($"{id} : page invalide."); continue; }
            if (page > lastShown) { errors.Add($"{id} : page {page} inexistante (pages 0 à {lastShown}). Crée-la d'abord avec dockpad_page_add."); continue; }

            (int row, int col)? dest = null;
            if (it.Row is { } r0 && it.Col is { } c0)
            {
                if (r0 < 0 || r0 >= GridRows || c0 < 0 || c0 >= GridCols)
                { errors.Add($"{id} : position hors bornes (lignes 0-{GridRows - 1}, colonnes 0-{GridCols - 1})."); continue; }
                if (occupied.Contains((page, r0, c0)))
                {
                    var occ = all.FirstOrDefault(s => s.Page == page && s.Row == r0 && s.Col == c0)?.Name
                              ?? staged.First(s => s.Page == page && s.Row == r0 && s.Col == c0).Name;
                    errors.Add($"{id} : case (page {page}, ligne {r0}, colonne {c0}) occupée par « {occ} ». " +
                               $"Cases libres : {FreeCellsText(occupied, page)}");
                    continue;
                }
                dest = (r0, c0);
            }
            else
            {
                dest = FirstFree(occupied, page);
                if (dest is null) { errors.Add($"{id} : page {page} pleine ({GridRows * GridCols} cases)."); continue; }
            }

            var entry = new ShortcutEntry
            {
                Page = page, Row = dest.Value.row, Col = dest.Value.col,
                Name = it.Name, Type = it.Type, Command = it.Command,
                IconPath = it.IconPath ?? "",
                Terminal = it.Terminal, ProcessSwitch = it.ProcessSwitch,
            };
            staged.Add(entry);
            occupied.Add((page, entry.Row, entry.Col));
        }

        if (errors.Count > 0)
            return ActionResult.Fail("Lot refusé (tout ou rien) :\n- " + string.Join("\n- ", errors));

        all.AddRange(staged);
        return ActionResult.Success(new
        {
            added = staged.Select(s => new { s.Name, page = s.Page, row = s.Row, col = s.Col }).ToList()
        });
    }

    public static ActionResult UpdateCore(List<ShortcutEntry> all, int page, int row, int col, ShortcutUpdate changes)
    {
        var s = all.FirstOrDefault(s => s.Page == page && s.Row == row && s.Col == col);
        if (s is null) return ActionResult.Fail($"Aucune tuile en page {page}, ligne {row}, colonne {col}.");

        if (changes.Name is { } n)
        {
            if (string.IsNullOrWhiteSpace(n)) return ActionResult.Fail("Le nom ne peut pas être vide.");
            s.Name = n;
        }
        if (changes.Type is { } t) s.Type = t;
        if (changes.Command is { } c)
        {
            if (string.IsNullOrWhiteSpace(c)) return ActionResult.Fail("La commande ne peut pas être vide.");
            s.Command = c;
        }
        if (changes.Terminal is { } term) s.Terminal = term;
        if (changes.ProcessSwitch is { } ps) s.ProcessSwitch = ps;
        // IconPath appliqué par l'enveloppe (copie profil)

        return ActionResult.Success(new { s.Name, page = s.Page, row = s.Row, col = s.Col });
    }

    public static ActionResult MoveCore(List<ShortcutEntry> all, List<PageConfig> configs, int page, int row, int col,
                                        int toPage, int? toRow, int? toCol)
    {
        var s = all.FirstOrDefault(s => s.Page == page && s.Row == row && s.Col == col);
        if (s is null) return ActionResult.Fail($"Aucune tuile en page {page}, ligne {row}, colonne {col}.");
        if (toPage < 0) return ActionResult.Fail("Page cible invalide.");
        int lastShown = LastShown(all, configs);
        if (toPage > lastShown)
            return ActionResult.Fail($"Page cible {toPage} inexistante (pages 0 à {lastShown}). Crée-la d'abord avec dockpad_page_add.");
        if (toRow.HasValue != toCol.HasValue) return ActionResult.Fail("toRow et toCol vont ensemble.");

        var occupied = all.Where(x => x.Page == toPage && x != s)
                          .Select(x => (x.Row, x.Col)).ToHashSet();

        var occupiedKeyed = occupied.Select(o => (toPage, o.Row, o.Col)).ToHashSet();

        (int row, int col) dest;
        if (toRow is { } tr && toCol is { } tc)
        {
            if (tr < 0 || tr >= GridRows || tc < 0 || tc >= GridCols)
                return ActionResult.Fail($"Position hors bornes (lignes 0-{GridRows - 1}, colonnes 0-{GridCols - 1}).");
            if (occupied.Contains((tr, tc)))
            {
                var occ = all.First(x => x.Page == toPage && x.Row == tr && x.Col == tc && x != s).Name;
                return ActionResult.Fail($"Case (page {toPage}, ligne {tr}, colonne {tc}) occupée par « {occ} ». " +
                                         $"Cases libres : {FreeCellsText(occupiedKeyed, toPage)}");
            }
            dest = (tr, tc);
        }
        else
        {
            // règle de l'app : même position si libre, sinon première case disponible
            if (!occupied.Contains((s.Row, s.Col)))
                dest = (s.Row, s.Col);
            else if (FirstFree(occupiedKeyed, toPage) is { } free)
                dest = free;
            else
                return ActionResult.Fail($"Page {toPage} pleine ({GridRows * GridCols} cases).");
        }

        s.Page = toPage; s.Row = dest.row; s.Col = dest.col;
        return ActionResult.Success(new { s.Name, page = s.Page, row = s.Row, col = s.Col });
    }

    public static ActionResult DeleteCore(List<ShortcutEntry> all, int page, int row, int col)
    {
        var s = all.FirstOrDefault(s => s.Page == page && s.Row == row && s.Col == col);
        if (s is null) return ActionResult.Fail($"Aucune tuile en page {page}, ligne {row}, colonne {col}.");
        all.Remove(s);
        return ActionResult.Success(new { deleted = s.Name });
    }

    public static ActionResult DuplicateCore(List<ShortcutEntry> all, int page, int row, int col)
    {
        var s = all.FirstOrDefault(s => s.Page == page && s.Row == row && s.Col == col);
        if (s is null) return ActionResult.Fail($"Aucune tuile en page {page}, ligne {row}, colonne {col}.");

        var occupied = all.Where(x => x.Page == page).Select(x => (x.Row, x.Col)).ToHashSet();
        (int row, int col)? nearest = null;
        int best = int.MaxValue;
        for (int r = 0; r < GridRows; r++)
            for (int c = 0; c < GridCols; c++)
            {
                if (occupied.Contains((r, c))) continue;
                int dist = Math.Max(Math.Abs(r - row), Math.Abs(c - col));
                if (dist < best) { best = dist; nearest = (r, c); }
            }
        if (nearest is null) return ActionResult.Fail("Page pleine. Naviguez vers une autre page pour dupliquer.");

        all.Add(new ShortcutEntry
        {
            Page = page, Row = nearest.Value.row, Col = nearest.Value.col,
            Name = s.Name, Type = s.Type, Command = s.Command, IconPath = s.IconPath,
            IconProfilePath = s.IconProfilePath,
            Terminal = s.Terminal is null ? null : new TerminalConfig
            {
                ExePath = s.Terminal.ExePath, StartingDirectory = s.Terminal.StartingDirectory,
                RunCommand = s.Terminal.RunCommand, NewTab = s.Terminal.NewTab, ExtraArgs = s.Terminal.ExtraArgs,
            },
            ProcessSwitch = s.ProcessSwitch is null ? null : new ProcessSwitchConfig
            {
                SearchMode = s.ProcessSwitch.SearchMode,
                ProcessName = s.ProcessSwitch.ProcessName, Executable = s.ProcessSwitch.Executable,
                Parameters = s.ProcessSwitch.Parameters,
            },
        });
        return ActionResult.Success(new { page, row = nearest.Value.row, col = nearest.Value.col });
    }

    // ───────────── Aides ─────────────

    private static int LastShown(List<ShortcutEntry> all, List<PageConfig> configs)
    {
        int maxUsed   = all.Count     > 0 ? all.Max(s => s.Page)      : -1;
        int maxConfig = configs.Count > 0 ? configs.Max(p => p.Index) : -1;
        return Math.Max(Math.Max(maxUsed, maxConfig), 0);
    }

    private static (int row, int col)? FirstFree(HashSet<(int, int, int)> occupied, int page)
    {
        for (int r = 0; r < GridRows; r++)
            for (int c = 0; c < GridCols; c++)
                if (!occupied.Contains((page, r, c))) return (r, c);
        return null;
    }

    private static string FreeCellsText(HashSet<(int, int, int)> occupied, int page)
    {
        var free = new List<string>();
        for (int r = 0; r < GridRows; r++)
            for (int c = 0; c < GridCols; c++)
                if (!occupied.Contains((page, r, c))) free.Add($"({r},{c})");
        return free.Count > 0 ? string.Join(" ", free) : "aucune";
    }

    /// <summary>Icône fournie → copie profil ; absente → icône de l'exe associé (RunCommand/SwitchToProcess/OpenTerminal).</summary>
    private static void ApplyIcon(ShortcutEntry s)
    {
        if (!string.IsNullOrEmpty(s.IconPath))
        {
            s.IconProfilePath ??= IconStoreService.CopyToProfile(s.IconPath);
            return;
        }
        string? exe = s.Type switch
        {
            ShortcutType.RunCommand      => FirstToken(s.Command),
            ShortcutType.SwitchToProcess => s.ProcessSwitch?.Executable,
            ShortcutType.OpenTerminal    => s.Terminal?.ExePath,
            _ => null,
        };
        if (exe is not null && File.Exists(exe))
        {
            s.IconPath = exe;
            s.IconProfilePath = IconStoreService.CopyToProfile(exe);
        }
    }

    /// <summary>Premier token d'une commande ("C:\a b\x.exe" args → C:\a b\x.exe).</summary>
    private static string FirstToken(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 0 ? command[1..end] : command.Trim('"');
        }
        int sp = command.IndexOf(' ');
        return sp > 0 ? command[..sp] : command;
    }
}
