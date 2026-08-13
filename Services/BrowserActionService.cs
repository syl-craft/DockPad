using DockPad.Models;

namespace DockPad.Services;

/// <summary>Actions navigateurs & règles de domaine, partagées UI ↔ MCP.</summary>
public static class BrowserActionService
{
    public static ActionResult ListBrowsers()
    { lock (ConfigLock.Gate) return ListBrowsersCore(BrowserConfigService.Load()); }

    public static ActionResult UpdateBrowser(string id, BrowserUpdate changes)
    {
        lock (ConfigLock.Gate)
        {
            var cfg = BrowserConfigService.Load();
            var result = UpdateBrowserCore(cfg, id, changes);
            if (result.Ok) BrowserConfigService.Save(cfg);
            return result;
        }
    }

    public static ActionResult ListRules()
    { lock (ConfigLock.Gate) return ListRulesCore(BrowserConfigService.Load()); }

    public static ActionResult AddRule(string host, string browserId)
    {
        lock (ConfigLock.Gate)
        {
            var cfg = BrowserConfigService.Load();
            var result = AddRuleCore(cfg, host, browserId);
            if (result.Ok) BrowserConfigService.Save(cfg);
            return result;
        }
    }

    public static ActionResult DeleteRule(string host)
    {
        lock (ConfigLock.Gate)
        {
            var cfg = BrowserConfigService.Load();
            var result = DeleteRuleCore(cfg, host);
            if (result.Ok) BrowserConfigService.Save(cfg);
            return result;
        }
    }

    // ───────────── Cœurs purs ─────────────

    public static ActionResult ListBrowsersCore(BrowsersConfig cfg) =>
        ActionResult.Success(new
        {
            // Ordre d'affichage : chaque navigateur suivi de ses profils.
            browsers = BrowserRowLayout.Grouped(cfg).Select(r => r.Entry)
                .Select(b => new
                {
                    b.Id, b.Name, b.ExePath, b.Arguments, b.Hidden, b.Order,
                    b.ParentId, b.ProfileDirectory,
                }).ToList(),
            autoOpenSeconds = cfg.AutoOpenSeconds,
        });

    public static ActionResult UpdateBrowserCore(BrowsersConfig cfg, string id, BrowserUpdate changes)
    {
        var b = cfg.Browsers.FirstOrDefault(b => b.Id == id);
        if (b is null)
            return ActionResult.Fail($"Navigateur « {id} » introuvable. Utilise dockpad_browser_list pour les ids.");

        if (changes.Name is { } n)
        {
            if (string.IsNullOrWhiteSpace(n)) return ActionResult.Fail("Le nom ne peut pas être vide.");
            b.Name = n;
        }
        if (changes.ExePath is { } e)
        {
            if (string.IsNullOrWhiteSpace(e)) return ActionResult.Fail("Le chemin exe ne peut pas être vide.");
            b.ExePath = e;
        }
        if (changes.Arguments is { } a) b.Arguments = a;
        if (changes.Hidden is { } h) b.Hidden = h;
        if (changes.Order is { } o)
        {
            // Repositionnement par insertion dans la fratrie : parmi les navigateurs pour un
            // navigateur (ses profils le suivent), parmi les profils du même navigateur sinon.
            var siblings = b.ParentId is null
                ? cfg.Browsers.Where(x => x.ParentId is null).OrderBy(x => x.Order).ToList()
                : BrowserRowLayout.Children(cfg, b.ParentId);
            siblings.Remove(b);
            siblings.Insert(Math.Clamp(o, 0, siblings.Count), b);
            for (int i = 0; i < siblings.Count; i++) siblings[i].Order = i;

            BrowserRowLayout.Reindex(cfg);
        }

        return ActionResult.Success(new { b.Id, b.Name, b.Order, b.Hidden });
    }

    public static ActionResult ListRulesCore(BrowsersConfig cfg) =>
        ActionResult.Success(new
        {
            rules = cfg.Rules.Select(r => new
            {
                host = r.Host,
                browserId = r.BrowserId,
                browserName = cfg.Browsers.FirstOrDefault(b => b.Id == r.BrowserId)?.Name ?? "?",
            }).ToList(),
        });

    public static ActionResult AddRuleCore(BrowsersConfig cfg, string host, string browserId)
    {
        host = host.Trim().ToLowerInvariant();
        if (host.Length == 0) return ActionResult.Fail("host requis (ex. github.com, localhost:44351).");

        var browser = cfg.Browsers.FirstOrDefault(b => b.Id == browserId);
        if (browser is null)
            return ActionResult.Fail($"Navigateur « {browserId} » introuvable. Utilise dockpad_browser_list pour les ids.");

        var existing = cfg.Rules.FirstOrDefault(r => r.Host.Equals(host, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var owner = cfg.Browsers.FirstOrDefault(b => b.Id == existing.BrowserId)?.Name ?? existing.BrowserId;
            return ActionResult.Fail($"Une règle existe déjà pour {host} (→ {owner}). " +
                                     "Supprime-la d'abord avec dockpad_rule_delete.");
        }

        cfg.Rules.Add(new BrowserRule { Host = host, BrowserId = browserId });
        return ActionResult.Success(new { host, browser = browser.Name });
    }

    public static ActionResult DeleteRuleCore(BrowsersConfig cfg, string host)
    {
        host = host.Trim().ToLowerInvariant();
        var rule = cfg.Rules.FirstOrDefault(r => r.Host.Equals(host, StringComparison.OrdinalIgnoreCase));
        if (rule is null) return ActionResult.Fail($"Aucune règle pour {host}.");
        cfg.Rules.Remove(rule);
        return ActionResult.Success(new { deleted = host });
    }
}
