using System.IO;
using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

public class BrowserProfileServiceTests
{
    // ── ParseProfiles ───────────────────────────────────────────────────────────

    private const string LocalState = """
        {
          "profile": {
            "info_cache": {
              "Profile 2":  { "name": "Deux" },
              "Default":    { "name": "Boulot" },
              "Profile 10": { "name": "Dix" },
              "Profile 1":  { "name": "Un" }
            }
          }
        }
        """;

    [Fact]
    public void ParseProfiles_LitDossierEtNom()
    {
        var profiles = BrowserProfileService.ParseProfiles(LocalState);
        Assert.Contains(profiles, p => p is { Directory: "Profile 1", Name: "Un" });
    }

    [Fact]
    public void ParseProfiles_DefaultDAbordPuisNumeroCroissant()
    {
        var profiles = BrowserProfileService.ParseProfiles(LocalState);
        Assert.Equal(["Default", "Profile 1", "Profile 2", "Profile 10"],
                     profiles.Select(p => p.Directory));
    }

    [Fact]
    public void ParseProfiles_NomAbsent_RetombeSurLeDossier()
    {
        var profiles = BrowserProfileService.ParseProfiles(
            """{ "profile": { "info_cache": { "Profile 1": { } } } }""");
        Assert.Equal("Profile 1", Assert.Single(profiles).Name);
    }

    [Fact]
    public void ParseProfiles_JsonInvalide_ListeVide()
    {
        Assert.Empty(BrowserProfileService.ParseProfiles("{ pas du json"));
    }

    [Fact]
    public void ParseProfiles_SansInfoCache_ListeVide()
    {
        Assert.Empty(BrowserProfileService.ParseProfiles("""{ "profile": { } }"""));
    }

    // ── ResolveUserDataDir ──────────────────────────────────────────────────────

    private const string Local = @"C:\Users\X\AppData\Local";

