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

    public static ActionResult GetGrid(int? page = null, TileFiles? files = null)
    {
        files ??= TileStore.FilesFor(TileTarget.Shortcuts);
        lock (ConfigLock.Gate)
            return GetGridCore(ShortcutService.Load(files.EntriesPath),
                               PageConfigService.Load(files.PagesPath), page);
    }

    /// <summary>
    /// Ajoute un lot de tuiles, en allant chercher l'icône des tuiles web qui n'en ont pas.
    /// </summary>
    /// <remarks>
    /// Le téléchargement a lieu <b>avant</b> le verrou, jamais dedans : <see cref="ConfigLock.Gate"/>
    /// est le verrou global des configs, et l'y tenir le temps d'un appel réseau bloquerait
    /// l'interface et toute requête MCP concurrente.
    /// </remarks>
    public static async Task<ActionResult> AddAsync(List<ShortcutAddItem> items, TileFiles? files = null)
    {
        files ??= TileStore.FilesFor(TileTarget.Shortcuts);
        var favicons = await ResolveFaviconsAsync(items).ConfigureAwait(false);

        lock (ConfigLock.Gate)
        {
            var all = ShortcutService.Load(files.EntriesPath);
            var configs = PageConfigService.Load(files.PagesPath);
            var staged = new List<ShortcutEntry>();
            var result = AddCore(all, configs, items, staged);
            if (!result.Ok) return result;

            // Icônes : fournie → copie profil ; absente → favicon du site, puis icône de l'exe
            // associé (comme les dialogs). staged suit l'ordre des items, d'où l'index — et non la
            // fin de la liste : une tuile posée dans une sous-case n'y est pas ajoutée.
            int i = 0;
            foreach (var s in staged)
            {
                if (favicons.TryGetValue(i++, out var stored) && string.IsNullOrEmpty(s.IconPath))
                    s.IconProfilePath = stored;
                ApplyIcon(s);
            }
            ShortcutService.Save(all, files.EntriesPath);
            return result;
        }
    }

    /// <summary>
    /// Variante bloquante, pour les appelants sans contexte asynchrone — le serveur MCP, qui
    /// travaille sur un thread de pipe. À ne pas appeler depuis le thread d'interface.
    /// </summary>
    public static ActionResult Add(List<ShortcutAddItem> items, TileFiles? files = null) =>
        AddAsync(items, files).GetAwaiter().GetResult();

    public static async Task<ActionResult> AddInGroupAsync(ShortcutAddItem item, int slot, TileFiles files)
    {
        var favicons = await ResolveFaviconsAsync([item]).ConfigureAwait(false);
        return TileGroupService.Mutate(files, all =>
        {
            var address = new TileAddress(item.Page ?? 0, item.Row ?? -1, item.Col ?? -1, slot);
            if (!TileGroupService.IsValidDestination(all, address) || TileGroupService.Get(all, address) is not null)
                return ActionResult.Fail(Loc.T("Group_InvalidDestination"));
            var staged = new List<ShortcutEntry>();
            var result = AddCore(staged, PageConfigService.Load(files.PagesPath)
                .Concat([new PageConfig { Index = address.Page }]).ToList(), [item]);
            if (!result.Ok) return result;
            var entry = staged.Single();
            if (favicons.TryGetValue(0, out var stored) && string.IsNullOrEmpty(entry.IconPath))
                entry.IconProfilePath = stored;
            ApplyIcon(entry);
            TileGroupService.Put(all, address, entry);
            return ActionResult.Success();
        });
    }

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
    public static async Task<ActionResult> UpdateAsync(int page, int row, int col, ShortcutUpdate changes,
                                                       TileFiles? files = null, int? slot = null)
    {
        files ??= TileStore.FilesFor(TileTarget.Shortcuts);
        string? favicon = await ResolveFaviconForUpdateAsync(page, row, col, changes, files, slot).ConfigureAwait(false);

        lock (ConfigLock.Gate)
        {
            var all = ShortcutService.Load(files.EntriesPath);
            var result = UpdateCore(all, page, row, col, changes, slot);
            if (!result.Ok) return result;
            if (changes.IconPath != null || changes.Command != null || changes.Type != null)
            {
                var s = TileGroupService.Get(all, new(page, row, col, slot))!;
                if (changes.IconPath != null) { s.IconPath = changes.IconPath; s.IconProfilePath = null; }
                if (favicon != null && string.IsNullOrEmpty(s.IconPath)) s.IconProfilePath = favicon;
                ApplyIcon(s);
            }
            ShortcutService.Save(all, files.EntriesPath);
            return result;
        }
    }

    /// <summary>Variante bloquante — voir <see cref="Add"/>.</summary>
    public static ActionResult Update(int page, int row, int col, ShortcutUpdate changes,
                                      TileFiles? files = null, int? slot = null) =>
        UpdateAsync(page, row, col, changes, files, slot).GetAwaiter().GetResult();

    private static async Task<string?> ResolveFaviconForUpdateAsync(
        int page, int row, int col, ShortcutUpdate changes, TileFiles files, int? slot)
    {
        ShortcutEntry? existing;
        lock (ConfigLock.Gate)
            existing = TileGroupService.Get(ShortcutService.Load(files.EntriesPath), new(page, row, col, slot));

        if (existing is null) return null;

        // L'état tel qu'il sera après la modification : seuls les champs fournis changent.
        var type = changes.Type ?? existing.Type;
        var command = changes.Command ?? existing.Command;
        var icon = changes.IconPath ?? existing.IconPath;

        return FaviconService.ShouldFetch(SettingsService.LoadAutoFavicon(), type, icon, command)
            ? await Favicon.TryFetchIntoStoreAsync(command, CancellationToken.None).ConfigureAwait(false)
            : null;
    }

    public static ActionResult Move(int page, int row, int col, int toPage, int? toRow = null,
                                    int? toCol = null, TileFiles? files = null, int? slot = null,
                                    int? toSlot = null)
    {
        files ??= TileStore.FilesFor(TileTarget.Shortcuts);
        lock (ConfigLock.Gate)
        {
            var all = ShortcutService.Load(files.EntriesPath);
            var configs = PageConfigService.Load(files.PagesPath);
            var result = MoveCore(all, configs, page, row, col, toPage, toRow, toCol, slot, toSlot);
            if (result.Ok) ShortcutService.Save(all, files.EntriesPath);
            return result;
        }
    }

    public static ActionResult Delete(int page, int row, int col, TileFiles? files = null, int? slot = null)
    {
        files ??= TileStore.FilesFor(TileTarget.Shortcuts);
        lock (ConfigLock.Gate)
        {
            var all = ShortcutService.Load(files.EntriesPath);
            var result = DeleteCore(all, page, row, col, slot);
            if (result.Ok) ShortcutService.Save(all, files.EntriesPath);
            return result;
        }
    }

    /// <summary>Crée, transforme ou habille une tuile groupée — voir <see cref="GroupSetCore"/>.</summary>
    public static ActionResult GroupSet(int page, int row, int col, TileLayout? layout, string? name,
                                        string? color, TileFiles? files = null)
    {
        files ??= TileStore.FilesFor(TileTarget.Shortcuts);
        lock (ConfigLock.Gate)
        {
            var all = ShortcutService.Load(files.EntriesPath);
            var configs = PageConfigService.Load(files.PagesPath);
            var result = GroupSetCore(all, configs, page, row, col, layout, name, color);
            if (result.Ok) ShortcutService.Save(all, files.EntriesPath);
            return result;
        }
    }

    /// <summary>
    /// Déplace une tuile d'une grille vers l'autre — raccourcis ↔ favoris.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Les deux écritures vivent sous un seul verrou</b>, sinon une requête MCP pourrait
    /// s'intercaler entre le retrait et l'ajout, et voir la tuile nulle part.
    /// </para>
    /// <para>
    /// <b>On écrit la destination AVANT de retirer de la source.</b> Si la seconde écriture
    /// échoue — disque plein, fichier verrouillé — la tuile se retrouve en double : c'est visible
    /// et ça se rattrape d'un clic droit. Dans l'ordre inverse, elle serait perdue, et personne ne
    /// saurait quoi recréer.
    /// </para>
    /// <para>
    /// Rien d'asynchrone ici, contrairement à l'ajout : l'entrée voyage entière, avec son
    /// <c>IconProfilePath</c>, donc aucune icône n'est à retélécharger.
    /// </para>
    /// </remarks>
    public static ActionResult Transfer(int page, int row, int col, TileFiles from, TileFiles to, int? childSlot = null)
    {
        if (from.EntriesPath == to.EntriesPath)
            return ActionResult.Fail("La grille de départ et celle d'arrivée sont la même.");

        lock (ConfigLock.Gate)
        {
            var source = ShortcutService.Load(from.EntriesPath);
            var dest = ShortcutService.Load(to.EntriesPath);
            var destPages = PageConfigService.Load(to.PagesPath);

            var result = TransferCore(source, dest, destPages, page, row, col, childSlot);
            if (!result.Ok) return result;

            ShortcutService.Save(dest, to.EntriesPath);
            ShortcutService.Save(source, from.EntriesPath);
            return result;
        }
    }

    /// <summary>Utilisée par l'UI uniquement (⧉ Dupliquer) — non exposée côté MCP.</summary>
    public static ActionResult Duplicate(int page, int row, int col, TileFiles? files = null, int? slot = null)
    {
        files ??= TileStore.FilesFor(TileTarget.Shortcuts);
        lock (ConfigLock.Gate)
        {
            var all = ShortcutService.Load(files.EntriesPath);
            var result = DuplicateCore(all, page, row, col, slot);
            if (result.Ok) ShortcutService.Save(all, files.EntriesPath);
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
                               iconProfilePath = s.IconProfilePath, layout = s.Layout.ToString(),
                               groupColor = s.IsGroup ? s.GroupColor : null,
                               // children[i] est la sous-case i ; freeSlots évite au modèle de compter les null.
                               freeSlots = s.IsGroup
                                   ? Enumerable.Range(0, s.Children?.Count ?? 0).Where(i => s.Children![i] is null).ToList()
                                   : null,
                               children = s.Children })
            .ToList();

        return ActionResult.Success(new { gridRows = GridRows, gridCols = GridCols, pages, shortcuts });
    }

    public static ActionResult AddCore(List<ShortcutEntry> all, List<PageConfig> configs, List<ShortcutAddItem> items) =>
        AddCore(all, configs, items, []);

    /// <param name="staged">Reçoit les entrées créées, dans l'ordre des items — une tuile posée
    /// dans une sous-case n'est pas ajoutée à <paramref name="all"/>, qui ne la montre donc pas.</param>
    private static ActionResult AddCore(List<ShortcutEntry> all, List<PageConfig> configs,
                                        List<ShortcutAddItem> items, List<ShortcutEntry> staged)
    {
        if (items is not { Count: > 0 })
            return ActionResult.Fail("Aucun raccourci à ajouter.");

        // Bornée sur l'état initial : un lot ciblant une page qui n'existe pas encore
        // échoue en entier, même si un item antérieur du même lot créerait cette page.
        int lastShown = LastShown(all, configs);

        var errors = new List<string>();
        // occupation simulée : existant + items déjà placés dans ce lot
        var occupied = all.Select(s => (s.Page, s.Row, s.Col)).ToHashSet();
        var occupiedSlots = new HashSet<TileAddress>();

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

            if (it.Slot is { } slot)
            {
                if (it.Row is not { } gr || it.Col is not { } gc)
                { errors.Add($"{id} : slot demande la position (row, col) du groupe."); continue; }
                var address = new TileAddress(page, gr, gc, slot);
                if (SlotError(all, address) is { } slotError) { errors.Add($"{id} : {slotError}"); continue; }
                if (!occupiedSlots.Add(address))
                { errors.Add($"{id} : sous-case {slot} déjà visée par un autre item du lot."); continue; }
                staged.Add(NewEntry(it, page, gr, gc, slot));
                continue;
            }

            (int row, int col)? dest = null;
            if (it.Row is { } r0 && it.Col is { } c0)
            {
                if (r0 < 0 || r0 >= GridRows || c0 < 0 || c0 >= GridCols)
                { errors.Add($"{id} : position hors bornes (lignes 0-{GridRows - 1}, colonnes 0-{GridCols - 1})."); continue; }
                if (occupied.Contains((page, r0, c0)))
                {
                    var occ = all.FirstOrDefault(s => s.Page == page && s.Row == r0 && s.Col == c0)?.Name
                              ?? staged.First(s => s.Page == page && s.Row == r0 && s.Col == c0 && s.Slot is null).Name;
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

            var entry = NewEntry(it, page, dest.Value.row, dest.Value.col, slot: null);
            staged.Add(entry);
            occupied.Add((page, entry.Row, entry.Col));
        }

        if (errors.Count > 0)
        {
            staged.Clear();
            return ActionResult.Fail("Lot refusé (tout ou rien) :\n- " + string.Join("\n- ", errors));
        }

        foreach (var entry in staged)
            TileGroupService.Put(all, TileAddress.Of(entry), entry);
        return ActionResult.Success(new
        {
            added = staged.Select(s => new { s.Name, page = s.Page, row = s.Row, col = s.Col, slot = s.Slot }).ToList()
        });
    }

    private static ShortcutEntry NewEntry(ShortcutAddItem it, int page, int row, int col, int? slot) => new()
    {
        Page = page, Row = row, Col = col, Slot = slot,
        Name = it.Name, Type = it.Type, Command = it.Command,
        IconPath = it.IconPath ?? "",
        Terminal = it.Terminal, ProcessSwitch = it.ProcessSwitch,
    };

    /// <summary>Pourquoi une sous-case ne peut pas recevoir de tuile, ou null si elle est libre.</summary>
    private static string? SlotError(List<ShortcutEntry> all, TileAddress address)
    {
        var root = TileGroupService.Get(all, address.Root);
        if (root?.IsGroup != true)
            return $"aucune tuile groupée en page {address.Page}, ligne {address.Row}, colonne {address.Col}. " +
                   "Crée-la d'abord avec dockpad_group_set.";
        int capacity = TileGroupService.Capacity(root.Layout);
        if (address.Slot is not { } slot || slot < 0 || slot >= capacity)
            return $"sous-case {address.Slot} hors du groupe « {root.Name} » (0 à {capacity - 1}).";
        if (root.Children![slot] is { } occupant)
            return $"sous-case {slot} du groupe « {root.Name} » occupée par « {occupant.Name} ». " +
                   $"Sous-cases libres : {FreeSlotsText(root)}";
        return null;
    }

    private static string FreeSlotsText(ShortcutEntry group)
    {
        var free = Enumerable.Range(0, group.Children!.Count).Where(i => group.Children[i] is null).ToList();
        return free.Count == 0 ? "aucune" : string.Join(", ", free);
    }

    public static ActionResult UpdateCore(List<ShortcutEntry> all, int page, int row, int col, ShortcutUpdate changes, int? slot = null)
    {
        var s = TileGroupService.Get(all, new(page, row, col, slot));
        if (s is null) return ActionResult.Fail($"Aucune tuile en page {page}, ligne {row}, colonne {col}.");

        if (s.IsGroup) return ActionResult.Fail(Loc.T("Group_EditChild"));

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
                                        int toPage, int? toRow, int? toCol, int? slot = null, int? toSlot = null)
    {
        var s = TileGroupService.Get(all, new(page, row, col, slot));
        if (s is null) return ActionResult.Fail($"Aucune tuile en page {page}, ligne {row}, colonne {col}.");
        if (toSlot is not null) return MoveIntoSlot(all, s, new(toPage, toRow ?? -1, toCol ?? -1, toSlot), toRow.HasValue && toCol.HasValue);
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

        TileGroupService.Put(all, new(page, row, col, slot), null);
        TileGroupService.Put(all, new(toPage, dest.row, dest.col), s);
        TileGroupService.Normalize(all);
        return ActionResult.Success(new { s.Name, page = s.Page, row = s.Row, col = s.Col });
    }

    /// <summary>
    /// Vers une sous-case LIBRE : l'interface échange au glisser-déposer, MCP refuse une case
    /// occupée — même règle que partout ailleurs côté serveur, un modèle ne voit pas l'échange.
    /// </summary>
    private static ActionResult MoveIntoSlot(List<ShortcutEntry> all, ShortcutEntry s, TileAddress to, bool hasPosition)
    {
        if (!hasPosition) return ActionResult.Fail("toSlot demande toRow et toCol, la position du groupe d'arrivée.");
        if (s.IsGroup) return ActionResult.Fail("Un groupe ne se range pas dans la sous-case d'un autre groupe.");
        if (SlotError(all, to) is { } error) return ActionResult.Fail(char.ToUpperInvariant(error[0]) + error[1..]);

        var result = TileGroupService.MoveCore(all, TileAddress.Of(s), to);
        return result.Ok
            ? ActionResult.Success(new { s.Name, page = s.Page, row = s.Row, col = s.Col, slot = s.Slot })
            : result;
    }

    /// <summary>
    /// Crée, transforme ou habille une tuile groupée, tout ou rien. <paramref name="layout"/> :
    /// une case vide devient un groupe vide, une tuile simple en devient la sous-case 0, un groupe
    /// change de disposition (refusé s'il perdrait des tuiles), Simple défait un groupe d'au plus une
    /// tuile. <paramref name="name"/> et <paramref name="color"/> s'appliquent au groupe résultant.
    /// </summary>
    public static ActionResult GroupSetCore(List<ShortcutEntry> all, List<PageConfig> configs, int page, int row, int col,
                                            TileLayout? layout, string? name, string? color)
    {
        if (layout is null && name is null && color is null)
            return ActionResult.Fail("Rien à modifier : fournir layout, name ou color.");
        if (page < 0 || page > LastShown(all, configs))
            return ActionResult.Fail($"Page {page} inexistante (pages 0 à {LastShown(all, configs)}).");
        if (row < 0 || row >= GridRows || col < 0 || col >= GridCols)
            return ActionResult.Fail($"Position hors bornes (lignes 0-{GridRows - 1}, colonnes 0-{GridCols - 1}).");
        if (color is not null && !TileGroupService.IsValidColor(color))
            return ActionResult.Fail("Couleur invalide : format #RRGGBB attendu.");
        if (name is not null && string.IsNullOrWhiteSpace(name))
            return ActionResult.Fail("Le nom ne peut pas être vide.");

        // Tout refus est décidé avant la première mutation : ChangeLayoutCore refuse sans rien toucher.
        var address = new TileAddress(page, row, col);
        var resulting = layout ?? TileGroupService.Get(all, address)?.Layout ?? TileLayout.Simple;
        if ((name is not null || color is not null) && resulting == TileLayout.Simple)
            return ActionResult.Fail($"Aucune tuile groupée en page {page}, ligne {row}, colonne {col} : " +
                                     "fournir layout (Quad ou TwoPlusFour) pour en créer une.");

        if (layout is { } l)
        {
            var changed = TileGroupService.ChangeLayoutCore(all, address, l);
            if (!changed.Ok) return changed;
        }
        var group = TileGroupService.Get(all, address);
        if (group?.IsGroup == true)
        {
            if (name is not null) group.Name = name.Trim();
            if (color is not null) group.GroupColor = color.ToUpperInvariant();
        }

        return ActionResult.Success(group is null ? null : new
        {
            name = group.Name, page, row, col, layout = group.Layout.ToString(),
            groupColor = group.IsGroup ? group.GroupColor : null,
            freeSlots = group.IsGroup ? Enumerable.Range(0, group.Children!.Count).Where(i => group.Children[i] is null).ToList() : null,
        });
    }

    /// <summary>Une case libre dans une grille, et s'il a fallu inventer la page pour l'avoir.</summary>
    public readonly record struct GridSlot(int Page, int Row, int Col, bool NeedsNewPage);

    /// <summary>
    /// Première case libre d'une grille entière, pages balayées dans l'ordre ; toutes pleines, la
    /// case (0, 0) d'une page qui n'existe pas encore.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Règle distincte de celle d'<see cref="AddCore"/></b>, qui ne regarde qu'une page et
    /// refuse si elle est pleine. Celle-là ne peut pas refuser : ses appelants sont des gestes
    /// d'un seul clic — l'étoile du popup, le déplacement d'une grille à l'autre — devant lesquels
    /// personne n'est là pour lire un message d'erreur.
    /// </para>
    /// <para>
    /// Un balayage des cases, et non un comptage des entrées : deux tuiles peuvent partager une
    /// position dans un fichier édité à la main, et une position peut être hors bornes. Compter
    /// déclarerait alors une page pleine qui ne l'est pas.
    /// </para>
    /// </remarks>
    public static GridSlot FirstFreeSlot(List<ShortcutEntry> entries, List<PageConfig> pages)
    {
        int maxUsed = entries.Count > 0 ? entries.Max(s => s.Page) : -1;
        int maxConfig = pages.Count > 0 ? pages.Max(p => p.Index) : -1;
        int lastShown = Math.Max(Math.Max(maxUsed, maxConfig), 0);

        for (int page = 0; page <= lastShown; page++)
        {
            var occupied = entries.Where(s => s.Page == page).Select(s => (s.Row, s.Col)).ToHashSet();
            for (int row = 0; row < GridRows; row++)
                for (int col = 0; col < GridCols; col++)
                    if (!occupied.Contains((row, col)))
                        return new GridSlot(page, row, col, NeedsNewPage: false);
        }

        return new GridSlot(lastShown + 1, 0, 0, NeedsNewPage: true);
    }

    /// <summary>
    /// Déplace une tuile d'une grille vers l'autre : elle quitte <paramref name="source"/> et
    /// atterrit à la première case libre de <paramref name="dest"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>C'est l'entrée elle-même qui voyage</b>, pas une copie reconstruite. Le store d'icônes
    /// est commun aux deux grilles et <c>IconProfilePath</c> y est relatif : l'icône suit donc sans
    /// qu'on retélécharge quoi que ce soit, et les configurations de type — terminal, processus —
    /// arrivent intactes. Passer par <see cref="AddCore"/> aurait reconstruit une entrée nue.
    /// </para>
    /// <para>
    /// <b>Aucune page n'est déclarée</b> quand la destination est pleine : une tuile posée sur la
    /// page suivante suffit à la faire exister, la pagination lisant le maximum des pages
    /// utilisées. Un <c>PageConfig</c> ne sert qu'à porter une icône de bouton.
    /// </para>
    /// </remarks>
    public static ActionResult TransferCore(List<ShortcutEntry> source, List<ShortcutEntry> dest,
                                            List<PageConfig> destPages, int page, int row, int col, int? childSlot = null)
    {
        var entry = TileGroupService.Get(source, new(page, row, col, childSlot));
        if (entry is null) return ActionResult.Fail($"Aucune tuile en page {page}, ligne {row}, colonne {col}.");

        var slot = FirstFreeSlot(dest, destPages);

        TileGroupService.Put(source, new(page, row, col, childSlot), null);
        entry.Page = slot.Page;
        entry.Row = slot.Row;
        entry.Col = slot.Col;
        dest.Add(entry);
        TileGroupService.Normalize(source);
        TileGroupService.Normalize(dest);

        return ActionResult.Success(new
        {
            moved = entry.Name, page = slot.Page, row = slot.Row, col = slot.Col,
        });
    }

    public static ActionResult DeleteCore(List<ShortcutEntry> all, int page, int row, int col, int? slot = null)
    {
        var s = TileGroupService.Get(all, new(page, row, col, slot));
        if (s is null) return ActionResult.Fail($"Aucune tuile en page {page}, ligne {row}, colonne {col}.");
        TileGroupService.Put(all, new(page, row, col, slot), null);
        return ActionResult.Success(new { deleted = s.Name });
    }

    public static ActionResult DuplicateCore(List<ShortcutEntry> all, int page, int row, int col, int? slot = null)
    {
        var s = TileGroupService.Get(all, new(page, row, col, slot));
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

        var copy = TileGroupService.Clone(s);
        TileGroupService.Put(all, new(page, nearest.Value.row, nearest.Value.col), copy);
        TileGroupService.Normalize(all);
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

    /// <summary>
    /// Icône fournie → copie profil ; absente → icône de l'exe associé
    /// (RunCommand/SwitchToProcess/OpenTerminal), ou l'icône dossier par défaut (OpenFolder) —
    /// celle que pose déjà le dépôt d'un dossier depuis l'Explorateur.
    /// </summary>
    private static void ApplyIcon(ShortcutEntry s)
    {
        if (!string.IsNullOrEmpty(s.IconPath))
        {
            s.IconProfilePath ??= IconStoreService.CopyToProfile(s.IconPath);
            return;
        }
        if (s.Type == ShortcutType.OpenFolder)
        {
            s.IconProfilePath ??= IconStoreService.StoreDefaultFolderIcon();
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
