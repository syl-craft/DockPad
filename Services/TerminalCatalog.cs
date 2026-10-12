using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// La liste des terminaux de <c>terminals.json</c> : reprise de <c>settings.json</c>, fusion de la
/// détection, ordre, et terminaux proposés au lancement. Sans fichier ni WPF.
/// </summary>
public static class TerminalCatalog
{
    /// <summary>
    /// Ce que la reprise de <c>settings.json</c> produit : la liste, le terminal par défaut (son
    /// identifiant a pu changer) et les arguments d'un terminal absent, gardés pour son retour.
    /// </summary>
    public sealed record Migration(List<TerminalEntry> Terminals, string DefaultTerminal,
                                   Dictionary<string, TerminalSettings> PendingTemplates);

    /// <summary>
    /// Première liste : la détection, avec les arguments modifiés dans <c>settings.json</c>, puis les
    /// terminaux qui y étaient déclarés et l'ancien programme unique (<c>customTerminal</c>).
    /// </summary>
    /// <remarks>
    /// Une déclaration au programme d'un terminal détecté lui donne son nom et ses arguments au lieu
    /// de le doubler. Un identifiant est gardé tant qu'il est libre ; s'il change, le terminal par
    /// défaut qui le visait le suit.
    /// </remarks>
    public static Migration Migrate(IReadOnlyList<TerminalInfo> detected, AppSettings legacy)
    {
        var pending = new Dictionary<string, TerminalSettings>(legacy.TerminalTemplates);
        var entries = new List<TerminalEntry>();
        Merge(entries, detected, pending);

        var definitions = legacy.Terminals.ToList();
        if (legacy.CustomTerminal.ExePath.Trim() != "")
        {
            definitions.Add(new TerminalDefinition
            {
                Id          = "custom",
                Name        = System.IO.Path.GetFileNameWithoutExtension(legacy.CustomTerminal.ExePath.Trim().Trim('"')),
                ExePath     = legacy.CustomTerminal.ExePath,
                FolderArgs  = legacy.CustomTerminal.FolderArgs,
                CommandArgs = legacy.CustomTerminal.CommandArgs,
            });
        }

        string defaultTerminal = legacy.DefaultTerminal;
        foreach (var definition in definitions)
        {
            string exePath = definition.ExePath.Trim().Trim('"');
            if (exePath == "") continue;

            string declaredId = definition.Id.Trim();
            string name = definition.Name.Trim() == "" ? declaredId : definition.Name.Trim();

            var samePath = entries.Where(e => SamePath(e.ExePath, exePath)).FirstOrDefault();
            if (samePath != null)
            {
                if (name != "") samePath.Name = name;
                if (definition.FolderArgs.Trim() != "") samePath.FolderArgs = definition.FolderArgs.Trim();
                if (definition.CommandArgs.Trim() != "") samePath.CommandArgs = definition.CommandArgs.Trim();
                if (defaultTerminal == declaredId) defaultTerminal = samePath.Id;
                continue;
            }

            var taken = entries.Select(e => e.Id).ToList();
            bool idIsFree = declaredId != ""
                && !taken.Where(id => id.Equals(declaredId, StringComparison.OrdinalIgnoreCase)).Any()
                && !TerminalDetectionService.KnownIds.Where(id => id.Equals(declaredId, StringComparison.OrdinalIgnoreCase)).Any();
            string newId = idIsFree ? declaredId : NewId(name == "" ? "terminal" : name, taken);

            entries.Add(new TerminalEntry
            {
                Id          = newId,
                Name        = name == "" ? newId : name,
                ExePath     = exePath,
                FolderArgs  = definition.FolderArgs.Trim(),
                CommandArgs = definition.CommandArgs.Trim(),
                Order       = NextOrder(entries),
            });
            if (defaultTerminal == declaredId) defaultTerminal = newId;
        }
        return new Migration(entries, defaultTerminal, pending);
    }

