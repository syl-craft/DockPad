using System.IO;
using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Les deux grilles écrivent chacune dans ses fichiers, et jamais dans ceux de l'autre.
/// </summary>
/// <remarks>
/// Huit enveloppes d'action prennent la cible en paramètre. C'est exactement le genre d'endroit où
/// l'on en oublie une, et l'oubli ne se voit pas à l'écran : la tuile part simplement dans l'autre
/// grille. Chaque test ci-dessous vérifie les deux moitiés — ce qui a été écrit, et ce qui est
/// resté intact.
/// </remarks>
public class TileRoutingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"tiles_{Guid.NewGuid():N}");
    private readonly TileFiles _a;
    private readonly TileFiles _b;

    public TileRoutingTests()
    {
        Directory.CreateDirectory(_dir);
        _a = new TileFiles(Path.Combine(_dir, "a.json"), Path.Combine(_dir, "a-pages.json"));
        _b = new TileFiles(Path.Combine(_dir, "b.json"), Path.Combine(_dir, "b-pages.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // RunCommand et non OpenUrl : une tuile web sans icône fait partir FaviconService sur le
    // réseau, et un test qui sort de la machine est un test qui casse un jour sans raison. Ce qui
    // est vérifié ici est le routage des fichiers, pas les icônes.
    private static ShortcutAddItem Item(string name) =>
        new() { Name = name, Command = "notepad.exe", Type = ShortcutType.RunCommand };

    // ── Les deux lecteurs ────────────────────────────────────────────────────

    [Fact]
    public void ShortcutService_SaveEtLoad_SurUnChemin_FontUnAllerRetour()
    {
        ShortcutService.Save([new ShortcutEntry { Name = "A", Command = "x" }], _a.EntriesPath);

        var back = ShortcutService.Load(_a.EntriesPath);

        Assert.Single(back);
        Assert.Equal("A", back[0].Name);
    }

    [Fact]
    public void ShortcutService_Save_NEcritQueSonFichier()
    {
        ShortcutService.Save([new ShortcutEntry { Name = "A", Command = "x" }], _a.EntriesPath);
        ShortcutService.Save([], _b.EntriesPath);

        Assert.Single(ShortcutService.Load(_a.EntriesPath));
        Assert.Empty(ShortcutService.Load(_b.EntriesPath));
    }

    [Fact]
    public void PageConfigService_SaveEtLoad_SurUnChemin_FontUnAllerRetour()
    {
        PageConfigService.Save([new PageConfig { Index = 2 }], _a.PagesPath);

        var back = PageConfigService.Load(_a.PagesPath);

        Assert.Equal(2, Assert.Single(back).Index);
    }

    // ── Les enveloppes d'action ──────────────────────────────────────────────

    [Fact]
    public async Task Add_NAlimenteQueLaGrilleVisee()
    {
        var r = await ShortcutActionService.AddAsync([Item("Favori")], _a);

        Assert.True(r.Ok);
        Assert.Equal("Favori", Assert.Single(ShortcutService.Load(_a.EntriesPath)).Name);
        Assert.Empty(ShortcutService.Load(_b.EntriesPath));
    }

    [Fact]
    public async Task Update_NeTouchePasLautreGrille()
    {
        await ShortcutActionService.AddAsync([Item("Avant")], _a);
        await ShortcutActionService.AddAsync([Item("Intact")], _b);

        var r = await ShortcutActionService.UpdateAsync(0, 0, 0, new ShortcutUpdate { Name = "Apres" }, _a);

        Assert.True(r.Ok);
        Assert.Equal("Apres", ShortcutService.Load(_a.EntriesPath)[0].Name);
        Assert.Equal("Intact", ShortcutService.Load(_b.EntriesPath)[0].Name);
    }

    [Fact]
    public async Task Move_NeDeplaceQueDansLaGrilleVisee()
    {
        await ShortcutActionService.AddAsync([Item("M")], _a);
        await ShortcutActionService.AddAsync([Item("Intact")], _b);

        var r = ShortcutActionService.Move(0, 0, 0, 0, 1, 2, _a);

        Assert.True(r.Ok);
        var moved = Assert.Single(ShortcutService.Load(_a.EntriesPath));
        Assert.Equal((1, 2), (moved.Row, moved.Col));
        var untouched = Assert.Single(ShortcutService.Load(_b.EntriesPath));
        Assert.Equal((0, 0), (untouched.Row, untouched.Col));
    }

    [Fact]
    public async Task Delete_NeSupprimeQueDansLaGrilleVisee()
    {
        await ShortcutActionService.AddAsync([Item("Cible")], _a);
        await ShortcutActionService.AddAsync([Item("Intact")], _b);

        var r = ShortcutActionService.Delete(0, 0, 0, _a);

        Assert.True(r.Ok);
        Assert.Empty(ShortcutService.Load(_a.EntriesPath));
        Assert.Single(ShortcutService.Load(_b.EntriesPath));
    }

    [Fact]
    public async Task GetGrid_LitLaGrilleVisee()
    {
        await ShortcutActionService.AddAsync([Item("Seulement dans A")], _a);

        var r = ShortcutActionService.GetGrid(null, _a);
        var vide = ShortcutActionService.GetGrid(null, _b);

        Assert.True(r.Ok);
        Assert.Contains("Seulement dans A", System.Text.Json.JsonSerializer.Serialize(r.Data));
        Assert.DoesNotContain("Seulement dans A", System.Text.Json.JsonSerializer.Serialize(vide.Data));
    }

    // ── Les pages ────────────────────────────────────────────────────────────

    [Fact]
    public void PageAdd_NAjouteLaPageQuaLaGrilleVisee()
    {
        var r = PageActionService.Add(null, _a);

        Assert.True(r.Ok);
        Assert.Single(PageConfigService.Load(_a.PagesPath));
        Assert.Empty(PageConfigService.Load(_b.PagesPath));
    }

    [Fact]
    public void PageDelete_NeSupprimeQueDansLaGrilleVisee()
    {
        PageActionService.Add(null, _a);
        PageActionService.Add(null, _b);

        var r = PageActionService.Delete(1, _a);

        Assert.True(r.Ok);
        Assert.Empty(PageConfigService.Load(_a.PagesPath));
        Assert.Single(PageConfigService.Load(_b.PagesPath));
    }

    [Fact]
    public void PageUpdate_NeRenumeroteQueDansLaGrilleVisee()
    {
        PageActionService.Add(null, _a);
        PageActionService.Add(null, _b);

        var r = PageActionService.Update(1, iconProvided: true, "", newIndex: null, _a);

        Assert.True(r.Ok);
        Assert.Single(PageConfigService.Load(_b.PagesPath));
    }

    // ── Le défaut ────────────────────────────────────────────────────────────

    [Fact]
    public void FilesFor_Shortcuts_EstLeDefautDeTousLesAppelsExistants()
    {
        // La trentaine d'appels de la fenêtre et du serveur MCP ne passent aucune cible : ils
        // doivent continuer de viser les raccourcis, au fichier près.
        var defaults = TileStore.FilesFor(TileTarget.Shortcuts);

        Assert.Equal(ShortcutService.FilePath, defaults.EntriesPath);
        Assert.Equal(PageConfigService.FilePath, defaults.PagesPath);
    }
}
