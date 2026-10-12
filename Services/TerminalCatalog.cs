using DockPad.Models;

namespace DockPad.Services;

/// <summary>
/// La liste des terminaux de <c>terminals.json</c> : reprise de <c>settings.json</c>, fusion de la
/// détection, ordre, et terminaux proposés au lancement. Sans fichier ni WPF.
/// </summary>
public static class TerminalCatalog
{
    /// <summary>
    /// Première liste : la détection, avec les arguments modifiés dans <c>settings.json</c>, puis
    /// les terminaux qui y étaient déclarés, sauf ceux dont la détection a déjà le programme.
    /// </summary>
    public static List<TerminalEntry> Initial(IReadOnlyList<TerminalInfo> detected,
                                              IReadOnlyDictionary<string, TerminalSettings> legacyTemplates,
                                              IReadOnlyList<TerminalDefinition> legacyDefinitions)
    {
        var entries = new List<TerminalEntry>();
        Merge(entries, detected);

        foreach (var entry in entries)
        {
            if (legacyTemplates.GetValueOrDefault(entry.Id) is not { } edited) continue;
            entry.FolderArgs  = edited.FolderArgs;
            entry.CommandArgs = edited.CommandArgs;
        }

        foreach (var definition in legacyDefinitions)
        {
            string exePath = definition.ExePath.Trim().Trim('"');
            if (exePath == "" || entries.Any(e => SamePath(e.ExePath, exePath))) continue;

            string name = definition.Name.Trim() == "" ? definition.Id.Trim() : definition.Name.Trim();
            entries.Add(new TerminalEntry
            {
                Id          = NewId(name, entries.Select(e => e.Id)),
                Name        = name,
                ExePath     = exePath,
                FolderArgs  = definition.FolderArgs.Trim(),
                CommandArgs = definition.CommandArgs.Trim(),
                Order       = NextOrder(entries),
            });
        }
        return entries;
    }

    /// <summary>
    /// Ajoute les terminaux détectés absents de la liste, et rend leur nombre. Un terminal déjà
    /// présent garde ses arguments, son chemin, son masquage et son ordre ; son nom suit la
    /// détection tant qu'il n'a pas été changé. Un terminal disparu reste dans la liste.
    /// </summary>
    public static int Merge(List<TerminalEntry> entries, IReadOnlyList<TerminalInfo> detected)
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

            entries.Add(new TerminalEntry
            {
                Id           = found.Id,
                Name         = found.DisplayName,
                DetectedName = found.DisplayName,
                ExePath      = found.ExePath,
                FolderArgs   = found.FolderArgs,
                CommandArgs  = found.CommandArgs,
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
