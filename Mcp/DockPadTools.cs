using System.ComponentModel;
using System.Text.Json;
using DockPad.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace DockPad.Mcp;

[McpServerToolType]
public static class DockPadTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private const string PosDoc = "Positions 0-based : page 0 = première page, lignes 0-3, colonnes 0-5.";

    /// <summary>
    /// Documentation du paramètre de grille, la même sur les huit outils : une seule phrase, donc
    /// une seule à corriger le jour où une troisième grille apparaîtrait.
    /// </summary>
    private const string TargetDoc =
        "Grille visée : \"shortcuts\" (défaut) ou \"favorites\". Les favoris ont leurs propres pages " +
        "et leurs propres positions ; les deux grilles ne se mélangent jamais.";

    // ───── Grille ─────

    [McpServerTool(Name = "dockpad_grid_get")]
    [Description("État de la grille DockPad : pages, tuiles (nom, type, commande, icônes) et cases " +
                 "libres. iconProfilePath est relatif à %APPDATA%\\DockPad\\ ; iconPath est le chemin " +
                 "source d'origine. " + PosDoc)]
    public static string GridGet(
        [Description("Limiter à une page (0-based). Omis = toutes les pages.")] int? page = null,
        [Description(TargetDoc)] string? target = null)
        => Call("dockpad_grid_get", new { page, target });

    [McpServerTool(Name = "dockpad_shortcut_add")]
    [Description("Ajoute un ou plusieurs raccourcis (lot tout-ou-rien). Position omise = première case " +
                 "libre de la page ; position occupée = erreur listant les cases libres. " + PosDoc)]
    public static string ShortcutAdd(
        [Description("Tuiles à créer. Champs par item : name (requis), command (requis), type " +
                     "(RunCommand|OpenFolder|OpenUrl|OpenTerminal|SwitchToProcess, défaut RunCommand), " +
                     "page/row/col (optionnels), iconPath (optionnel — sinon icône de l'exe), " +
                     "terminal (pour OpenTerminal), processSwitch (pour SwitchToProcess).")]
        List<ShortcutAddItem> items,
        [Description(TargetDoc)] string? target = null)
        => Call("dockpad_shortcut_add", new { items, target });

    [McpServerTool(Name = "dockpad_shortcut_update")]
    [Description("Modifie une tuile identifiée par (page, row, col). Seuls les champs fournis changent. " + PosDoc)]
    public static string ShortcutUpdate(int page, int row, int col,
        [Description("Champs à modifier : name, type, command, iconPath, terminal, processSwitch.")]
        ShortcutUpdate changes,
        [Description(TargetDoc)] string? target = null)
        => Call("dockpad_shortcut_update", new { page, row, col, changes, target });

    [McpServerTool(Name = "dockpad_shortcut_move")]
    [Description("Déplace une tuile vers une page/case de la MÊME grille, ou vers l'autre grille " +
                 "si toTarget en désigne une autre. Sans toRow/toCol : même case si libre, sinon " +
                 "première case libre de la page cible. " + PosDoc)]
    public static string ShortcutMove(int page, int row, int col,
                                      [Description("Page d'arrivée. Requis pour un déplacement dans la " +
                                                   "même grille ; ignoré quand toTarget change de grille, " +
                                                   "les deux grilles ayant leurs propres pages.")]
                                      int? toPage = null,
                                      int? toRow = null, int? toCol = null,
                                      [Description(TargetDoc)] string? target = null,
                                      [Description("Grille d'arrivée. Omise ou égale à target : déplacement " +
                                                   "dans la grille, comme avant. Différente : la tuile change " +
                                                   "de grille et se pose à la première case libre, en gardant " +
                                                   "son icône. \"shortcuts\" ou \"favorites\".")]
                                      string? toTarget = null)
        => Call("dockpad_shortcut_move", new { page, row, col, toPage, toRow, toCol, target, toTarget });

    [McpServerTool(Name = "dockpad_shortcut_delete")]
    [Description("Supprime une tuile. Requiert l'option « Autoriser la suppression » de DockPad. " + PosDoc)]
    public static string ShortcutDelete(int page, int row, int col,
                                       [Description(TargetDoc)] string? target = null)
        => Call("dockpad_shortcut_delete", new { page, row, col, target });

    // ───── Pages ─────

    [McpServerTool(Name = "dockpad_page_add")]
    [Description("Crée une nouvelle page (à la fin) et renvoie son index 0-based.")]
    public static string PageAdd(
        [Description("Icône du bouton de pagination (chemin .png/.ico/.exe…). Optionnel.")]
        string? iconPath = null,
        [Description(TargetDoc)] string? target = null)
        => Call("dockpad_page_add", new { iconPath, target });

    [McpServerTool(Name = "dockpad_page_update")]
    [Description("Change l'icône d'une page et/ou la déplace par insertion à newIndex (pages " +
                 "intermédiaires décalées). iconPath : omis = icône inchangée, chaîne vide \"\" = " +
                 "retirer l'icône, chemin = nouvelle icône.")]
    public static string PageUpdate(int index, string? iconPath = null, int? newIndex = null,
                                    [Description(TargetDoc)] string? target = null)
        // iconPath null est omis du JSON (WhenWritingNull) → « inchangé » côté dispatcher ;
        // "" est transmis → « retirer » ; un chemin est transmis → nouvelle icône.
        => Call("dockpad_page_update", new { index, iconPath, newIndex, target });

    [McpServerTool(Name = "dockpad_page_delete")]
    [Description("Supprime une page : ses tuiles sont supprimées, les pages suivantes décalées. " +
                 "Requiert l'option « Autoriser la suppression » de DockPad.")]
    public static string PageDelete(int index,
                                    [Description(TargetDoc)] string? target = null)
        => Call("dockpad_page_delete", new { index, target });

    // ───── Navigateurs & règles ─────

    [McpServerTool(Name = "dockpad_browser_list")]
    [Description("Navigateurs configurés dans DockPad (id, name, exePath, arguments, hidden, order) " +
                 "et délai d'ouverture automatique de la popup. Une entrée avec parentId est un profil " +
                 "du navigateur correspondant (profileDirectory = dossier du profil) ; les profils " +
                 "suivent leur navigateur dans la liste et peuvent être visés par une règle de domaine.")]
    public static string BrowserList() => Call("dockpad_browser_list", new { });

    [McpServerTool(Name = "dockpad_browser_update")]
    [Description("Modifie un navigateur ou un profil par id (voir dockpad_browser_list). Seuls les " +
                 "champs fournis changent : name, exePath, arguments (%1 = URL), hidden (masqué dans " +
                 "la popup), order (position 0-based). Pour un profil, order est sa position parmi les " +
                 "profils de son navigateur.")]
    public static string BrowserUpdate(string id, BrowserUpdate changes)
        => Call("dockpad_browser_update", new { id, changes });

    [McpServerTool(Name = "dockpad_rule_list")]
    [Description("Règles domaine → navigateur du sélecteur d'URLs de DockPad.")]
    public static string RuleList() => Call("dockpad_rule_list", new { });

    [McpServerTool(Name = "dockpad_rule_add")]
    [Description("Ajoute une règle « toujours ouvrir ce domaine avec ce navigateur ». host = domaine, " +
                 "port optionnel (ex. github.com, localhost:44351) ; matche aussi les sous-domaines.")]
    public static string RuleAdd(string host, string browserId)
        => Call("dockpad_rule_add", new { host, browserId });

    [McpServerTool(Name = "dockpad_rule_delete")]
    [Description("Supprime la règle de domaine pour ce host. Requiert l'option « Autoriser la " +
                 "suppression » de DockPad.")]
    public static string RuleDelete(string host)
        => Call("dockpad_rule_delete", new { host });

    // ───── Relais pipe ─────

    private static string Call(string tool, object args)
    {
        var request = JsonSerializer.Serialize(new { tool, args }, JsonOpts);
        string response;
        try
        {
            response = Services.McpPipeService.Send(request);
        }
        catch (Exception ex)
        {
            Services.LogService.Warn(ex, $"Relais MCP : échange interrompu ({tool})");
            throw new McpException(
                "Communication avec DockPad interrompue. Vérifie que l'application est ouverte et consulte son journal. " +
                "Une action déjà reçue peut avoir été exécutée : relis l'état avant de la relancer.");
        }

        using var doc = JsonDocument.Parse(response);
        if (doc.RootElement.GetProperty("ok").GetBoolean())
            return doc.RootElement.TryGetProperty("data", out var d) && d.ValueKind != JsonValueKind.Null
                ? d.GetRawText()
                : """{"ok":true}""";
        throw new McpException(doc.RootElement.GetProperty("error").GetString() ?? "Erreur inconnue.");
    }
}
