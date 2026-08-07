using DockPad.Models;

namespace DockPad.Services;

/// <summary>Actions sur les pages, partagées UI ↔ MCP (mêmes règles que la pagination de QuickAccessWindow).</summary>
public static class PageActionService
{
    public static ActionResult Add(string? iconPath = null)
    {
        lock (ConfigLock.Gate)
        {
            var all = ShortcutService.Load();
            var configs = PageConfigService.Load();
            string? profile = string.IsNullOrEmpty(iconPath) ? null : IconStoreService.CopyToProfile(iconPath);
            var result = AddCore(all, configs, iconPath ?? "", profile);
            if (result.Ok) PageConfigService.Save(configs);
            return result;
        }
    }

    public static ActionResult Update(int index, bool iconProvided, string? iconPath, int? newIndex)
    {
        lock (ConfigLock.Gate)
        {
            var all = ShortcutService.Load();
            var configs = PageConfigService.Load();
            string? profile = iconProvided && !string.IsNullOrEmpty(iconPath)
                ? IconStoreService.CopyToProfile(iconPath) : null;
            var result = UpdateCore(all, configs, index, iconProvided, iconPath, profile, newIndex);
            if (result.Ok) { ShortcutService.Save(all); PageConfigService.Save(configs); }
            return result;
        }
    }

    public static ActionResult Delete(int index)
    {
        lock (ConfigLock.Gate)
        {
            var all = ShortcutService.Load();
            var configs = PageConfigService.Load();
            var result = DeleteCore(all, configs, index);
            if (result.Ok) { ShortcutService.Save(all); PageConfigService.Save(configs); }
            return result;
        }
    }

    // ───────────── Cœurs purs ─────────────

    private static int LastShown(List<ShortcutEntry> all, List<PageConfig> configs)
    {
        int maxUsed   = all.Count     > 0 ? all.Max(s => s.Page)      : -1;
        int maxConfig = configs.Count > 0 ? configs.Max(p => p.Index) : -1;
        return Math.Max(Math.Max(maxUsed, maxConfig), 0);
    }

    public static ActionResult AddCore(List<ShortcutEntry> all, List<PageConfig> configs,
                                       string iconPath, string? iconProfilePath)
    {
        int newIndex = LastShown(all, configs) + 1;
        configs.Add(new PageConfig { Index = newIndex, IconPath = iconPath, IconProfilePath = iconProfilePath });
        return ActionResult.Success(new { index = newIndex });
    }

    public static ActionResult UpdateCore(List<ShortcutEntry> all, List<PageConfig> configs, int index,
                                          bool iconProvided, string? iconPath, string? iconProfilePath,
                                          int? newIndex)
    {
        int last = LastShown(all, configs);
        if (index < 0 || index > last)
            return ActionResult.Fail($"Page {index} inexistante (pages 0 à {last}).");
        if (!iconProvided && newIndex is null)
            return ActionResult.Fail("Rien à modifier : fournir iconPath et/ou newIndex.");

        if (iconProvided)
        {
            var cfg = configs.FirstOrDefault(c => c.Index == index);
            if (iconPath is null) // null explicite = retirer l'icône
            {
                if (cfg != null) { cfg.IconPath = ""; cfg.IconProfilePath = null; }
            }
            else
            {
                if (cfg is null) { cfg = new PageConfig { Index = index }; configs.Add(cfg); }
                cfg.IconPath = iconPath;
                cfg.IconProfilePath = iconProfilePath;
            }
        }

        if (newIndex is { } to)
        {
            if (to < 0 || to > last)
                return ActionResult.Fail($"newIndex {to} invalide (pages 0 à {last}).");
            if (to != index)
            {
                foreach (var s in all)     s.Page  = Remap(s.Page,  index, to);
                foreach (var c in configs) c.Index = Remap(c.Index, index, to);
            }
        }

        return ActionResult.Success(new { index = newIndex ?? index });
    }

    public static ActionResult DeleteCore(List<ShortcutEntry> all, List<PageConfig> configs, int index)
    {
        int last = LastShown(all, configs);
        if (index < 0 || index > last)
            return ActionResult.Fail($"Page {index} inexistante (pages 0 à {last}).");

        int removed = all.RemoveAll(s => s.Page == index);
        foreach (var s in all.Where(s => s.Page > index)) s.Page--;

        configs.RemoveAll(c => c.Index == index);
        foreach (var c in configs.Where(c => c.Index > index)) c.Index--;

        return ActionResult.Success(new { deletedTiles = removed });
    }

    /// <summary>Déplacement par insertion : from → to, pages intermédiaires décalées.</summary>
    private static int Remap(int p, int from, int to) =>
        p == from                          ? to
        : from < to && p > from && p <= to ? p - 1
        : from > to && p >= to && p < from ? p + 1
        : p;
}
