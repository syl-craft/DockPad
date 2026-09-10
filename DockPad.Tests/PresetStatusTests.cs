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
        // La commande finit par « codex », que ce soit via Windows Terminal ou le repli
        // PowerShell — c'est le seul invariant qui vaille sur toutes les machines.
        var codex = PresetService.GetPresets().Single(p => p.RegistryKey == "OpenCodexTerminal");

        Assert.Contains("%V", codex.Command);
        Assert.EndsWith("codex", codex.Command);
    }

    [Fact]
    public void Codex_EtClaude_NePartagentPasLeurCleDeRegistre()
    {
        // Deux clés identiques et le second prédéfini écraserait le premier, en silence.
        var keys = PresetService.GetPresets().Select(p => p.RegistryKey).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }
}
