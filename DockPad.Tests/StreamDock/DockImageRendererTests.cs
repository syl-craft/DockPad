using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockPad.Models;
using DockPad.Services;
using DockPad.Services.Localization;
using DockPad.Services.StreamDock;
using DockPad.Surfaces;

namespace DockPad.Tests;

/// <summary>
/// Dimensions, opacité, appui, replis d’icônes, noms longs et mosaïques avec trous ou six emplacements.
/// File STA commune aux touches et à la colonne, arrêt et références PNG.
/// </summary>
public class DockImageRendererTests : IDisposable
{
    private readonly CultureInfo _previousCulture = Loc.Current;

    public DockImageRendererTests() => Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));

    public void Dispose() => Loc.SetCulture(_previousCulture);

    [Theory]
    [InlineData(96)]
    [InlineData(72)]
    public async Task Touche_TailleDemandee(int size)
    {
        var image = await OnStaThread(() => DockImageRenderer.RenderKey(new DockKey.GoHome(), DockKeyStyle.Classic, pressed: false, size: size));
        Assert.Equal(size, image.Width);
        Assert.Equal(size, image.Height);
        Assert.Equal(size * size * 4, image.Bgra32.Length);
    }

    [Fact]
    public async Task Rendu_EntierementOpaque()
    {
        var images = await OnStaThread(() =>
        {
            DockKey[] keys = [new DockKey.Empty(), new DockKey.GoHome(), new DockKey.Back(), new DockKey.Next(),
                new DockKey.OpenPage(2, null), new DockKey.RunTile(Tile()), new DockKey.OpenGroup(Group())];
            return keys.SelectMany(key => new[] { false, true }.Select(pressed =>
                    DockImageRenderer.RenderKey(key, DockKeyStyle.Classic, pressed, size: 96)))
                .ToArray();
        });
        foreach (var image in images)
            for (int offset = 3; offset < image.Bgra32.Length; offset += 4)
                Assert.Equal((byte)255, image.Bgra32[offset]);
    }

    [Fact]
    public async Task Enfoncee_DiffereDeLaNormale()
    {
        var (normal, pressed) = await OnStaThread(() => (
            DockImageRenderer.RenderKey(new DockKey.GoHome(), DockKeyStyle.Classic, pressed: false, size: 96),
            DockImageRenderer.RenderKey(new DockKey.GoHome(), DockKeyStyle.Classic, pressed: true, size: 96)));
        Assert.False(normal.Bgra32.SequenceEqual(pressed.Bgra32));
        Assert.Equal(((byte)30, (byte)30, (byte)30), PixelAt(normal, x: 0, y: 0));
        Assert.Equal(((byte)58, (byte)58, (byte)58), PixelAt(pressed, x: 0, y: 0));
        Assert.True(WhitePixels(pressed, top: 4, bottom: 57) < WhitePixels(normal, top: 4, bottom: 57));
    }

    [Fact]
    public async Task IconeIllisible_AfficheLaPremiereLettre()
    {
        string folder = Path.Combine(Path.GetTempPath(), "DockPadRenderer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string corrupted = Path.Combine(folder, "corrupted.png");
            File.WriteAllBytes(corrupted, [0, 1, 2, 3]);
            foreach (string iconPath in new[] { Path.Combine(folder, "missing.png"), corrupted })
            {
                var (fallback, missing) = await OnStaThread(() => (
                    DockImageRenderer.RenderKey(new DockKey.RunTile(Tile()), DockKeyStyle.Classic, pressed: false, size: 96),
                    DockImageRenderer.RenderKey(new DockKey.RunTile(Tile(iconPath)), DockKeyStyle.Classic, pressed: false, size: 96)));
                Assert.Equal(fallback.Bgra32, missing.Bgra32);
                Assert.True(WhitePixels(missing, top: 8, bottom: 56) > 10);
                Assert.NotEqual(((byte)30, (byte)30, (byte)30), PixelAt(missing, x: 30, y: 20));
            }
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task IconeDuProfil_PrioritaireEtComposeeSurFondOpaque()
    {
        string folder = Path.Combine(Path.GetTempPath(), "DockPadRenderer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await OnStaThread(() =>
            {
                string profileIcon = Path.Combine(folder, "profile.png");
                string originalIcon = Path.Combine(folder, "original.png");
                WritePng(profileIcon, SolidImage(blue: 0, green: 0, red: 255, alpha: 128));
                WritePng(originalIcon, SolidImage(blue: 255, green: 0, red: 0, alpha: 255));
                var entry = Tile(originalIcon);
                entry.IconProfilePath = Path.GetRelativePath(IconStoreService.ProfileRoot, profileIcon);
                var tile = DockImageRenderer.RenderKey(new DockKey.RunTile(entry), DockKeyStyle.Classic, pressed: false, size: 96);
                var page = DockImageRenderer.RenderKey(new DockKey.OpenPage(0, profileIcon), DockKeyStyle.Classic, pressed: false, size: 96);
                foreach (var image in new[] { tile, page })
                {
                    var center = PixelAt(image, x: 48, y: 32);
                    Assert.InRange(center.Red, (byte)140, (byte)145);
                    Assert.InRange(center.Blue, (byte)13, (byte)17);
                    Assert.Equal(center.Blue, center.Green);
                    Assert.Equal((byte)255, image.Bgra32[(32 * image.Width + 48) * 4 + 3]);
                    Assert.Equal(((byte)30, (byte)30, (byte)30), PixelAt(image, x: 48, y: 10));
                }
                return true;
            });
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task Groupe_NomDeRepliEtQuatrePremiersEnfants()
    {
        await OnStaThread(() =>
        {
            var group = Group();
            var baseline = DockImageRenderer.RenderKey(new DockKey.OpenGroup(group), DockKeyStyle.Classic, pressed: false, size: 96);
            group.Name = Loc.T("Group_Actions");
            Assert.Equal(baseline.Bgra32,
                DockImageRenderer.RenderKey(new DockKey.OpenGroup(group), DockKeyStyle.Classic, pressed: false, size: 96).Bgra32);
            var children = group.Children ?? throw new InvalidOperationException("Missing group fixture children.");
            children.Add(new ShortcutEntry { Name = "Fifth", Slot = 4 });
            Assert.Equal(baseline.Bgra32,
                DockImageRenderer.RenderKey(new DockKey.OpenGroup(group), DockKeyStyle.Classic, pressed: false, size: 96).Bgra32);
            for (int slot = 0; slot < 4; slot++)
            {
                var child = children[slot];
                children[slot] = new ShortcutEntry { Name = "Zebra", Slot = slot };
                Assert.False(baseline.Bgra32.SequenceEqual(
                    DockImageRenderer.RenderKey(new DockKey.OpenGroup(group), DockKeyStyle.Classic, pressed: false, size: 96).Bgra32));
                children[slot] = child;
            }
            return true;
        });
    }

    [Theory]
    [InlineData(TileLayout.Quad)]
    [InlineData(TileLayout.TwoPlusFour)]
    public async Task Groupe_EmplacementVide_GardeSaPlace(TileLayout layout)
    {
        await OnStaThread(() =>
        {
            var group = Group();
            group.Layout = layout;
            var children = group.Children ?? throw new InvalidOperationException("Missing group fixture children.");
            var baseline = DockImageRenderer.RenderKey(new DockKey.OpenGroup(group), DockKeyStyle.Classic, pressed: false, size: 96);
            children[1] = null;

            var image = DockImageRenderer.RenderKey(new DockKey.OpenGroup(group), DockKeyStyle.Classic, pressed: false, size: 96);

            int top = layout == TileLayout.TwoPlusFour ? 24 : 20;
            Assert.Equal(((byte)30, (byte)30, (byte)30), PixelAt(image, x: 60, y: top));
            Assert.Equal(PixelAt(baseline, x: 36, y: top), PixelAt(image, x: 36, y: top));
            var remainingCenters = layout == TileLayout.TwoPlusFour
                ? new[] { (X: 30, Y: 48), (X: 42, Y: 48) }
                : new[] { (X: 36, Y: 44), (X: 60, Y: 44) };
            foreach (var center in remainingCenters)
            {
                Assert.NotEqual(((byte)30, (byte)30, (byte)30), PixelAt(image, center.X, center.Y));
                Assert.Equal(PixelAt(baseline, center.X, center.Y), PixelAt(image, center.X, center.Y));
            }
            children.Clear();
            var empty = DockImageRenderer.RenderKey(new DockKey.OpenGroup(group), DockKeyStyle.Classic, pressed: false, size: 96);
            children.AddRange(Enumerable.Repeat<ShortcutEntry?>(null, TileGroupService.Capacity(layout)));
            var holes = DockImageRenderer.RenderKey(new DockKey.OpenGroup(group), DockKeyStyle.Classic, pressed: false, size: 96);
            Assert.Equal(empty.Bgra32, holes.Bgra32);
            for (int y = 8; y < 56; y++)
                for (int x = 24; x < 72; x++)
                    Assert.Equal(((byte)30, (byte)30, (byte)30), PixelAt(holes, x, y));
            return true;
        });
    }

    [Fact]
    public async Task Groupe_DeuxPlusQuatre_SixEmplacements()
    {
        await OnStaThread(() =>
        {
            var group = Group();
            group.Layout = TileLayout.TwoPlusFour;
            var children = group.Children ?? throw new InvalidOperationException("Missing group fixture children.");
            children.Add(new ShortcutEntry { Name = "Mail", Slot = 4 });
            children.Add(new ShortcutEntry { Name = "Files", Slot = 5 });
            var baseline = DockImageRenderer.RenderKey(new DockKey.OpenGroup(group), DockKeyStyle.Classic, pressed: false, size: 96);
            (int X, int Y)[] centers = [(36, 24), (60, 24), (30, 48), (42, 48), (54, 48), (66, 48)];
            for (int slot = 0; slot < centers.Length; slot++)
            {
                var center = centers[slot];
                var child = children[slot];
                children[slot] = null;
                var missing = DockImageRenderer.RenderKey(new DockKey.OpenGroup(group), DockKeyStyle.Classic, pressed: false, size: 96);
                children[slot] = child;

                Assert.NotEqual(((byte)30, (byte)30, (byte)30), PixelAt(baseline, center.X, center.Y));
                Assert.Equal(((byte)30, (byte)30, (byte)30), PixelAt(missing, center.X, center.Y));
                for (int other = 0; other < centers.Length; other++)
                    if (other != slot)
                        Assert.Equal(PixelAt(baseline, centers[other].X, centers[other].Y),
                            PixelAt(missing, centers[other].X, centers[other].Y));
            }
            Assert.NotEqual(((byte)30, (byte)30, (byte)30), PixelAt(baseline, x: 42, y: 24));
            Assert.Equal(((byte)30, (byte)30, (byte)30), PixelAt(baseline, x: 36, y: 48));
            return true;
        });
    }

    /// <summary>
    /// Une page avec icône occupe toute la touche : aucun numéro ni libellé dessous.
    /// </summary>
    [Fact]
    public async Task PageAvecIcone_IconeSurTouteLaToucheSansNumero()
    {
        string folder = Path.Combine(Path.GetTempPath(), "DockPadRenderer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string icon = Path.Combine(folder, "page.png");
            var page = await OnStaThread(() =>
            {
                WritePng(icon, SolidImage(blue: 0, green: 0, red: 255, alpha: 255));
                return DockImageRenderer.RenderKey(new DockKey.OpenPage(7, icon), DockKeyStyle.Classic, pressed: false, size: 96);
            });
            Assert.Equal(((byte)0, (byte)0, (byte)255), PixelAt(page, x: 80, y: 48));
            Assert.Equal(((byte)0, (byte)0, (byte)255), PixelAt(page, x: 16, y: 48));
            Assert.Equal(0, WhitePixels(page, top: 0, bottom: 96));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task PageSansIcone_NumeroDockPadEtRepliSiIllisible()
    {
        var (pageOne, pageTwo, missingIcon) = await OnStaThread(() => (
            DockImageRenderer.RenderKey(new DockKey.OpenPage(0, null), DockKeyStyle.Classic, pressed: false, size: 96),
            DockImageRenderer.RenderKey(new DockKey.OpenPage(1, null), DockKeyStyle.Classic, pressed: false, size: 96),
            DockImageRenderer.RenderKey(new DockKey.OpenPage(0, "missing-stream-dock-icon.png"), DockKeyStyle.Classic, pressed: false, size: 96)));
        Assert.Equal(pageOne.Bgra32, missingIcon.Bgra32);
        Assert.False(pageOne.Bgra32.SequenceEqual(pageTwo.Bgra32));
        Assert.True(WhitePixels(pageOne, top: 8, bottom: 56) > 10);
    }

    [Fact]
    public async Task NomLong_TronqueSurDeuxLignes()
    {
        string name = string.Concat(Enumerable.Repeat("Outil ", 12)) + "Lanceur🚀";
        Assert.Equal(81, name.Length);
        var image = await OnStaThread(() => DockImageRenderer.RenderKey(
            new DockKey.RunTile(new ShortcutEntry { Name = name }), DockKeyStyle.Classic, pressed: false, size: 96));
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (x < 2 || x >= image.Width - 2 || y < 2 || y >= image.Height - 2)
                    Assert.Equal(((byte)30, (byte)30, (byte)30), PixelAt(image, x, y));
        Assert.True(WhitePixels(image, top: 60, bottom: 76) > 0);
        Assert.True(WhitePixels(image, top: 76, bottom: 92) > 0);
    }

    [Fact]
    public async Task Vide_EstNoire()
    {
        var images = await OnStaThread(() => new[] {
            DockImageRenderer.RenderKey(new DockKey.Empty(), DockKeyStyle.Classic, pressed: false, size: 96),
            DockImageRenderer.RenderKey(new DockKey.Empty(), DockKeyStyle.Classic, pressed: true, size: 96) });
        foreach (var image in images)
            for (int offset = 0; offset < image.Bgra32.Length; offset += 4)
            {
                Assert.Equal((byte)0, image.Bgra32[offset]);
                Assert.Equal((byte)0, image.Bgra32[offset + 1]);
                Assert.Equal((byte)0, image.Bgra32[offset + 2]);
                Assert.Equal((byte)255, image.Bgra32[offset + 3]);
            }
    }

    [Fact]
    public async Task StaDockRenderer_RendHorsDuThreadAppelant()
    {
        var observations = await OnStaThread(() =>
        {
            int caller = Environment.CurrentManagedThreadId;
            var threads = new List<(int Id, ApartmentState Apartment, bool Background)>();
            void Observe() => threads.Add((Environment.CurrentManagedThreadId,
                Thread.CurrentThread.GetApartmentState(), Thread.CurrentThread.IsBackground));
            using var renderer = new StaDockRenderer(
                (key, style, pressed, size) => { Observe(); return DockImageRenderer.RenderKey(key, style, pressed, size); },
                (content, style, size) => { Observe(); return SideStripRenderer.Render(content, style, size.Width, size.Height); });
            var keyTask = renderer.RenderKeyAsync(new DockKey.GoHome(), DockKeyStyle.Classic, pressed: false, size: 96);
            var displayTask = renderer.RenderSideStripAsync(Column(), SideStripStyle.Rings, new SurfaceSize(75, 380));
            Task.WhenAll(keyTask, displayTask).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            return (Caller: caller, Threads: threads.ToArray());
        });
        Assert.Equal(2, observations.Threads.Length);
        Assert.Equal(observations.Threads[0].Id, observations.Threads[1].Id);
        foreach (var thread in observations.Threads)
        {
            Assert.NotEqual(observations.Caller, thread.Id);
            Assert.Equal(ApartmentState.STA, thread.Apartment);
            Assert.True(thread.Background);
        }
    }

    [Fact]
    public async Task StaDockRenderer_EchecDeRenduNeBloquePasLaFile()
    {
        using var renderer = new StaDockRenderer();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            renderer.RenderKeyAsync(new DockKey.GoHome(), DockKeyStyle.Classic, pressed: false, size: 0));
        var image = await renderer.RenderSideStripAsync(Column(), SideStripStyle.Columns, new SurfaceSize(75, 380)).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(75, image.Width);
        Assert.Equal(380, image.Height);
    }

    [Fact]
    public async Task StaDockRenderer_ArretTermineLeThreadEtRefuseLesRendus()
    {
        Thread? worker = null;
        var renderer = new StaDockRenderer((key, style, pressed, size) =>
        {
            worker = Thread.CurrentThread;
            return DockImageRenderer.RenderKey(key, style, pressed, size);
        }, (content, style, size) => SideStripRenderer.Render(content, style, size.Width, size.Height));
        try
        {
            await renderer.RenderKeyAsync(new DockKey.Empty(), DockKeyStyle.Classic, pressed: false, size: 96).WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { renderer.Dispose(); }
        Assert.NotNull(worker);
        Assert.False(worker.IsAlive);
        renderer.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => renderer.RenderSideStripAsync(Column(), SideStripStyle.Columns, new SurfaceSize(75, 380)));
    }

    [Fact]
    [Trait("Category", "Reference")]
    public async Task References_EcritLesEcransEnPng()
    {
        await OnStaThread(() =>
        {
            string folder = Path.Combine(Path.GetTempPath(), "DockPadStreamDockReference");
            Directory.CreateDirectory(folder);
            List<ShortcutEntry> entries = [Tile(), Group(),
                new ShortcutEntry { Page = 1, Name = "Terminal" }, new ShortcutEntry { Page = 2, Name = "Notes" }];
            for (int index = 2; index < 15; index++)
                entries.Add(new ShortcutEntry { Row = index / 6, Col = index % 6, Name = "Tool " + index });
            WriteView(folder, "home", DockLayout.Build(new DockScreen.Home(0), entries, [], keyCount: 15));
            WriteView(folder, "page", DockLayout.Build(new DockScreen.Page(0, 0), entries, [], keyCount: 15));
            WriteView(folder, "group", DockLayout.Build(new DockScreen.Group(0, 0, 1, 0, 0), entries, [], keyCount: 15));
            WritePng(Path.Combine(folder, "pressed.png"), DockImageRenderer.RenderKey(
                new DockKey.RunTile(Tile()), DockKeyStyle.Classic, pressed: true, size: 96));
            return true;
        });
    }

    private static SideStripContent Column() => new([], 23, 61);

    [Theory]
    [InlineData(255, 0, 0)]
    [InlineData(0, 0, 255)]
    public async Task Halo_TuileTeinteeParLaCouleurDeLIcone(byte red, byte green, byte blue)
    {
        string folder = Path.Combine(Path.GetTempPath(), "DockPadRenderer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var tile = await OnStaThread(() =>
            {
                string icon = Path.Combine(folder, "icon.png");
                WritePng(icon, SolidImage(blue, green, red, alpha: 255));
                return DockImageRenderer.RenderKey(new DockKey.RunTile(Tile(icon)), DockKeyStyle.Halo, pressed: false, size: 96);
            });

            // Bord gauche de la tuile, hors de l'icône et du nom : la teinte domine sans saturer.
            var side = PixelAt(tile, x: 10, y: 50);
            if (red > blue)
                Assert.True(side.Red > side.Blue + 20 && side.Red > side.Green + 20, $"{side}");
            else
                Assert.True(side.Blue > side.Red + 20 && side.Blue > side.Green + 20, $"{side}");
            Assert.True(Math.Max(side.Red, side.Blue) < 200, $"{side}");
            Assert.Equal(((byte)0, (byte)0, (byte)0), PixelAt(tile, x: 1, y: 1));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Halo_NomEcritEnBasDansLaTuile()
    {
        var (named, unnamed) = await OnStaThread(() => (
            DockImageRenderer.RenderKey(new DockKey.RunTile(Tile()), DockKeyStyle.Halo, pressed: false, size: 96),
            DockImageRenderer.RenderKey(new DockKey.RunTile(new ShortcutEntry { Name = "A" }), DockKeyStyle.Halo, pressed: false, size: 96)));

        Assert.True(WhitePixels(named, top: 72, bottom: 88) > WhitePixels(unnamed, top: 72, bottom: 88) + 15);
        Assert.Equal(0, WhitePixels(named, top: 89, bottom: 96));
    }

    [Fact]
    public async Task Halo_SansIcone_InitialeSurLaTeinteDuNom()
    {
        var image = await OnStaThread(() =>
            DockImageRenderer.RenderKey(new DockKey.RunTile(Tile()), DockKeyStyle.Halo, pressed: false, size: 96));

        Assert.True(WhitePixels(image, top: 15, bottom: 61) > 20);
        Assert.NotEqual(((byte)0, (byte)0, (byte)0), PixelAt(image, x: 10, y: 50));
    }

    [Fact]
    public async Task Halo_Appuyee_TuileReduiteEtAssombrie()
    {
        var (normal, pressed) = await OnStaThread(() => (
            DockImageRenderer.RenderKey(new DockKey.GoHome(), DockKeyStyle.Halo, pressed: false, size: 96),
            DockImageRenderer.RenderKey(new DockKey.GoHome(), DockKeyStyle.Halo, pressed: true, size: 96)));

        Assert.NotEqual(((byte)0, (byte)0, (byte)0), PixelAt(normal, x: 6, y: 48));
        Assert.Equal(((byte)0, (byte)0, (byte)0), PixelAt(pressed, x: 6, y: 48));
        Assert.True(PixelAt(pressed, x: 20, y: 70).Red < PixelAt(normal, x: 20, y: 70).Red);
    }

    [Fact]
    public async Task Halo_NavigationEtGroupe_VerreNeutre()
    {
        var group = new ShortcutEntry { Name = "Outils", Layout = TileLayout.Quad, Children = [] };
        var images = await OnStaThread(() => new[]
        {
            DockImageRenderer.RenderKey(new DockKey.GoHome(), DockKeyStyle.Halo, pressed: false, size: 96),
            DockImageRenderer.RenderKey(new DockKey.Next(), DockKeyStyle.Halo, pressed: false, size: 96),
            DockImageRenderer.RenderKey(new DockKey.OpenGroup(group), DockKeyStyle.Halo, pressed: false, size: 96),
        });

        foreach (var image in images)
        {
            var side = PixelAt(image, x: 10, y: 50);
            Assert.InRange(side.Red, (byte)20, (byte)90);
            Assert.InRange(Math.Abs(side.Red - side.Blue), 0, 6);
            Assert.InRange(Math.Abs(side.Red - side.Green), 0, 6);
            Assert.True(WhitePixels(image, top: 72, bottom: 88) > 15);
        }
    }

    [Fact]
    public async Task Halo_ToucheVide_Noire()
    {
        var image = await OnStaThread(() =>
            DockImageRenderer.RenderKey(new DockKey.Empty(), DockKeyStyle.Halo, pressed: false, size: 96));

        Assert.All(Enumerable.Range(0, image.Width * image.Height),
            index => Assert.Equal(((byte)0, (byte)0, (byte)0), PixelAt(image, index % image.Width, index / image.Width)));
    }

    [Fact]
    public async Task CouleurDominante_IgnoreLesPixelsTransparents()
    {
        var color = await OnStaThread(() =>
        {
            var pixels = new byte[8 * 8 * 4];
            for (int index = 0; index < 64; index++)
            {
                bool opaque = index % 8 < 2;
                pixels[index * 4] = (byte)(opaque ? 0 : 255);
                pixels[index * 4 + 2] = (byte)(opaque ? 255 : 0);
                pixels[index * 4 + 3] = (byte)(opaque ? 255 : 0);
            }
            var bitmap = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, pixels, 8 * 4);
            return DockImageRenderer.DominantColor(bitmap);
        });

        Assert.True(color.R > 200 && color.B < 60, color.ToString());
    }

    private static ShortcutEntry Tile(string iconPath = "") => new() { Name = "Atlas", IconPath = iconPath };

    private static ShortcutEntry Group() => new()
    {
        Name = "", Layout = TileLayout.Quad, Col = 1,
        Children = [new() { Name = "Code", Slot = 0 }, new() { Name = "Notes", Slot = 1 },
            new() { Name = "Terminal", Slot = 2 }, new() { Name = "Web", Slot = 3 }],
    };

    private static (byte Blue, byte Green, byte Red) PixelAt(SurfaceImage image, int x, int y)
    {
        int offset = (y * image.Width + x) * 4;
        return (image.Bgra32[offset], image.Bgra32[offset + 1], image.Bgra32[offset + 2]);
    }

    private static int WhitePixels(SurfaceImage image, int top, int bottom) => Enumerable.Range(top, bottom - top)
        .SelectMany(y => Enumerable.Range(0, image.Width).Select(x => PixelAt(image, x, y)))
        .Where(pixel => pixel.Blue > 220 && pixel.Green > 220 && pixel.Red > 220).Count();

    private static void WriteView(string folder, string name, DockView view)
    {
        var pixels = new byte[5 * 96 * 3 * 96 * 4];
        for (int index = 0; index < view.Keys.Count; index++)
        {
            var key = DockImageRenderer.RenderKey(view.Keys[index], DockKeyStyle.Classic, pressed: false, size: 96);
            for (int y = 0; y < 96; y++)
                Buffer.BlockCopy(key.Bgra32, y * 96 * 4, pixels,
                    (((index / 5 * 96 + y) * 5 * 96) + index % 5 * 96) * 4, 96 * 4);
        }
        WritePng(Path.Combine(folder, name + ".png"), new SurfaceImage(5 * 96, 3 * 96, pixels));
    }

    private static SurfaceImage SolidImage(byte blue, byte green, byte red, byte alpha)
    {
        var pixels = new byte[8 * 4 * 4];
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = blue;
            pixels[offset + 1] = green;
            pixels[offset + 2] = red;
            pixels[offset + 3] = alpha;
        }
        return new SurfaceImage(8, 4, pixels);
    }

    private static void WritePng(string path, SurfaceImage image)
    {
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null,
            image.Bgra32, image.Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static async Task<T> OnStaThread<T>(Func<T> action)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { done.SetResult(action()); }
            catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await done.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
}
