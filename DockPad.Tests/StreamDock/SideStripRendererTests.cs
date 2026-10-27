using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockPad.Models;
using DockPad.Services.Localization;
using DockPad.Services.StreamDock;
using DockPad.Surfaces;

namespace DockPad.Tests;

/// <summary>
/// Quatre rendus opaques et déterministes, quotas limités, couleurs, replis PC et textes longs dans les trois langues.
/// Textes réellement dessinés, quinconce, surface du réservoir, valeurs absentes et références PNG.
/// </summary>
public class SideStripRendererTests : IDisposable
{
    private readonly CultureInfo _previousCulture = Loc.Current;
    private static readonly (byte Blue, byte Green, byte Red) Background = (0, 0, 0);

    public SideStripRendererTests() => Loc.SetCulture(CultureInfo.GetCultureInfo("fr"));

    public void Dispose() => Loc.SetCulture(_previousCulture);

    public static IEnumerable<object[]> Styles() => Enum.GetValues<SideStripStyle>().Select(style => new object[] { style });

    [Theory]
    [MemberData(nameof(Styles))]
    public async Task Rendus_TailleEtOpacite(SideStripStyle style)
    {
        await OnStaThread(() =>
        {
            foreach (var content in Cases().Select(item => item.Content))
                foreach (var size in new[] { (Width: 75, Height: 380), (Width: 150, Height: 760) })
                {
                    var image = SideStripRenderer.Render(content, style, size.Width, size.Height);
                    Assert.Equal(size.Width, image.Width);
                    Assert.Equal(size.Height, image.Height);
                    Assert.Equal(size.Width * size.Height * 4, image.Bgra32.Length);
                    for (int offset = 3; offset < image.Bgra32.Length; offset += 4)
                        Assert.Equal((byte)255, image.Bgra32[offset]);
                }
            return true;
        });
    }