    /// <summary>
    /// Ajoute les terminaux détectés absents de la liste, et rend leur nombre. Un terminal déjà
    /// présent garde ses arguments, son chemin, son masquage et son ordre ; son nom suit la
    /// détection tant qu'il n'a pas été changé. Un terminal disparu reste dans la liste. Un terminal
    /// ajouté reprend les arguments que <paramref name="pendingTemplates"/> gardait pour lui, et les en retire.
    /// </summary>
    public static int Merge(List<TerminalEntry> entries, IReadOnlyList<TerminalInfo> detected,
                            Dictionary<string, TerminalSettings> pendingTemplates)
    {
        int added = 0;
        foreach (var found in detected)
        {
            var existing = entries.Where(e => e.Id == found.Id).FirstOrDefault();
            if (existing != null)
            {
                if (existing.Name == existing.DetectedName) existing.Name = found.DisplayName;
                existing.DetectedName = found.DisplayName;
                continue;
            }

            var edited = pendingTemplates.GetValueOrDefault(found.Id);
            pendingTemplates.Remove(found.Id);
            entries.Add(new TerminalEntry
            {
                Id           = found.Id,
                Name         = found.DisplayName,
                DetectedName = found.DisplayName,
                ExePath      = found.ExePath,
                FolderArgs   = edited?.FolderArgs ?? found.FolderArgs,
                CommandArgs  = edited?.CommandArgs ?? found.CommandArgs,
                Order        = NextOrder(entries),
            });
            added++;
        }
        return added;
    }

    /// <summary>
    /// Les terminaux proposés : visibles et dont le programme existe, dans l'ordre de la liste.
    /// </summary>
    public static List<TerminalInfo> Available(IReadOnlyList<TerminalEntry> entries, Func<string, bool> exists) =>
        entries.Where(e => !e.Hidden && exists(e.ExePath))
               .OrderBy(e => e.Order)
               .Select(ToInfo)
               .ToList();

    public static TerminalInfo ToInfo(TerminalEntry entry) => new()
    {
        Id          = entry.Id,
        DisplayName = entry.Name.Trim() == "" ? entry.Id : entry.Name.Trim(),
        ExePath     = entry.ExePath.Trim().Trim('"'),
        FolderArgs  = entry.FolderArgs.Trim(),
        CommandArgs = entry.CommandArgs.Trim(),
    };

    /// <summary>
    /// Échange le terminal avec son voisin dans l'ordre affiché ; au bord de la liste, rien ne bouge.
    /// </summary>
    public static void Move(List<TerminalEntry> entries, TerminalEntry entry, int delta)
    {
        var ordered = entries.OrderBy(e => e.Order).ToList();
        int index = ordered.IndexOf(entry);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= ordered.Count) return;

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        for (int i = 0; i < ordered.Count; i++) ordered[i].Order = i;
    }

    public static void Reindex(List<TerminalEntry> entries)
    {
        var ordered = entries.OrderBy(e => e.Order).ToList();
        for (int i = 0; i < ordered.Count; i++) ordered[i].Order = i;
    }

    /// <summary>
    /// Identifiant d'un terminal ajouté, tiré de son nom : minuscules, lettres et chiffres, suffixé
    /// s'il est déjà pris ou réservé à un terminal que DockPad sait détecter.
    /// </summary>
    public static string NewId(string name, IEnumerable<string> takenIds)
    {
        string slug = string.Concat(name.Trim().ToLowerInvariant()
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_')
            .Select(c => c is ' ' or '_' ? '-' : c)).Trim('-');
        if (slug == "") slug = "terminal";

        var taken = new HashSet<string>(takenIds, StringComparer.OrdinalIgnoreCase);
        taken.UnionWith(TerminalDetectionService.KnownIds);

        string id = slug;
        for (int n = 2; taken.Contains(id); n++) id = $"{slug}-{n}";
        return id;
    }

    private static int NextOrder(List<TerminalEntry> entries) =>
        entries.Count == 0 ? 0 : entries.Max(e => e.Order) + 1;

    private static bool SamePath(string a, string b) => string.Equals(
        Environment.ExpandEnvironmentVariables(a.Trim().Trim('"')),
        Environment.ExpandEnvironmentVariables(b.Trim().Trim('"')),
        StringComparison.OrdinalIgnoreCase);
}