    [Theory]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", @"Google\Chrome")]
    [InlineData(@"C:\Users\X\AppData\Local\Google\Chrome SxS\Application\chrome.exe", @"Google\Chrome SxS")]
    [InlineData(@"C:\Program Files\Google\Chrome Beta\Application\chrome.exe", @"Google\Chrome Beta")]
    [InlineData(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", @"Microsoft\Edge")]
    [InlineData(@"C:\Program Files\Microsoft\Edge Dev\Application\msedge.exe", @"Microsoft\Edge Dev")]
    [InlineData(@"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe", @"BraveSoftware\Brave-Browser")]
    [InlineData(@"C:\Users\X\AppData\Local\Vivaldi\Application\vivaldi.exe", @"Vivaldi")]
    public void ResolveUserDataDir_NavigateurChromium(string exe, string relative)
    {
        Assert.Equal(Path.Combine(Local, relative, "User Data"),
                     BrowserProfileService.ResolveUserDataDir(exe, Local));
    }

    [Fact]
    public void ResolveUserDataDir_ExeInconnu_Null()
    {
        Assert.Null(BrowserProfileService.ResolveUserDataDir(
            @"C:\Program Files\Mozilla Firefox\firefox.exe", Local));
    }

    [Fact]
    public void ResolveUserDataDir_HorsDossierApplication_Null()
    {
        Assert.Null(BrowserProfileService.ResolveUserDataDir(@"D:\portable\chrome.exe", Local));
    }

    // ── Détection sur disque ────────────────────────────────────────────────────

    [Fact]
    public void Detect_LitLesProfilsEtLeursIcones()
    {
        using var tmp = new TempDir();
        var userData = tmp.Sub(@"Google\Chrome\User Data");
        File.WriteAllText(Path.Combine(userData, "Local State"), LocalState);
        var picture = Path.Combine(tmp.Sub(@"Google\Chrome\User Data\Profile 1"),
                                   "Google Profile Picture.png");
        File.WriteAllText(picture, "png");

        var profiles = BrowserProfileService.Detect(
            @"C:\Program Files\Google\Chrome\Application\chrome.exe", tmp.Path);

        Assert.Equal(4, profiles.Count);
        Assert.Equal(picture, profiles.First(p => p.Directory == "Profile 1").IconPath);
        Assert.Null(profiles.First(p => p.Directory == "Default").IconPath);
    }

    [Fact]
    public void Detect_SansLocalState_ListeVide()
    {
        using var tmp = new TempDir();
        Assert.Empty(BrowserProfileService.Detect(
            @"C:\Program Files\Google\Chrome\Application\chrome.exe", tmp.Path));
    }

    [Fact]
    public void FindProfileIcon_ImageEdge()
    {
        using var tmp = new TempDir();
        var dir = tmp.Sub(@"User Data\Default");
        var picture = Path.Combine(dir, "Edge Profile Picture.png");
        File.WriteAllText(picture, "png");

        Assert.Equal(picture, BrowserProfileService.FindProfileIcon(
            Path.Combine(tmp.Path, "User Data"), "Default"));
    }

    // ── MergeProfiles ───────────────────────────────────────────────────────────

    private static BrowsersConfig Cfg() => new()
    {
        Browsers =
        [
            new BrowserEntry { Id = "chrome00", Name = "Chrome", ExePath = @"C:\chrome.exe", Order = 0 },
            new BrowserEntry { Id = "edge0000", Name = "Edge",   ExePath = @"C:\edge.exe",   Order = 1 },
        ],
    };

    private static BrowserEntry Parent(BrowsersConfig cfg) => cfg.Browsers.First(b => b.Id == "chrome00");

    private static readonly List<DetectedProfile> Two =
    [
        new("Default",   "Boulot", null),
        new("Profile 1", "Perso",  null),
    ];

    [Fact]
    public void MergeProfiles_UnSeulProfil_NAjouteRien()
    {
        var cfg = Cfg();
        var added = BrowserProfileService.MergeProfiles(cfg, Parent(cfg), [new("Default", "Boulot", null)]);
        Assert.Empty(added);
        Assert.Equal(2, cfg.Browsers.Count);
    }

    [Fact]
    public void MergeProfiles_DeuxProfils_AjouteDesEnfantsRattachesAuParent()
    {
        var cfg = Cfg();
        var added = BrowserProfileService.MergeProfiles(cfg, Parent(cfg), Two);

        Assert.Equal(2, added.Count);
        Assert.All(added, e =>
        {
            Assert.Equal("chrome00", e.ParentId);
            Assert.Equal(@"C:\chrome.exe", e.ExePath);
        });
        Assert.Equal(["Default", "Profile 1"], added.Select(e => e.ProfileDirectory));
        Assert.Equal(["Boulot", "Perso"], added.Select(e => e.Name));
    }

    [Fact]
    public void MergeProfiles_LesEnfantsSuiventLeurParentDansLOrdre()
    {
        var cfg = Cfg();
        BrowserProfileService.MergeProfiles(cfg, Parent(cfg), Two);

        Assert.Equal(["Chrome", "Boulot", "Perso", "Edge"],
                     BrowserRowLayout.Grouped(cfg).Select(r => r.Entry.Name));
    }

    [Fact]
    public void MergeProfiles_ProfilDejaConnu_ConserveIdMasquageEtNomPersonnalise()
    {
        var cfg = Cfg();
        var added = BrowserProfileService.MergeProfiles(cfg, Parent(cfg), Two);
        var child = added[0];
        child.Name = "Mon boulot"; // renommé par l'utilisateur
        child.Hidden = true;

        BrowserProfileService.MergeProfiles(cfg, Parent(cfg), Two);

        var again = cfg.Browsers.Single(b => b.ProfileDirectory == "Default" && b.ParentId == "chrome00");
        Assert.Same(child, again);
        Assert.Equal("Mon boulot", again.Name);
        Assert.True(again.Hidden);
    }

    [Fact]
    public void MergeProfiles_NomNonPersonnalise_SuitLeRenommageDansLeNavigateur()
    {
        var cfg = Cfg();
        BrowserProfileService.MergeProfiles(cfg, Parent(cfg), Two);

        BrowserProfileService.MergeProfiles(cfg, Parent(cfg),
            [new("Default", "Travail", null), new("Profile 1", "Perso", null)]);

        var child = cfg.Browsers.Single(b => b.ProfileDirectory == "Default" && b.ParentId == "chrome00");
        Assert.Equal("Travail", child.Name);
    }

    [Fact]
    public void MergeProfiles_ProfilDisparuDuNavigateur_EstConserve()
    {
        var cfg = Cfg();
        BrowserProfileService.MergeProfiles(cfg, Parent(cfg), Two);

        BrowserProfileService.MergeProfiles(cfg, Parent(cfg),
            [new("Default", "Boulot", null), new("Profile 3", "Neuf", null)]);

        Assert.Contains(cfg.Browsers, b => b.ProfileDirectory == "Profile 1");
        Assert.Contains(cfg.Browsers, b => b.ProfileDirectory == "Profile 3");
    }

    // ── Dossier temporaire jetable ──────────────────────────────────────────────

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dockpad-tests-" + Guid.NewGuid().ToString("N")[..8]);

        public TempDir() => System.IO.Directory.CreateDirectory(Path);

        public string Sub(string relative)
        {
            var full = System.IO.Path.Combine(Path, relative);
            System.IO.Directory.CreateDirectory(full);
            return full;
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }
}
