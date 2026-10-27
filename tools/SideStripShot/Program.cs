using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockPad.Models;
using DockPad.Services.Localization;
using DockPad.Services.StreamDock;

namespace SideStripShot;

/// <summary>
/// Rend l'écran latéral du Stream Dock en PNG avec le vrai moteur, à trois fois sa taille, pour le motion design
/// tools/MotionDemo/clips/15-stream-dock.html. Chiffres fictifs, aucun relevé réel.
/// </summary>
/// <remarks>
/// Sortie par défaut : tools/MotionDemo/out/strip/{en,fr}/ — remplissage du Réservoir (fill-00 à fill-30), Codex en
/// tête (reservoir-codex) et les trois autres rendus (columns, hero, rings).
/// </remarks>
internal static class Program
{
    private const int Width = 225, Height = 1140;

    private static int Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0]
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "MotionDemo", "out", "strip"));
        var thread = new Thread(() => RenderAll(outDir));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Console.WriteLine(outDir);
        return 0;
    }

    private static void RenderAll(string outDir)
    {
        foreach (string culture in new[] { "en", "fr" })
        {
            Loc.SetCulture(CultureInfo.GetCultureInfo(culture));
            string folder = Path.Combine(outDir, culture);
            Directory.CreateDirectory(folder);

            for (int step = 0; step <= 30; step++)
            {
                double fill = step / 30d;
                Save(folder, $"fill-{step:00}", new SideStripContent(ClaudeFirst(fill),
                    (int)Math.Round(23 * fill), (int)Math.Round(61 * fill)), SideStripStyle.Reservoir);
            }

            var quotas = ClaudeFirst(1);
            Save(folder, "reservoir-codex", new SideStripContent([quotas[2], quotas[0], quotas[1]], 23, 61), SideStripStyle.Reservoir);
            Save(folder, "columns", new SideStripContent(quotas, 23, 61), SideStripStyle.Columns);
            Save(folder, "hero", new SideStripContent(quotas, 23, 61), SideStripStyle.HeroStack);
            Save(folder, "rings", new SideStripContent(quotas, 23, 61), SideStripStyle.Rings);
        }
    }

    /// <summary>
    /// Claude 5 h et 7 j puis Codex 7 j, remplis à la proportion <paramref name="fill"/> des valeurs du bandeau de démo.
    /// </summary>
    private static QuotaGauge[] ClaudeFirst(double fill) =>
    [
        Gauge("claude", "Claude", UsageWindowKind.Session, (int)Math.Round(62 * fill), TimeSpan.FromMinutes(158)),
        Gauge("claude", "Claude", UsageWindowKind.Week, (int)Math.Round(44 * fill)),
        Gauge("codex", "Codex", UsageWindowKind.Week, (int)Math.Round(27 * fill)),
    ];

    private static QuotaGauge Gauge(string providerId, string name, UsageWindowKind kind, int usedPct, TimeSpan? resetIn = null) =>
        new(providerId, name, Loc.T(kind == UsageWindowKind.Session ? "StreamDock_Session" : "StreamDock_Week"), usedPct, IsAlert: false,
            IsStale: false, ResetsAt: null, WindowKind: kind, ResetIn: resetIn);

    private static void Save(string folder, string name, SideStripContent content, SideStripStyle style)
    {
        var image = SideStripRenderer.Render(content, style, Width, Height);
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null,
            image.Bgra32, image.Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(stream);
    }
}
