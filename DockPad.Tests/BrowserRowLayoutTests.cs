using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

public class BrowserRowLayoutTests
{
    /// <summary>Chrome (2 profils) puis Edge (sans profil).</summary>
    private static BrowsersConfig Cfg() => new()
    {
        Browsers =
        [
            new BrowserEntry { Id = "chrome00", Name = "Chrome", ExePath = @"C:\chrome.exe", Order = 0 },
            new BrowserEntry { Id = "boulot00", Name = "Boulot", ExePath = @"C:\chrome.exe", Order = 1,
                               ParentId = "chrome00", ProfileDirectory = "Default" },
            new BrowserEntry { Id = "perso000", Name = "Perso",  ExePath = @"C:\chrome.exe", Order = 2,
                               ParentId = "chrome00", ProfileDirectory = "Profile 1" },
            new BrowserEntry { Id = "edge0000", Name = "Edge",   ExePath = @"C:\edge.exe",   Order = 3 },
        ],
    };

    private static List<string> Names(IEnumerable<BrowserRow> rows) => rows.Select(r => r.Entry.Name).ToList();

    // ── Grouped (fenêtre de configuration : tout est affiché) ───────────────────

    [Fact]
    public void Grouped_EnfantsJusteApresLeurParent()
    {
        Assert.Equal(["Chrome", "Boulot", "Perso", "Edge"], Names(BrowserRowLayout.Grouped(Cfg())));
    }

    [Fact]
    public void Grouped_MarqueLesEnfants()
    {
        var rows = BrowserRowLayout.Grouped(Cfg()).ToList();
        Assert.Equal([false, true, true, false], rows.Select(r => r.IsChild));
    }

    [Fact]
    public void Grouped_AucunEnTete()
    {
        var cfg = Cfg();
        cfg.Browsers[0].Hidden = true;
        Assert.DoesNotContain(BrowserRowLayout.Grouped(cfg), r => r.IsHeader);
    }

    // ── ForPicker (popup : masqués exclus) ─────────────────────────────────────

    [Fact]
    public void ForPicker_ExclutLesEntreesMasquees()
    {
        var cfg = Cfg();
        cfg.Browsers.First(b => b.Id == "perso000").Hidden = true;
        Assert.Equal(["Chrome", "Boulot", "Edge"], Names(BrowserRowLayout.ForPicker(cfg)));
    }

    [Fact]
    public void ForPicker_ParentMasqueAvecEnfantsVisibles_DevientUnEnTete()
    {
        var cfg = Cfg();
        cfg.Browsers.First(b => b.Id == "chrome00").Hidden = true;

        var rows = BrowserRowLayout.ForPicker(cfg).ToList();

        Assert.Equal(["Chrome", "Boulot", "Perso", "Edge"], Names(rows));
        Assert.True(rows[0].IsHeader);
        Assert.DoesNotContain(rows[1..], r => r.IsHeader);
    }

    [Fact]
    public void ForPicker_ParentMasqueSansEnfantVisible_EstAbsent()
    {
        var cfg = Cfg();
        foreach (var b in cfg.Browsers.Where(b => b.Id != "edge0000")) b.Hidden = true;
        Assert.Equal(["Edge"], Names(BrowserRowLayout.ForPicker(cfg)));
    }

    // ── Move (↑ / ↓ dans la fenêtre de configuration) ──────────────────────────

    [Fact]
    public void Move_Parent_DeplaceToutSonGroupe()
    {
        var cfg = Cfg();
        BrowserRowLayout.Move(cfg, cfg.Browsers.First(b => b.Id == "chrome00"), +1);
        Assert.Equal(["Edge", "Chrome", "Boulot", "Perso"], Names(BrowserRowLayout.Grouped(cfg)));
    }

    [Fact]
    public void Move_Enfant_EchangeAvecSonFrere()
    {
        var cfg = Cfg();
        BrowserRowLayout.Move(cfg, cfg.Browsers.First(b => b.Id == "boulot00"), +1);
        Assert.Equal(["Chrome", "Perso", "Boulot", "Edge"], Names(BrowserRowLayout.Grouped(cfg)));
    }

    [Fact]
    public void Move_PremierEnfantVersLeHaut_NeSortPasDuGroupe()
    {
        var cfg = Cfg();
        BrowserRowLayout.Move(cfg, cfg.Browsers.First(b => b.Id == "boulot00"), -1);
        Assert.Equal(["Chrome", "Boulot", "Perso", "Edge"], Names(BrowserRowLayout.Grouped(cfg)));
    }

    [Fact]
    public void Move_DernierParentVersLeBas_NeFaitRien()
    {
        var cfg = Cfg();
        BrowserRowLayout.Move(cfg, cfg.Browsers.First(b => b.Id == "edge0000"), +1);
        Assert.Equal(["Chrome", "Boulot", "Perso", "Edge"], Names(BrowserRowLayout.Grouped(cfg)));
    }

    [Fact]
    public void Move_ReindexeLesOrdresSansTrou()
    {
        var cfg = Cfg();
        BrowserRowLayout.Move(cfg, cfg.Browsers.First(b => b.Id == "chrome00"), +1);
        Assert.Equal([0, 1, 2, 3], BrowserRowLayout.Grouped(cfg).Select(r => r.Entry.Order));
    }

    // ── Enfants d'un parent supprimé ───────────────────────────────────────────

    [Fact]
    public void Grouped_EnfantSansParent_EstAffichéCommeNavigateur()
    {
        var cfg = Cfg();
        cfg.Browsers.RemoveAll(b => b.Id == "chrome00");

        var rows = BrowserRowLayout.Grouped(cfg).ToList();

        Assert.Equal(["Edge", "Boulot", "Perso"], Names(rows));
        Assert.DoesNotContain(rows, r => r.IsChild);
    }

    // ── DisplayName (ComboBox des règles de domaine) ───────────────────────────

    [Fact]
    public void DisplayName_Navigateur_SonNom()
    {
        var cfg = Cfg();
        Assert.Equal("Chrome", BrowserRowLayout.DisplayName(cfg, cfg.Browsers.First(b => b.Id == "chrome00")));
    }

    [Fact]
    public void DisplayName_Profil_PrefixeParSonNavigateur()
    {
        var cfg = Cfg();
        Assert.Equal("Chrome › Boulot",
                     BrowserRowLayout.DisplayName(cfg, cfg.Browsers.First(b => b.Id == "boulot00")));
    }

    [Fact]
    public void DisplayName_ProfilOrphelin_SonNomSeul()
    {
        var cfg = Cfg();
        cfg.Browsers.RemoveAll(b => b.Id == "chrome00");
        Assert.Equal("Boulot", BrowserRowLayout.DisplayName(cfg, cfg.Browsers.First(b => b.Id == "boulot00")));
    }

    [Fact]
    public void Children_RetourneLesProfilsDUnNavigateur()
    {
        var cfg = Cfg();
        Assert.Equal(["Boulot", "Perso"],
                     BrowserRowLayout.Children(cfg, "chrome00").Select(b => b.Name));
    }
}
