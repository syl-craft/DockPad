using DockPad.Models;

namespace DockPad.Services.StreamDock;

public abstract record DockScreen
{
    public sealed record Home(int Sheet) : DockScreen;
    public sealed record Page(int PageIndex, int Sheet) : DockScreen;
    public sealed record Group(int PageIndex, int Row, int Col, int PageSheet, int Sheet) : DockScreen;
}

public abstract record DockKey
{
    public sealed record Empty : DockKey;
    public sealed record GoHome : DockKey;
    public sealed record Back : DockKey;
    public sealed record Next : DockKey;
    public sealed record OpenPage(int PageIndex, string? IconRef) : DockKey;
    public sealed record RunTile(ShortcutEntry Entry) : DockKey;
    public sealed record OpenGroup(ShortcutEntry Entry) : DockKey;
}

public sealed record DockView(DockScreen Screen, IReadOnlyList<DockKey> Keys, int SheetCount);

/// <summary>
/// Contenu et navigation des touches du dock, indépendants du rendu et du matériel.
/// </summary>
public static class DockLayout
{
    private const int DockKeyCount = 15;

    /// <summary>
    /// Numéros de pages garnies, dans l’ordre de la grille, sans les pages vides.
    /// </summary>
    public static IReadOnlyList<int> PagesWithTiles(IReadOnlyList<ShortcutEntry> entries) => entries
        .Where(IsVisibleTile).Select(entry => entry.Page).Distinct().OrderBy(page => page).ToArray();

    /// <summary>
    /// Vue de taille fixe, avec navigation et feuille ramenée dans les limites du contenu.
    /// </summary>
    public static DockView Build(DockScreen screen, IReadOnlyList<ShortcutEntry> entries,
        IReadOnlyList<PageConfig> pages, int keyCount)
    {
        if (keyCount < 3)
            throw new ArgumentOutOfRangeException(nameof(keyCount), "At least three keys are required for navigation and content.");

        switch (screen)
        {
            case DockScreen.Home:
                var pageKeys = PagesWithTiles(entries).Select(pageIndex =>
                {
                    var config = pages.Where(page => page.Index == pageIndex).FirstOrDefault();
                    string? iconRef = IconStoreService.ResolvePageIconPath(config);
                    return (DockKey)new DockKey.OpenPage(pageIndex, iconRef);
                }).ToArray();
                return BuildView(screen, pageKeys, navigation: null, keyCount: keyCount);

            case DockScreen.Page page:
                var tileKeys = TilesOnPage(entries, page.PageIndex).Select(KeyForTile).ToArray();
                return BuildView(screen, tileKeys, new DockKey.GoHome(), keyCount);

            case DockScreen.Group group:
                var entry = FindGroup(entries, group);
                // Un groupe introuvable renvoie à sa page et à la feuille de départ.
                if (entry == null)
                    return Build(new DockScreen.Page(group.PageIndex, group.PageSheet), entries, pages, keyCount);

                var childKeys = ChildrenInSlotOrder(entry).Select(KeyForTile).ToArray();
                return BuildView(screen, childKeys, new DockKey.Back(), keyCount);

            default:
                throw new ArgumentOutOfRangeException(nameof(screen), "Unknown dock screen.");
        }
    }

    /// <summary>
    /// Maintient l’écran après modification de la grille ; une page vidée renvoie à l’accueil.
    /// </summary>
    public static DockScreen Keep(DockScreen current, IReadOnlyList<ShortcutEntry> entries)
    {
        // Suppose 15 touches ; Build ramène la feuille dans les limites.
        if (current is DockScreen.Home home)
        {
            int sheetCount = CountSheets(PagesWithTiles(entries).Count, DockKeyCount);
            return home with { Sheet = ClampSheet(home.Sheet, sheetCount) };
        }

        int pageIndex = current switch
        {
            DockScreen.Page page => page.PageIndex,
            DockScreen.Group group => group.PageIndex,
            _ => throw new ArgumentOutOfRangeException(nameof(current), "Unknown dock screen."),
        };
        int tileCount = entries.Where(entry => entry.Page == pageIndex && IsVisibleTile(entry)).Count();
        if (tileCount == 0)
            return new DockScreen.Home(0);

        int pageSheetCount = CountSheets(tileCount, DockKeyCount - 1);
        if (current is DockScreen.Page currentPage)
            return currentPage with { Sheet = ClampSheet(currentPage.Sheet, pageSheetCount) };

        var currentGroup = (DockScreen.Group)current;
        int pageSheet = ClampSheet(currentGroup.PageSheet, pageSheetCount);
        var groupEntry = FindGroup(entries, currentGroup);
        if (groupEntry == null)
            return new DockScreen.Page(pageIndex, pageSheet);

        int groupSheetCount = CountSheets(ChildrenInSlotOrder(groupEntry).Count(), DockKeyCount - 1);
        return currentGroup with { PageSheet = pageSheet, Sheet = ClampSheet(currentGroup.Sheet, groupSheetCount) };
    }

