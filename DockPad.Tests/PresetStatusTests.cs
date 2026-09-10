using System.IO;
using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Statut d'un prédéfini face à ce que porte déjà le registre.
/// </summary>
/// <remarks>
/// La comparaison ne regardait que la commande et l'icône. Après un changement de langue, une entrée
/// installée s'affichait donc « Déjà installé », <c>CanSelect</c> était faux, et le bouton refusait
/// de la réappliquer : le libellé du menu contextuel de Windows restait dans l'ancienne langue, sans
/// aucun moyen de le mettre à jour depuis l'interface. Le nom affiché entre dans la comparaison.
/// </remarks>
public class PresetStatusTests
{
    private static PresetEntry Preset(string name = "Ouvrir un terminal Claude") => new()
    {
        DisplayName = name,
        RegistryKey = "OpenClaudeTerminal",
        Command = "wt.exe -w 0 new-tab",
        IconPath = @"C:\app.exe,0",
        Target = ContextMenuTarget.FolderBackground,
    };

    [Fact]
    public void Absent_DuRegistre_EstNonInstalle()
    {
        var status = PresetService.CompareStatus(installed: null, Preset());

        Assert.Equal(PresetStatus.NotInstalled, status);
    }

    [Fact]
    public void Identique_EstAJour()
    {
        var installed = ("Ouvrir un terminal Claude", "wt.exe -w 0 new-tab", @"C:\app.exe,0");

        var status = PresetService.CompareStatus(installed, Preset());

        Assert.Equal(PresetStatus.UpToDate, status);
    }

    [Fact]
    public void CommandeDifferente_EstAMettreAJour()
    {
        var installed = ("Ouvrir un terminal Claude", "ancienne-commande", @"C:\app.exe,0");

        var status = PresetService.CompareStatus(installed, Preset());

        Assert.Equal(PresetStatus.UpdateAvailable, status);
    }

    [Fact]
    public void IconeDifferente_EstAMettreAJour()
    {
        var installed = ("Ouvrir un terminal Claude", "wt.exe -w 0 new-tab", @"C:\ancien.exe,0");

        var status = PresetService.CompareStatus(installed, Preset());

        Assert.Equal(PresetStatus.UpdateAvailable, status);
    }

    [Fact]
    public void LibelleDifferent_EstAMettreAJour()
    {
        // Le cas du changement de langue : commande et icône identiques, seul le libellé a changé.
        // Sans lui, la traduction du menu contextuel serait inatteignable depuis le bouton.
        var installed = ("Open a Claude terminal", "wt.exe -w 0 new-tab", @"C:\app.exe,0");

        var status = PresetService.CompareStatus(installed, Preset());

        Assert.Equal(PresetStatus.UpdateAvailable, status);
    }

    // ── Le catalogue ─────────────────────────────────────────────────────────

    [Fact]
    public void Codex_EstProposeCommeClaude()
    {
        // Décalque du prédéfini Claude : proposé sur toute machine, sans condition
        // d'installation — comme lui, et contrairement à GitHub Desktop.
        var presets = PresetService.GetPresets();

        var codex = Assert.Single(presets, p => p.RegistryKey == "OpenCodexTerminal");
        Assert.Equal(ContextMenuTarget.FolderBackground, codex.Target);
    }

    [Fact]
    public void Codex_LanceCodexDansLeDossierClique()
    {
        // %V est le dossier du clic droit : sans lui le terminal s'ouvrirait n'importe où.
        var codex = PresetService.GetPresets().Single(p => p.RegistryKey == "OpenCodexTerminal");

        Assert.Contains("%V", codex.Command);
        Assert.Contains("codex", codex.Command);
    }

    [Fact]
    public void Codex_EtClaude_NePartagentPasLeurCleDeRegistre()
    {
        // Deux clés identiques et le second prédéfini écraserait le premier, en silence.
        var keys = PresetService.GetPresets().Select(p => p.RegistryKey).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    // ── Ou vit codex.exe ─────────────────────────────────────────────────────
    //
    // Codex s'installe de plusieurs facons, et elles ne posent pas le binaire au meme endroit.
    // Meme patron que la localisation de bw.exe : PATH, puis WinGet, puis — en dernier — le
    // paquet npm, ou codex.exe est enfoui et ABSENT du PATH (celui-ci ne porte que codex.cmd).

    private sealed class TempTree : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"codex_{Guid.NewGuid():N}");
        public TempTree() => Directory.CreateDirectory(Root);

