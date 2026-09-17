using System.Text.Json;
using DockPad.Models;

namespace DockPad.Services;

/// <summary>Dispositions et déplacements entre cases et sous-cases, sans groupe imbriqué.</summary>
public static class TileGroupService
{
    public const string DefaultColor = "#8B5CF6";

    public static bool IsValidColor(string? color) => color is { Length: 7 } && color[0] == '#'
        && color.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

    public static string EffectiveColor(string? color) => IsValidColor(color) ? color! : DefaultColor;

    public static int Capacity(TileLayout layout) => layout switch
    {
        TileLayout.Simple => 1, TileLayout.Quad => 4, TileLayout.TwoPlusFour => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(layout)),
    };

    public static IEnumerable<ShortcutEntry> Leaves(IEnumerable<ShortcutEntry> entries) =>
        entries.SelectMany(e => e.IsGroup ? Leaves(e.Children?.OfType<ShortcutEntry>() ?? []) : [e]);

    public static void Normalize(IEnumerable<ShortcutEntry> entries)
    {
        foreach (var entry in entries)
        {
            entry.Slot = null;
            if (!entry.IsGroup) continue;
            entry.Children ??= [];
            if (entry.Children.Count > Capacity(entry.Layout) || entry.Children.Any(c => c?.IsGroup == true))
                throw new System.IO.InvalidDataException(Loc.T("Group_InvalidDestination"));
            while (entry.Children.Count < Capacity(entry.Layout)) entry.Children.Add(null);
            for (int slot = 0; slot < entry.Children.Count; slot++)
                if (entry.Children[slot] is { } child)
                {
                    child.Page = entry.Page; child.Row = entry.Row; child.Col = entry.Col;
                    child.Slot = slot;
                }
        }
    }

    public static ShortcutEntry? Get(IEnumerable<ShortcutEntry> entries, TileAddress address)
    {
        var root = entries.FirstOrDefault(e => e.Page == address.Page && e.Row == address.Row && e.Col == address.Col);
        return address.Slot is { } slot
            ? root?.Children?.ElementAtOrDefault(slot)
            : root;
    }

    public static bool IsValidDestination(IEnumerable<ShortcutEntry> entries, TileAddress address) =>
        address.Page >= 0 && address.Row is >= 0 and < ShortcutActionService.GridRows
        && address.Col is >= 0 and < ShortcutActionService.GridCols
        && (address.Slot is null || (Get(entries, address.Root) is { IsGroup: true } group
            && address.Slot >= 0 && address.Slot < Capacity(group.Layout)));

    public static void Put(List<ShortcutEntry> entries, TileAddress address, ShortcutEntry? entry)
    {
        if (address.Slot is { } slot)
        {
            var parent = Get(entries, address.Root)!;
            parent.Children![slot] = entry;
        }
        else
        {
            entries.RemoveAll(e => e.Page == address.Page && e.Row == address.Row && e.Col == address.Col);
            if (entry is not null) entries.Add(entry);
        }
        if (entry is not null)
        {
            entry.Page = address.Page; entry.Row = address.Row; entry.Col = address.Col; entry.Slot = address.Slot;
        }
    }

    public static ShortcutEntry Clone(ShortcutEntry entry) =>
        JsonSerializer.Deserialize<ShortcutEntry>(JsonSerializer.Serialize(entry))!;

    public static ActionResult ChangeLayoutCore(List<ShortcutEntry> entries, TileAddress address, TileLayout layout)
    {
        address = address.Root;
        if (!Enum.IsDefined(layout) || !IsValidDestination(entries, address))
            return ActionResult.Fail(Loc.T("Group_InvalidDestination"));
        var root = Get(entries, address);
        if ((root?.Layout ?? TileLayout.Simple) == layout) return ActionResult.Success();
        var children = root is null ? new List<ShortcutEntry>()
            : root.IsGroup ? root.Children!.OfType<ShortcutEntry>().ToList() : [root];
        int excess = children.Count - Capacity(layout);
        if (excess > 0) return ActionResult.Fail(Loc.F("Group_TooMany", excess));

        if (layout == TileLayout.Simple)
            Put(entries, address, children.SingleOrDefault());
        else
            Put(entries, address, new ShortcutEntry
            {
                Name = root?.Name ?? "",
                GroupColor = root?.IsGroup == true ? root.GroupColor : null,
                Layout = layout,
                Children = children.Cast<ShortcutEntry?>().Concat(
                    Enumerable.Repeat<ShortcutEntry?>(null, Capacity(layout) - children.Count)).ToList(),
            });
        Normalize(entries);
        return ActionResult.Success();
    }

    public static ActionResult RenameCore(List<ShortcutEntry> entries, TileAddress address, string name)
    {
        var group = Get(entries, address.Root);
        if (group?.IsGroup != true) return ActionResult.Fail(Loc.T("Group_InvalidDestination"));
        if (string.IsNullOrWhiteSpace(name)) return ActionResult.Fail(Loc.T("Shortcut_Err_NameRequired"));
        group.Name = name.Trim();
        return ActionResult.Success();
    }

    public static ActionResult UpdateCore(List<ShortcutEntry> entries, TileAddress address, string name, string color)
    {
        if (!IsValidColor(color)) return ActionResult.Fail(Loc.T("Group_InvalidColor"));
        var result = RenameCore(entries, address, name);
        if (result.Ok) Get(entries, address.Root)!.GroupColor = color.ToUpperInvariant();
        return result;
    }

    public static bool CanMove(List<ShortcutEntry> entries, TileAddress from, TileAddress to)
    {
        if (from == to || !IsValidDestination(entries, from) || !IsValidDestination(entries, to)) return false;
        var source = Get(entries, from);
        var target = Get(entries, to);
        if (source is null) return false;
        // Un groupe peut échanger sa case avec une tuile entière, jamais avec une sous-case.
        if (source.IsGroup && to.Slot is not null) return false;
        if (target?.IsGroup == true && (!source.IsGroup || from.Slot is not null)) return false;
        if (from.Root == to.Root && (from.Slot is null || to.Slot is null)) return false;
        return true;
    }

    public static ActionResult MoveCore(List<ShortcutEntry> entries, TileAddress from, TileAddress to)
    {
        if (from == to) return ActionResult.Success();
        if (!CanMove(entries, from, to)) return ActionResult.Fail(Loc.T("Group_InvalidDestination"));
        var source = Get(entries, from)!;
        var target = Get(entries, to);
        // Retirer les deux avant de changer leurs coordonnées (échange de cases racines).
        Put(entries, from, null);
        Put(entries, to, null);
        Put(entries, from, target);
        Put(entries, to, source);
        Normalize(entries);
        return ActionResult.Success();
    }

    public static ActionResult Mutate(TileFiles files, Func<List<ShortcutEntry>, ActionResult> action)
    {
        lock (ConfigLock.Gate)
        {
            var entries = ShortcutService.Load(files.EntriesPath);
            var result = action(entries);
            if (result.Ok) ShortcutService.Save(entries, files.EntriesPath);
            return result;
        }
    }
}