    [Fact]
    public async Task Rendus_DifferentsSelonLeStyle()
    {
        await OnStaThread(() =>
        {
            foreach (var content in new[] { Content(0), Content(3) })
            {
                var images = Enum.GetValues<SideStripStyle>().Select(style => Render(content, style)).ToArray();
                for (int first = 0; first < images.Length; first++)
                    for (int second = first + 1; second < images.Length; second++)
                        Assert.False(images[first].Bgra32.SequenceEqual(images[second].Bgra32));
            }
            return true;
        });
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public async Task Rendus_Deterministes(SideStripStyle style)
    {
        await OnStaThread(() =>
        {
            foreach (var content in Cases().Select(item => item.Content))
                Assert.Equal(Render(content, style).Bgra32, Render(content, style).Bgra32);
            return true;
        });
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("qps-Ploc")]
    public async Task Rendus_NomsLongs_RienHorsZoneUtile(string culture)
    {
        Loc.SetCulture(CultureInfo.GetCultureInfo(culture));
        await OnStaThread(() =>
        {
            var typeface = new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            foreach (string text in new[] { new string('W', 60), Loc.T("StreamDock_Week"), "é🚀" + new string('M', 60) })
                foreach (double width in new[] { 10d, 27d, 65d })
                {
                    var fitted = SideStripRenderer.Fit(text, maxWidth: width, fontSize: 18, minFontSize: 9);
                    var measured = new FormattedText(fitted.Text, Loc.Formatting, FlowDirection.LeftToRight,
                        typeface, fitted.FontSize, Brushes.White, pixelsPerDip: 1);
                    Assert.True(measured.WidthIncludingTrailingWhitespace <= width);
                    Assert.InRange(fitted.FontSize, 9, 18);
                }
            Assert.Equal(("5 h", 12d), SideStripRenderer.Fit("5 h", maxWidth: 65, fontSize: 12, minFontSize: 9));
            var shortened = SideStripRenderer.Fit(new string('W', 60), maxWidth: 65, fontSize: 18, minFontSize: 9);
            Assert.EndsWith("…", shortened.Text);
            Assert.Equal(9d, shortened.FontSize);
            Assert.Equal("", SideStripRenderer.Fit("W", maxWidth: 0, fontSize: 12, minFontSize: 9).Text);

            var content = Content(3);
            content = content with { Quotas = content.Quotas.Select(quota => quota with
                { ProviderName = new string('W', 60), WindowLabel = new string('M', 60) }).ToArray() };
            foreach (var style in Enum.GetValues<SideStripStyle>())
            {
                var texts = CollectTexts(content, style);
                Assert.NotEmpty(texts);
                foreach (var drawn in texts)
                {
                    Assert.True(drawn.MeasuredSize.Width <= drawn.Zone.Width, drawn.SourceText);
                    Assert.True(drawn.MeasuredSize.Height <= drawn.Zone.Height, drawn.SourceText);
                    Assert.True(new Rect(0, 0, 75, 380).Contains(drawn.Zone), drawn.SourceText);
                    var measured = new FormattedText(drawn.Text, Loc.Formatting, FlowDirection.LeftToRight,
                        typeface, drawn.FontSize, Brushes.White, pixelsPerDip: 1);
                    Assert.Equal(measured.WidthIncludingTrailingWhitespace, drawn.MeasuredSize.Width);
                    Assert.Equal(measured.Height, drawn.MeasuredSize.Height);
                }
                if (style != SideStripStyle.Columns)
                    Assert.Contains(texts, drawn => drawn.SourceText == new string('W', 60));
                var image = Render(content, style);
                for (int y = 0; y < image.Height; y++)
                {
                    Assert.Equal(Background, PixelAt(image, x: 0, y: y));
                    Assert.Equal(Background, PixelAt(image, x: image.Width - 1, y: y));
                }
                for (int x = 0; x < image.Width; x++)
                {
                    Assert.Equal(Background, PixelAt(image, x: x, y: 0));
                    Assert.Equal(Background, PixelAt(image, x: x, y: image.Height - 1));
                }
            }
            return true;
        });
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public async Task Rendus_AncienPrimeSurAlerte(SideStripStyle style)
    {
        await OnStaThread(() =>
        {
            var quota = Content(1).Quotas[0];
            var stale = new SideStripContent([quota with { IsStale = true }], 23, 61);
            var alert = new SideStripContent([quota with { IsAlert = true }], 23, 61);
            var both = new SideStripContent([quota with { IsStale = true, IsAlert = true }], 23, 61);
            Assert.Equal(Render(stale, style).Bgra32, Render(both, style).Bgra32);
            Assert.True(CountColor(Render(alert, style), (77, 72, 229)) > 0);
            Assert.Equal(0, CountColor(Render(both, style), (77, 72, 229)));
            Assert.True(CountColor(Render(stale, style), (145, 145, 145)) > 0);
            return true;
        });
    }

    [Theory]
    [InlineData("claude", 87, 119, 217)]
    [InlineData("codex", 220, 212, 208)]
    [InlineData("antigravity", 232, 115, 26)]
    [InlineData("unknown", 212, 212, 212)]
    public async Task Rendus_PaletteIdentiteEtMesuresPc(string provider, byte blue, byte green, byte red)
    {
        await OnStaThread(() =>
        {
            var quota = Content(1).Quotas[0] with { ProviderId = provider };
            foreach (var style in Enum.GetValues<SideStripStyle>())
            {
                var image = Render(new SideStripContent([quota], 23, 61), style);
                Assert.True(CountColor(image, (blue, green, red)) > 0);
                Assert.True(CountColor(image, (78, 193, 242)) > 0);
                Assert.True(CountColor(image, (242, 140, 181)) > 0);
            }
            return true;
        });
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public async Task Rendus_MesureAbsente_DiffereDeZero(SideStripStyle style)
    {
        await OnStaThread(() =>
        {
            var content = Content(0);
            Assert.False(Render(content with { CpuPct = null }, style).Bgra32.SequenceEqual(
                Render(content with { CpuPct = 0 }, style).Bgra32));
            Assert.False(Render(content with { MemoryPct = null }, style).Bgra32.SequenceEqual(
                Render(content with { MemoryPct = 0 }, style).Bgra32));
            return true;
        });
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public async Task Rendus_FenetresExcedentaires_NeChangentPasLImage(SideStripStyle style)
    {
        await OnStaThread(() =>
        {
            var content = Content(3);
            int count = style == SideStripStyle.HeroStack || style == SideStripStyle.Rings ? 4 : 3;
            Assert.Equal(Render(content with { Quotas = content.Quotas.Take(count).ToArray() }, style).Bgra32,
                Render(content, style).Bgra32);
            return true;
        });
    }

    [Fact]
    public async Task Rendus_StyleInconnu_Colonnes()
    {
        await OnStaThread(() =>
        {
            Assert.Equal(Render(Content(3), SideStripStyle.Columns).Bgra32,
                Render(Content(3), (SideStripStyle)999).Bgra32);
            return true;
        });
    }

    [Theory]
    [InlineData(SideStripStyle.HeroStack)]
    [InlineData(SideStripStyle.Reservoir)]
    public async Task Rendus_SansQuota_MesurePcLaPlusChargeeEnGrand(SideStripStyle style)
    {
        await OnStaThread(() =>
        {
            foreach (var load in new[] { (Cpu: (int?)83, Memory: (int?)12), (Cpu: (int?)null, Memory: (int?)61) })
            {
                var image = Render(new SideStripContent([], load.Cpu, load.Memory), style);
                var expected = load.Cpu == null ? ((byte)242, (byte)140, (byte)181) : ((byte)78, (byte)193, (byte)242);
                var other = load.Cpu == null ? ((byte)78, (byte)193, (byte)242) : ((byte)242, (byte)140, (byte)181);
                var topPixels = Enumerable.Range(0, image.Width * image.Height / 2)
                    .Select(index => PixelAt(image, index % image.Width, index / image.Width)).ToArray();
                Assert.Contains(expected, topPixels);
                Assert.DoesNotContain(other, topPixels);
            }
            return true;
        });
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public async Task Rendus_PourcentagesBornesEntreZeroEtCent(SideStripStyle style)
    {
        await OnStaThread(() =>
        {
            var quota = Content(1).Quotas[0];
            var excess = new SideStripContent([quota with { UsedPct = 150 }], -5, 150);
            var bounded = new SideStripContent([quota with { UsedPct = 100 }], 0, 100);
            Assert.Equal(Render(bounded, style).Bgra32, Render(excess, style).Bgra32);
            return true;
        });
    }

    [Fact]
    public async Task Colonnes_NiveauRemplitDepuisLeBas()
    {
        await OnStaThread(() =>
        {
            var image = Render(new SideStripContent([], 25, 75), SideStripStyle.Columns);
            Assert.Equal(((byte)50, (byte)43, (byte)40), PixelAt(image, x: 20, y: 100));
            Assert.Equal(((byte)78, (byte)193, (byte)242), PixelAt(image, x: 20, y: 280));
            Assert.Equal(((byte)242, (byte)140, (byte)181), PixelAt(image, x: 55, y: 150));
            return true;
        });
    }

    [Fact]
    public async Task Anneaux_DeuxFenetresAuPlusParFournisseur()
    {
        await OnStaThread(() =>
        {
            var content = Content(2);
            var thirdWindow = content.Quotas[0] with { UsedPct = 49, WindowLabel = "Extra" };
            var extra = content with { Quotas = content.Quotas.Take(2).Append(thirdWindow)
                .Concat(content.Quotas.Skip(2)).ToArray() };
            Assert.Equal(Render(content, SideStripStyle.Rings).Bgra32, Render(extra, SideStripStyle.Rings).Bgra32);
            return true;
        });
    }

    [Fact]
    public async Task Fit_ReduitAvantDeTronquer()
    {
        await OnStaThread(() =>
        {
            var fitted = SideStripRenderer.Fit("100%", maxWidth: 38, fontSize: 24, minFontSize: 9);
            Assert.Equal("100%", fitted.Text);
            Assert.InRange(fitted.FontSize, 9, 23.5);
            return true;
        });
    }

    [Theory]
    [InlineData(0, 380)]
    [InlineData(75, 0)]
    [InlineData(-1, 380)]
    public async Task Rendus_DimensionInvalide_Refusee(int width, int height)
    {
        await OnStaThread(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SideStripRenderer.Render(Content(0), SideStripStyle.Columns, width, height));
            return true;
        });
    }

    [Fact]
    [Trait("Category", "Reference")]
    public async Task References_EcritChaqueRenduEtChaqueCasEnPng()
    {
        await OnStaThread(() =>
        {
            string folder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DockPadSideStripReference"));
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);
            int count = 0;
            foreach (string culture in new[] { "en", "fr", "qps-Ploc" })
            {
                Loc.SetCulture(CultureInfo.GetCultureInfo(culture));
                foreach (var style in Enum.GetValues<SideStripStyle>())
                    foreach (var item in Cases())
                    {
                        var image = Render(item.Content, style);
                        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32,
                            null, image.Bgra32, image.Width * 4);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(folder, $"{culture}-{style}-{item.Name}.png"));
                        encoder.Save(stream);
                        count++;
                    }
            }
            Assert.Equal(count, Directory.GetFiles(folder, "*.png").Length);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Colonnes_TextesHorizontauxEspacesEtSansSymbole(int quotaCount)
    {
        await OnStaThread(() =>
        {
            var content = Content(2) with { CpuPct = 100, MemoryPct = 100 };
            content = content with { Quotas = content.Quotas.Take(quotaCount)
                .Select(quota => quota with { UsedPct = 100 }).ToArray() };
            var texts = CollectTexts(content, SideStripStyle.Columns);
            Assert.Equal(quotaCount * 2 + 2, texts.Count);
            Assert.Equal(quotaCount + 2, texts.Where(text => text.Text == "100").Count());
            Assert.All(texts, text => Assert.DoesNotContain("%", text.Text));
            Assert.All(texts, text => Assert.DoesNotContain(" ", text.Text));
            foreach (var row in texts.GroupBy(text => text.Zone.Y))
            {
                var ordered = row.OrderBy(text => text.Zone.X).ToArray();
                for (int index = 1; index < ordered.Length; index++)
                    Assert.True(ordered[index].Zone.Left - ordered[index - 1].Zone.Right >= 2);
            }
            return true;
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Colonnes_CellulesEtroites_ValeursEnQuinconceNomsSurUneLigne(int quotaCount)
    {
        await OnStaThread(() =>
        {
            var content = Content(2);
            content = content with { Quotas = content.Quotas.Take(quotaCount).ToArray() };
            var texts = CollectTexts(content, SideStripStyle.Columns);
            var values = texts.Where(text => text.Zone.Y < 100).ToArray();
            var labels = texts.Where(text => text.Zone.Y > 300).ToArray();
            Assert.Equal(quotaCount + 2, values.Length);
            Assert.Equal(quotaCount, labels.Length);
            for (int index = 0; index < values.Length; index++)
            {
                Assert.Equal(values[index % 2].Zone.Y, values[index].Zone.Y);
                Assert.True(values[index].Zone.Width > 69d / values.Length);
            }
            Assert.True(values[0].Zone.Bottom < values[1].Zone.Top);
            Assert.All(labels, label => Assert.Equal(labels[0].Zone.Y, label.Zone.Y));
            Assert.All(labels, label => Assert.Equal(label.SourceText, label.Text));
            return true;
        });
    }

    [Fact]
    public async Task Colonnes_FenetresDuFournisseurCoteACoteSousUnSeulLogo()
    {
        await OnStaThread(() =>
        {
            var content = new SideStripContent(
            [
                new QuotaGauge("claude", "Claude", "Semaine", 68, IsAlert: false, IsStale: false, ResetsAt: null,
                    WindowKind: UsageWindowKind.Week),
                new QuotaGauge("codex", "Codex", "Semaine", 65, IsAlert: false, IsStale: false, ResetsAt: null,
                    WindowKind: UsageWindowKind.Week),
                new QuotaGauge("claude", "Claude", "Session", 30, IsAlert: false, IsStale: false, ResetsAt: null,
                    WindowKind: UsageWindowKind.Session),
            ], 23, 61);
            var values = CollectTexts(content, SideStripStyle.Columns).Where(text => text.Zone.Y < 100)
                .OrderBy(text => text.Zone.X).Select(text => text.Text);
            Assert.Equal(new[] { "68", "30", "65", "23", "61" }, values);

            var image = Render(content, SideStripStyle.Columns);
            var claude = ColumnsWithColor(image, (87, 119, 217), fromRow: 340);
            var codex = ColumnsWithColor(image, (220, 212, 208), fromRow: 340);
            double cellWidth = 69d / 5;
            Assert.True(claude.Min() < 3 + cellWidth && claude.Max() > 3 + cellWidth);
            Assert.True(codex.Min() > 3 + cellWidth * 2 && codex.Max() < 3 + cellWidth * 3);
            return true;
        });
    }

    [Fact]
    public async Task Colonnes_FournisseurAncien_LogoGrise()
    {
        await OnStaThread(() =>
        {
            var content = Content(1);
            content = content with { Quotas = content.Quotas.Select(quota => quota with { IsStale = true }).ToArray() };
            var image = Render(content, SideStripStyle.Columns);
            Assert.Empty(ColumnsWithColor(image, (87, 119, 217), fromRow: 340));
            Assert.NotEmpty(ColumnsWithColor(image, (145, 145, 145), fromRow: 340));
            return true;
        });
    }

    [Theory]
    [InlineData("fr", "5h", "7j")]
    [InlineData("en", "5h", "7d")]
    [InlineData("qps-Ploc", "5h", "7j")]
    public async Task Colonnes_NomsCourts_SuiventLeTypeDeFenetre(string culture, string session, string week)
    {
        Loc.SetCulture(CultureInfo.GetCultureInfo(culture));
        await OnStaThread(() =>
        {
            var content = Content(1);
            content = content with { Quotas = content.Quotas.Select(quota => quota with
                { WindowLabel = "Nom fourni", ProviderName = "Nom fournisseur" }).ToArray() };
            var texts = CollectTexts(content, SideStripStyle.Columns);
            Assert.Equal(new[] { week, session },
                texts.Where(text => text.Zone.Y > 300).Select(text => text.Text));
            var hero = CollectTexts(content, SideStripStyle.HeroStack);
            Assert.Contains(hero, text => text.SourceText == "Nom fourni");
            Assert.Contains(hero, text => text.SourceText == "Nom fournisseur");
            return true;
        });
    }

    [Fact]
    public async Task PileHero_RemiseAZeroNulle_AucunTexteDeRemiseAZero()
    {
        await OnStaThread(() =>
        {
            var quota = Content(1).Quotas[0];
            var dated = CollectTexts(new SideStripContent([quota], 23, 61), SideStripStyle.HeroStack);
            var undated = CollectTexts(new SideStripContent([quota with { ResetsAt = null }], 23, 61), SideStripStyle.HeroStack);
            string reset = new DateTime(2026, 11, 2, 14, 0, 0).ToString(Loc.T("Usage_DayTimeFormat"), Loc.Formatting);
            Assert.Contains(dated, text => text.SourceText == reset);
            Assert.DoesNotContain(undated, text => text.SourceText == reset);
            Assert.Equal(undated.Select(text => text.SourceText),
                dated.Where(text => text.SourceText != reset).Select(text => text.SourceText));
            return true;
        });
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public async Task Rendus_MesureAbsente_TiretReellementDessine(SideStripStyle style)
    {
        await OnStaThread(() =>
        {
            var missing = CollectTexts(new SideStripContent([], null, null), style);
            Assert.Contains(missing, text => text.Text == "—");
            Assert.DoesNotContain(missing, text => text.Text == "0" || text.Text == "0%");
            Assert.All(missing.Where(text => text.Text == "—"), text =>
            {
                Assert.True(text.MeasuredSize.Width > 0);
                Assert.True(text.MeasuredSize.Height > 0);
            });
            return true;
        });
    }

    [Fact]
    public async Task Anneaux_SansQuota_UnSeulCadranPcAgranditAvecLaHauteur()
    {
        await OnStaThread(() =>
        {
            var content = new SideStripContent([], 23, 61);
            var texts = CollectTexts(content, SideStripStyle.Rings);
            Assert.Equal(new[] { "23%", "61%", "CPU / RAM" }, texts.Select(text => text.Text));
            var compact = CollectTexts(content, SideStripStyle.Rings, height: 80);
            Assert.True(texts[0].FontSize > compact[0].FontSize);
            var image = Render(new SideStripContent([], 0, 0), SideStripStyle.Rings);
            var trackPixels = Enumerable.Range(0, image.Width * image.Height)
                .Where(index => PixelAt(image, index % image.Width, index / image.Width) == ((byte)50, (byte)43, (byte)40))
                .Select(index => index / image.Width).Distinct().OrderBy(y => y).ToArray();
            Assert.NotEmpty(trackPixels);
            Assert.InRange(trackPixels.Last() - trackPixels.First(), 60, 72);
            Assert.True(trackPixels.First() > 100);
            Assert.True(trackPixels.Last() < 250);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(90)]
    [InlineData(100)]
    public async Task Reservoir_PourcentageDansLeLiquideOuAuDessusSansCacherLaSurface(int percent)
    {
        await OnStaThread(() =>
        {
            var quota = Content(1).Quotas[0] with { UsedPct = percent };
            var texts = CollectTexts(new SideStripContent([quota], null, null), SideStripStyle.Reservoir);
            var values = texts.Where(text => text.SourceText == percent.ToString(Loc.Formatting) + "%").ToArray();
            var value = Assert.Single(values);
            double bottom = 3 + 374 * 0.68;
            double level = bottom - 374 * 0.68 * percent / 100;
            Assert.True(value.Zone.Bottom <= bottom);
            Assert.True(value.Zone.Top > level || value.Zone.Bottom < level);
            if (percent >= 50)
                Assert.True(value.Zone.Top > level);
            else if (percent <= 10)
                Assert.True(value.Zone.Bottom < level);
            foreach (var text in texts.Where(text => text.Zone.Top < bottom))
                Assert.True(text.Zone.Top > level || text.Zone.Bottom < level);
            return true;
        });
    }

    [Theory]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(75)]
    public async Task Reservoir_NiveauCalculeDansLaPartieHaute(int percent)
    {
        await OnStaThread(() =>
        {
            var quota = Content(1).Quotas[0] with { UsedPct = percent };
            var image = Render(new SideStripContent([quota], null, null), SideStripStyle.Reservoir);
            int level = (int)(3 + 374 * 0.68 * (1 - percent / 100d));
            Assert.Equal(((byte)50, (byte)43, (byte)40), PixelAt(image, x: 5, y: level - 6));
            var liquid = PixelAt(image, x: 5, y: level + 6);
            Assert.InRange(liquid.Blue, (byte)51, (byte)105);
            Assert.InRange(liquid.Green, (byte)44, (byte)145);
            Assert.InRange(liquid.Red, (byte)41, (byte)239);
            return true;
        });
    }

    [Fact]
    public async Task Reservoir_PastillesSousLeReservoir_ContoursVisiblesMemeVides()
    {
        await OnStaThread(() =>
        {
            var quota = Content(1).Quotas[0] with { UsedPct = 100 };
            var content = new SideStripContent([quota], null, null);
            var image = Render(content, SideStripStyle.Reservoir);
            Assert.Equal(Background, PixelAt(image, x: 37, y: 270));
            Assert.Equal(Background, PixelAt(image, x: 37, y: 350));
            for (int index = 0; index < 4; index++)
            {
                int left = index % 2 == 0 ? 3 : 39;
                int top = index < 2 ? 265 : 321;
                var pixels = Enumerable.Range(top, 40).SelectMany(y => Enumerable.Range(left, 30)
                    .Select(x => PixelAt(image, x, y))).ToArray();
                Assert.Contains(((byte)184, (byte)172, (byte)166), pixels);
            }
            var texts = CollectTexts(content, SideStripStyle.Reservoir);
            var missing = texts.Where(text => text.Text == "—").ToArray();
            Assert.Equal(2, missing.Length);
            Assert.All(missing, text => Assert.True(text.Zone.Top > 3 + 374 * 0.68));
            return true;
        });
    }

    [Theory]
    [InlineData(158, "reset 2h", "5h·2h")]
    [InlineData(61, "reset 1h", "5h·1h")]
    [InlineData(60, "reset 1h", "5h·1h")]
    [InlineData(38, "reset 38m", "5h·38m")]
    [InlineData(0.5, "reset 1m", "5h·1m")]
    public async Task Reservoir_FenetreCinqHeures_TempsRestantEnHeuresPuisMinutes(double minutes, string hero, string badge)
    {
        await OnStaThread(() =>
        {
            var remaining = TimeSpan.FromMinutes(minutes);
            var session = new QuotaGauge("claude", "Claude", "Session", 70, IsAlert: false, IsStale: false,
                ResetsAt: null, WindowKind: UsageWindowKind.Session, ResetIn: remaining);
            var week = session with { UsedPct = 40, WindowKind = UsageWindowKind.Week, WindowLabel = "Semaine" };

            var heroTexts = CollectTexts(new SideStripContent([session, week], 23, 61), SideStripStyle.Reservoir);
            Assert.Contains(heroTexts, text => text.Text == hero);
            Assert.DoesNotContain(heroTexts, text => text.Text.Contains('·'));

            var badgeTexts = CollectTexts(new SideStripContent([week with { UsedPct = 80 }, session], 23, 61),
                SideStripStyle.Reservoir);
            Assert.Contains(badgeTexts, text => text.Text == badge);
            Assert.DoesNotContain(badgeTexts, text => text.Text.StartsWith("reset"));
            return true;
        });
    }

    [Fact]
    public async Task Reservoir_SemaineOuRemiseInconnue_AucunTempsRestant()
    {
        await OnStaThread(() =>
        {
            var week = new QuotaGauge("claude", "Claude", "Semaine", 70, IsAlert: false, IsStale: false,
                ResetsAt: null, WindowKind: UsageWindowKind.Week, ResetIn: TimeSpan.FromHours(30));
            var session = week with { UsedPct = 40, WindowKind = UsageWindowKind.Session, ResetIn = null };
            var texts = CollectTexts(new SideStripContent([week, session], 23, 61), SideStripStyle.Reservoir);
            Assert.DoesNotContain(texts, text => text.Text.StartsWith("reset") || text.Text.Contains('·'));
            return true;
        });
    }

    private static List<SideStripRenderer.DrawnText> CollectTexts(SideStripContent content, SideStripStyle style, int height = 380)
    {
        var texts = new List<SideStripRenderer.DrawnText>();
        SideStripRenderer.TextDrawn = texts.Add;
        try
        {
            SideStripRenderer.Render(content, style, width: 75, height: height);
            return texts;
        }
        finally
        {
            SideStripRenderer.TextDrawn = null;
        }
    }

    private static IEnumerable<(string Name, SideStripContent Content)> Cases()
    {
        for (int count = 0; count <= 3; count++)
            yield return ($"providers-{count}", Content(count));
        var content = Content(1);
        yield return ("single-window", content with { Quotas = content.Quotas.Take(1).ToArray() });
        yield return ("stale", content with { Quotas = content.Quotas.Select(q => q with { IsStale = true, IsAlert = true }).ToArray() });
        yield return ("alert", content with { Quotas = content.Quotas.Select(q => q with { IsAlert = true }).ToArray() });
        yield return ("no-reset", content with { Quotas = content.Quotas.Select(q => q with { ResetsAt = null }).ToArray() });
        yield return ("session-reset", content with { Quotas = content.Quotas
            .Select(q => q with { UsedPct = q.WindowKind == UsageWindowKind.Session ? 81 : 40, ResetIn = TimeSpan.FromMinutes(158) })
            .OrderByDescending(q => q.UsedPct).ToArray() });
        yield return ("session-reset-badge", content with { Quotas = content.Quotas
            .Select(q => q with { ResetIn = TimeSpan.FromMinutes(38) }).ToArray() });
        yield return ("no-cpu", content with { CpuPct = null });
        yield return ("no-measures", new SideStripContent([], null, null));
        yield return ("cpu-hero", new SideStripContent([], 83, 12));
        yield return ("empty", new SideStripContent([], 0, 0));
        yield return ("full", new SideStripContent([content.Quotas[0] with { UsedPct = 100 }], 100, 100));
        foreach (int percent in new[] { 1, 10, 50, 75, 90 })
            yield return ($"level-{percent}", content with { Quotas = [content.Quotas[0] with { UsedPct = percent }] });
        yield return ("five-full-columns", Content(2) with
            { Quotas = Content(2).Quotas.Select(q => q with { UsedPct = 100 }).ToArray(), CpuPct = 100, MemoryPct = 100 });
        yield return ("long-names", Content(3) with { Quotas = Content(3).Quotas.Select(q => q with
            { ProviderName = new string('W', 60), WindowLabel = new string('M', 60) }).ToArray() });
    }

    private static SideStripContent Content(int providerCount)
    {
        string[] providers = ["claude", "codex", "antigravity"];
        var quotas = providers.Take(providerCount).SelectMany((provider, index) => new[]
        {
            new QuotaGauge(provider, provider, Loc.T("StreamDock_Week"), 68 - index * 20,
                IsAlert: false, IsStale: false, ResetsAt: new DateTime(2026, 11, 2, 14, 0, 0), WindowKind: UsageWindowKind.Week),
            new QuotaGauge(provider, provider, Loc.T("StreamDock_Session"), 62 - index * 20,
                IsAlert: false, IsStale: false, ResetsAt: null, WindowKind: UsageWindowKind.Session),
        }).ToArray();
        return new SideStripContent(quotas, 23, 61);
    }

    private static SurfaceImage Render(SideStripContent content, SideStripStyle style) =>
        SideStripRenderer.Render(content, style, width: 75, height: 380);

    private static (byte Blue, byte Green, byte Red) PixelAt(SurfaceImage image, int x, int y)
    {
        int offset = (y * image.Width + x) * 4;
        return (image.Bgra32[offset], image.Bgra32[offset + 1], image.Bgra32[offset + 2]);
    }

    private static int[] ColumnsWithColor(SurfaceImage image, (byte Blue, byte Green, byte Red) color, int fromRow) =>
        Enumerable.Range(fromRow, image.Height - fromRow)
            .SelectMany(y => Enumerable.Range(0, image.Width).Where(x => PixelAt(image, x, y) == color))
            .Distinct().ToArray();

    private static int CountColor(SurfaceImage image, (byte Blue, byte Green, byte Red) color) =>
        Enumerable.Range(0, image.Width * image.Height)
            .Where(index => PixelAt(image, index % image.Width, index / image.Width) == color).Count();

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
        return await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