    /// <summary>
    /// Cible de navigation d’une touche ; null pour une action de tuile ou une touche vide.
    /// </summary>
    public static DockScreen? Navigate(DockView view, DockKey key) => key switch
    {
        DockKey.OpenPage page => new DockScreen.Page(page.PageIndex, 0),
        DockKey.OpenGroup group when view.Screen is DockScreen.Page page => new DockScreen.Group(
            page.PageIndex, group.Entry.Row, group.Entry.Col, PageSheet: page.Sheet, Sheet: 0),
        DockKey.GoHome => new DockScreen.Home(0),
        DockKey.Back when view.Screen is DockScreen.Group group => new DockScreen.Page(group.PageIndex, group.PageSheet),
        DockKey.Next => WithSheet(view.Screen, (SheetOf(view.Screen) + 1) % view.SheetCount),
        _ => null,
    };

    private static DockView BuildView(DockScreen screen, IReadOnlyList<DockKey> content, DockKey? navigation, int keyCount)
    {
        int firstContentKey = navigation == null ? 0 : 1;
        int availableKeys = keyCount - firstContentKey;
        int sheetCount = CountSheets(content.Count, availableKeys);
        bool hasNext = sheetCount > 1;
        int perSheet = availableKeys - (hasNext ? 1 : 0);
        int sheet = ClampSheet(SheetOf(screen), sheetCount);
        DockKey[] keys = Enumerable.Repeat<DockKey>(new DockKey.Empty(), keyCount).ToArray();
        if (navigation != null)
            keys[0] = navigation;

        int offset = sheet * perSheet;
        int visibleCount = Math.Min(perSheet, content.Count - offset);
        for (int index = 0; index < visibleCount; index++)
            keys[firstContentKey + index] = content[offset + index];

        if (hasNext)
            keys[^1] = new DockKey.Next();

        return new DockView(WithSheet(screen, sheet), Array.AsReadOnly(keys), sheetCount);
    }

    private static bool IsVisibleTile(ShortcutEntry entry) => entry.IsGroup || entry.Name.Length > 0;

    private static IEnumerable<ShortcutEntry> TilesOnPage(IReadOnlyList<ShortcutEntry> entries, int pageIndex) => entries
        .Where(entry => entry.Page == pageIndex && IsVisibleTile(entry)).OrderBy(entry => entry.Row).ThenBy(entry => entry.Col);

    private static ShortcutEntry? FindGroup(IReadOnlyList<ShortcutEntry> entries, DockScreen.Group group) => entries
        .Where(entry => entry.Page == group.PageIndex && entry.Row == group.Row && entry.Col == group.Col && entry.IsGroup)
        .FirstOrDefault();

    private static IEnumerable<ShortcutEntry> ChildrenInSlotOrder(ShortcutEntry group) => (group.Children ?? [])
        .Select((entry, index) => (Entry: entry, Slot: entry?.Slot ?? index))
        .OrderBy(child => child.Slot).Select(child => child.Entry).OfType<ShortcutEntry>()
        .Where(entry => entry.IsGroup == false && IsVisibleTile(entry));

    private static DockKey KeyForTile(ShortcutEntry entry) => entry.IsGroup
        ? new DockKey.OpenGroup(entry) : new DockKey.RunTile(entry);

    private static int CountSheets(int contentCount, int availableKeys)
    {
        if (contentCount <= availableKeys)
            return 1;

        int perSheet = availableKeys - 1;
        return 1 + (contentCount - 1) / perSheet;
    }

    private static int ClampSheet(int sheet, int sheetCount) => sheet >= 0 && sheet < sheetCount ? sheet : sheetCount - 1;

    private static int SheetOf(DockScreen screen) => screen switch
    {
        DockScreen.Home home => home.Sheet,
        DockScreen.Page page => page.Sheet,
        DockScreen.Group group => group.Sheet,
        _ => throw new ArgumentOutOfRangeException(nameof(screen), "Unknown dock screen."),
    };

    private static DockScreen WithSheet(DockScreen screen, int sheet) => screen switch
    {
        DockScreen.Home home => home with { Sheet = sheet },
        DockScreen.Page page => page with { Sheet = sheet },
        DockScreen.Group group => group with { Sheet = sheet },
        _ => throw new ArgumentOutOfRangeException(nameof(screen), "Unknown dock screen."),
    };
}