        public string Put(params string[] segments)
        {
            var full = Path.Combine([Root, .. segments]);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "");
            return full;
        }

        public string Dir(params string[] segments) => Path.Combine([Root, .. segments]);

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    [Fact]
    public void CodexExe_LePathDabord()
    {
        using var t = new TempTree();
        var onPath = t.Put("bin", "codex.exe");

        var found = PresetService.FindCodexExe(t.Dir("bin"), installRoots: [], npmRoots: []);

        Assert.Equal(onPath, found);
    }

    [Fact]
    public void CodexExe_PuisLesRacinesDInstallationEnRecursif()
    {
        // WinGet comme l'installateur natif rangent le binaire sous un dossier qui nomme la
        // version : on cherche dessous, on ne le nomme pas.
        using var t = new TempTree();
        var installed = t.Put("winget", "OpenAI.Codex_1.2.3_x64", "codex.exe");

        var found = PresetService.FindCodexExe(pathVariable: "", installRoots: [t.Dir("winget")], npmRoots: []);

        Assert.Equal(installed, found);
    }

    [Fact]
    public void CodexExe_TrouveMemeQuandLePathDuProcessusEstPerime()
    {
        // Le cas vecu : Codex installe pendant que DockPad tourne. Le processus a herite d'un
        // PATH d'avant l'installation, donc le binaire n'y est pas — mais il est bien sur le
        // disque, et l'utilisateur ne comprendrait pas que l'icone reste vide.
        using var t = new TempTree();
        var installed = t.Put("Programs", "OpenAI", "Codex", "bin", "codex.exe");

        var found = PresetService.FindCodexExe(pathVariable: "",
                                               installRoots: [t.Dir("Programs", "OpenAI")], npmRoots: []);

        Assert.Equal(installed, found);
    }

    [Fact]
    public void CodexExe_EnDernierRecoursLePaquetNpm()
    {
        using var t = new TempTree();
        var vendored = t.Put("nodejs", "node_modules", "@openai", "codex", "node_modules",
                             "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc",
                             "bin", "codex.exe");

        var found = PresetService.FindCodexExe(pathVariable: "", installRoots: [],
                                               npmRoots: [t.Dir("nodejs")]);

        Assert.Equal(vendored, found);
    }

    [Fact]
    public void CodexExe_NullePart_RendNull()
    {
        // C'est le cas d'une machine sans Codex : le predefini reste propose, sans icone.
        using var t = new TempTree();

        Assert.Null(PresetService.FindCodexExe(t.Dir("vide"), [t.Dir("vide2")], [t.Dir("vide3")]));
    }

    [Fact]
    public void CodexExe_UneEntreeDePathInvalideNInterromptPas()
    {
        // Le PATH d'une machine reelle porte des entrees mortes et des caracteres illegaux.
        using var t = new TempTree();
        var onPath = t.Put("bin", "codex.exe");

        var found = PresetService.FindCodexExe($"C:\\ne*existe|pas;{t.Dir("bin")}", [], []);

        Assert.Equal(onPath, found);
    }

    // -- Le logo ChatGPT ------------------------------------------------------

    [Fact]
    public void ChatGptExe_PrefereCeluiQuiPorteLeNom()
    {
        // Le paquet n'expose qu'un exe aujourd'hui, mais rien ne le garantit demain : un
        // utilitaire de mise a jour pose a cote, et on extrairait SON icone sans le voir.
        var pick = PresetService.PickChatGptExe(
            [@"C:\app\updater.exe", @"C:\app\ChatGPT Classic.exe", @"C:\app\crashpad.exe"]);

        Assert.Equal(@"C:\app\ChatGPT Classic.exe", pick);
    }

    [Fact]
    public void ChatGptExe_AucunNomReconnu_RendNull()
    {
        // On balaie desormais TOUS les paquets OpenAI, pas seulement celui de ChatGPT : prendre
        // « le premier exe venu » y attraperait chrome_proxy.exe ou un service d'elevation, et
        // l'on poserait leur icone sans le voir. Mieux vaut aucune icone qu'une icone fausse.
        var pick = PresetService.PickChatGptExe(
            [@"C:\app\chrome_proxy.exe", @"C:\app\elevation_service.exe"]);

        Assert.Null(pick);
    }

    [Fact]
    public void ChatGptExe_LeTrouveParmiLesBinairesDuPaquet()
    {
        // Le paquet reel en contient sept, dont Codex.exe — qui n'a PAS d'icone (mesure).
        var pick = PresetService.PickChatGptExe(
            [@"C:\app\chrome_proxy.exe", @"C:\app\Codex.exe",
             @"C:\app\ChatGPT.exe", @"C:\app\notification_helper.exe"]);

        Assert.Equal(@"C:\app\ChatGPT.exe", pick);
    }

    [Fact]
    public void ChatGptExe_AucunExe_RendNull()
    {
        // ChatGPT absent : le predefini retombe sur codex.exe, puis sur aucune icone.
        Assert.Null(PresetService.PickChatGptExe([]));
    }

    // -- L'encodage du .ico ---------------------------------------------------
    //
    // Icon.Save abime l'alpha : mesure sur le logo ChatGPT, 0 frange bleutee a l'extraction
    // et 45 apres l'aller-retour. On ecrit donc le conteneur soi-meme, avec un PNG dedans —
    // format que le shell lit depuis Vista et qui preserve la transparence exactement.

    [Fact]
    public void Ico_PorteLenTeteDUnIconeAUneSeuleImage()
    {
        var png = new byte[] { 1, 2, 3, 4, 5 };

        var ico = PresetService.BuildIco(png, 64);

        Assert.Equal(0, BitConverter.ToUInt16(ico, 0));   // reserve
        Assert.Equal(1, BitConverter.ToUInt16(ico, 2));   // type : icone
        Assert.Equal(1, BitConverter.ToUInt16(ico, 4));   // une seule image
        Assert.Equal(64, ico[6]);                          // largeur
        Assert.Equal(64, ico[7]);                          // hauteur
        Assert.Equal(32, BitConverter.ToUInt16(ico, 12));  // bits par pixel
    }

    [Fact]
    public void Ico_PointeLesDonneesJusteApresLenTete()
    {
        // 6 octets d'ICONDIR + 16 d'ICONDIRENTRY : une erreur ici et le shell lit du vide.
        var png = new byte[] { 9, 8, 7 };

        var ico = PresetService.BuildIco(png, 32);

        // Disposition d'ICONDIRENTRY : largeur, hauteur, palette, réservé, plans (2),
        // bits (2), puis la taille en 14 et le pointeur en 18.
        Assert.Equal(png.Length, BitConverter.ToInt32(ico, 14));
        Assert.Equal(22, BitConverter.ToInt32(ico, 18));
        Assert.Equal(png, ico[22..]);
    }

    [Fact]
    public void Ico_LaTaille256SEcritZero()
    {
        // Un octet ne peut pas porter 256 : le format code cette taille par zero. Sans ca,
        // une icone 256 s'annoncerait de taille nulle et ne s'afficherait pas.
        var ico = PresetService.BuildIco([0], 256);

        Assert.Equal(0, ico[6]);
        Assert.Equal(0, ico[7]);
    }

    // -- La commande lancee ---------------------------------------------------
    //
    // Cas vecu : « erreur 0x80070002, le fichier specifie est introuvable ». Explorer tournait
    // depuis des semaines et transmettait a wt.exe un P@H anterieur a l'installation de Codex.
    // Le nom nu ne se resout que si le P@H du LANCEUR est a jour — ce qu'on ne controle pas.

    [Fact]
    public void CodexCommande_UtiliseLeCheminAbsoluQuandOnLeConnait()
    {
        var cmd = PresetService.CodexCommand(wt: @"C:\wt.exe", powershell: null,
                                             codexExe: @"C:\OpenAI\bin\codex.exe");

        Assert.Contains(@"-- ""C:\OpenAI\bin\codex.exe""", cmd);
    }

    [Fact]
    public void CodexCommande_SansCheminConnu_RetombeSurLeNomNu()
    {
        // Machine ou DockPad ne sait pas localiser le binaire : on laisse le P@H decider,
        // ce qui est le comportement d'avant et vaut mieux que rien.
        var cmd = PresetService.CodexCommand(wt: @"C:\wt.exe", powershell: null, codexExe: null);

        Assert.EndsWith("-- codex", cmd);
    }

    [Fact]
    public void CodexCommande_SansWindowsTerminal_RepliPowerShell()
    {
        var cmd = PresetService.CodexCommand(wt: null, powershell: @"C:\powershell.exe",
                                             codexExe: @"C:\codex.exe");

        Assert.StartsWith(@"""C:\powershell.exe""", cmd);
        Assert.Contains("Set-Location '%V'", cmd);
        Assert.Contains(@"C:\codex.exe", cmd);
    }
}
