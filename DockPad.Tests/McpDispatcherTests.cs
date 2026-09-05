using System.Text.Json;
using DockPad.Models;
using DockPad.Services;
using Xunit;

// Deux états statiques partagés interdisent la parallélisation entre classes de test :
//
//   - McpDispatcher.Handle écrit dans McpLogService.Entries (collection statique partagée avec
//     McpLogServiceTests) ;
//   - Loc.SetCulture écrit la langue du processus (CurrentUICulture et les DefaultThread…), donc
//     une classe qui bascule en anglais casserait une autre qui vérifie un libellé français.
//
// Dans les deux cas l'échec dépendrait de l'ordonnancement, ce qui est le pire des tests instables.
// Un seul assembly de test ici, et la suite tourne en 3 s : pas d'impact perf.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DockPad.Tests;

public class McpDispatcherTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Handle_JsonInvalide_RenvoieErreur()
    {
        var resp = Parse(McpDispatcher.Handle("{pas du json", new McpConfig()));
        Assert.False(resp.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Handle_OutilInconnu_RenvoieErreur()
    {
        var resp = Parse(McpDispatcher.Handle("""{"tool":"dockpad_nope","args":{}}""", new McpConfig()));
        Assert.False(resp.GetProperty("ok").GetBoolean());
        Assert.Contains("inconnu", resp.GetProperty("error").GetString());
    }

    [Fact]
    public void Handle_McpDesactive_RefuseToutOutil()
    {
        var cfg = new McpConfig { Enabled = false };
        var resp = Parse(McpDispatcher.Handle("""{"tool":"dockpad_grid_get","args":{}}""", cfg));
        Assert.False(resp.GetProperty("ok").GetBoolean());
        Assert.Contains("désactivé", resp.GetProperty("error").GetString());
    }

    [Fact]
    public void Handle_SuppressionNonAutorisee_RefuseSansExecuter()
    {
        var cfg = new McpConfig { Enabled = true, AllowDelete = false };
        var resp = Parse(McpDispatcher.Handle(
            """{"tool":"dockpad_shortcut_delete","args":{"page":0,"row":0,"col":0}}""", cfg));
        Assert.False(resp.GetProperty("ok").GetBoolean());
        Assert.Contains("suppression", resp.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    // ── La cible : shortcuts (défaut) ou favorites ────────────────────────────

    [Fact]
    public void Handle_TargetInconnue_RefuseAuLieuDeRetomberSurLesRaccourcis()
    {
        // Un repli silencieux écrirait dans la mauvaise grille sans le dire : c'est le pire des
        // deux comportements, et le seul qu'un modèle ne peut pas rattraper.
        var resp = Parse(McpDispatcher.Handle(
            """{"tool":"dockpad_grid_get","args":{"target":"bookmarks"}}""", new McpConfig()));

        Assert.False(resp.GetProperty("ok").GetBoolean());
        Assert.Contains("bookmarks", resp.GetProperty("error").GetString());
    }

    [Fact]
    public void Handle_TargetFavorites_EstAcceptee()
    {
        var resp = Parse(McpDispatcher.Handle(
            """{"tool":"dockpad_grid_get","args":{"target":"favorites"}}""", new McpConfig()));

        Assert.True(resp.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Handle_SuppressionDansLesFavoris_RestreDerriereAllowDelete()
    {
        // Le verrou est posé sur le NOM de l'outil, pas sur la cible. Rien à faire pour que ce
        // soit vrai — mais une porte ouverte qu'aucun test ne tient finit par s'ouvrir.
        var cfg = new McpConfig { Enabled = true, AllowDelete = false };

        var resp = Parse(McpDispatcher.Handle(
            """{"tool":"dockpad_shortcut_delete","args":{"page":0,"row":0,"col":0,"target":"favorites"}}""", cfg));

        Assert.False(resp.GetProperty("ok").GetBoolean());
    }
}
