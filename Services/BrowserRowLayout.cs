using DockPad.Models;

namespace DockPad.Services;

/// <summary>Une ligne de liste : un navigateur, ou un de ses profils.</summary>
public sealed record BrowserRow(BrowserEntry Entry, bool IsChild, bool IsHeader);

/// <summary>Ordre d'affichage des navigateurs et de leurs profils, partagé popup ↔ configuration.</summary>
public static class BrowserRowLayout
{
    /// <summary>
    /// Tout le contenu de la config, chaque navigateur suivi de ses profils. Un profil dont
    /// le parent n'existe plus (json édité à la main) est traité comme un navigateur normal.
    /// </summary>
    public static List<BrowserRow> Grouped(BrowsersConfig cfg)
    {
        var rows = new List<BrowserRow>();

        foreach (var parent in cfg.Browsers.Where(b => b.ParentId is null).OrderBy(b => b.Order))
        {
            rows.Add(new BrowserRow(parent, IsChild: false, IsHeader: false));
            foreach (var child in Children(cfg, parent.Id))
                rows.Add(new BrowserRow(child, IsChild: true, IsHeader: false));
        }

        foreach (var orphan in cfg.Browsers
                     .Where(b => b.ParentId is not null && cfg.Browsers.All(p => p.Id != b.ParentId))
                     .OrderBy(b => b.Order))
            rows.Add(new BrowserRow(orphan, IsChild: false, IsHeader: false));

        return rows;
    }

    /// <summary>
    /// Lignes de la popup : les entrées masquées sont exclues, mais un navigateur masqué
    /// dont il reste des profils visibles devient un en-tête non sélectionnable, pour que
    /// ses profils ne flottent pas sans titre.
    /// </summary>
    public static List<BrowserRow> ForPicker(BrowsersConfig cfg)
    {
        var rows = new List<BrowserRow>();

        foreach (var row in Grouped(cfg))
        {
            if (!row.Entry.Hidden) { rows.Add(row); continue; }
            if (row.IsChild) continue;

            if (Children(cfg, row.Entry.Id).Any(c => !c.Hidden))
                rows.Add(row with { IsHeader = true });
        }

        return rows;
    }

    /// <summary>Les profils d'un navigateur, dans leur ordre d'affichage.</summary>
    public static List<BrowserEntry> Children(BrowsersConfig cfg, string parentId) =>
        cfg.Browsers.Where(b => b.ParentId == parentId).OrderBy(b => b.Order).ToList();

    /// <summary>
    /// Déplace une entrée d'un cran : un navigateur parmi les navigateurs (ses profils
    /// suivent), un profil parmi les profils de son navigateur. Aucun effet aux extrémités.
    /// </summary>
    public static void Move(BrowsersConfig cfg, BrowserEntry entry, int delta)
    {
        var siblings = entry.ParentId is null
            ? cfg.Browsers.Where(b => b.ParentId is null).OrderBy(b => b.Order).ToList()
            : Children(cfg, entry.ParentId);

        int i = siblings.IndexOf(entry), j = i + delta;
        if (i < 0 || j < 0 || j >= siblings.Count) return;

        (siblings[i].Order, siblings[j].Order) = (siblings[j].Order, siblings[i].Order);
        Reindex(cfg);
    }

    /// <summary>Libellé sans ambiguïté hors contexte de liste : « Chrome › Boulot » pour un profil.</summary>
    public static string DisplayName(BrowsersConfig cfg, BrowserEntry entry)
    {
        var parent = entry.ParentId is null ? null : cfg.Browsers.FirstOrDefault(b => b.Id == entry.ParentId);
        return parent is null ? entry.Name : $"{parent.Name} › {entry.Name}";
    }

    /// <summary>Réattribue les ordres 0..n-1 dans l'ordre d'affichage (groupes contigus).</summary>
    public static void Reindex(BrowsersConfig cfg)
    {
        var rows = Grouped(cfg);
        for (int i = 0; i < rows.Count; i++) rows[i].Entry.Order = i;
    }
}
