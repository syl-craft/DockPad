using System.Globalization;
using System.IO;
using System.Text;
using DockPad.Surfaces;

namespace StreamDockProbe;

internal sealed class ProbeReport(bool automatic)
{
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
    public bool Automatic { get; } = automatic;
    public SurfaceInfo? Surface { get; set; }
    public string Outcome { get; set; } = "Essai interrompu";
    public bool IsComplete { get; set; }
    public string? Failure { get; set; }
    public int NoiseJpegBytes { get; set; }
    public int NoiseChunks { get; set; }
    public int PressCount { get; set; }
    public int ReleaseCount { get; set; }
    public List<double> PressCommitMilliseconds { get; } = [];
    public List<(int Step, string Label, TimeSpan Elapsed)> Durations { get; } = [];

    public List<ProbeCheck> Checks { get; } =
    [
        new(1, "Les numéros sont-ils dans le bon ordre et à l'endroit ?"),
        new(1, "La colonne de droite est-elle continue, sans bande noire, avec le cadre vert entier ?"),
        new(2, "Image entière, sans bande grise ?"),
        new(3, "Rien n'a changé avant le Commit ?"),
        new(3, "Elle apparaît maintenant ?"),
        new(4, "Les positions écrites correspondent aux touches appuyées ?"),
        new(5, "Le dock est toujours allumé ?"),
        new(5, "Seule la touche L1 C1 est effacée ?"),
        new(5, "Toutes les touches et les écrans latéraux sont effacés ?"),
        new(5, "Écran éteint ?"),
        new(6, "Réaction immédiate ?"),
    ];

    public bool IsSuccessful => IsComplete && Checks.Where(check => check.Answer != "O").Any() == false;

    public string DescribeLatency()
    {
        if (PressCommitMilliseconds.Count == 0)
            return "Aucun appui mesuré.";

        var minimum = PressCommitMilliseconds.Min().ToString("F2", CultureInfo.GetCultureInfo("fr-FR"));
        var average = PressCommitMilliseconds.Average().ToString("F2", CultureInfo.GetCultureInfo("fr-FR"));
        var maximum = PressCommitMilliseconds.Max().ToString("F2", CultureInfo.GetCultureInfo("fr-FR"));
        return $"Appui → fin du Commit : {PressCommitMilliseconds.Count} mesures ; min {minimum} ms / moyenne {average} ms / max {maximum} ms.";
    }

    public void Save()
    {
        var summary = new StringBuilder();
        summary.AppendLine("# StreamDockProbe");
        summary.AppendLine();
        summary.AppendLine($"Date : {StartedAt:yyyy-MM-dd HH:mm:ss zzz}");
        summary.AppendLine($"Modèle : {Surface?.Model ?? "inconnu"}");
        summary.AppendLine($"Chemin : {Surface?.DevicePath ?? "inconnu"}");
        summary.AppendLine($"Numéro de série : {Surface?.SerialNumber ?? "non disponible"}");
        summary.AppendLine($"Mode : {(Automatic ? "automatique (réponses non vérifiées)" : "interactif")}");
        summary.AppendLine($"Résultat : {Outcome}");
        summary.AppendLine();
        summary.AppendLine("| Étape | Vérification | Réponse |");
        summary.AppendLine("| --- | --- | --- |");
        foreach (var check in Checks)
        {
            summary.AppendLine($"| {check.Step} | {check.Question} | {check.Answer} |");
        }
        summary.AppendLine();
        summary.AppendLine($"Image bruitée : 96 × 96 ; JPEG {NoiseJpegBytes} octets ; {NoiseChunks} blocs de données HID.");
        summary.AppendLine($"Écoute : {PressCount} appuis, {ReleaseCount} relâchements.");
        summary.AppendLine(DescribeLatency());
        foreach (var duration in Durations)
        {
            var seconds = duration.Elapsed.TotalSeconds.ToString("F2", CultureInfo.GetCultureInfo("fr-FR"));
            summary.AppendLine($"Étape {duration.Step} — {duration.Label} : {seconds} s.");
        }
        if (Failure != null)
        {
            summary.AppendLine();
            summary.AppendLine("Diagnostic :");
            summary.AppendLine("~~~text");
            summary.AppendLine(Failure);
            summary.AppendLine("~~~");
        }
        var markdown = summary.ToString();
        Console.WriteLine();
        Console.Write(markdown);
        var path = Path.Combine(Environment.CurrentDirectory, $"StreamDockProbe-{StartedAt:yyyy-MM-dd_HH-mm-ss-fff}.md");
        File.WriteAllText(path, markdown, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"Résumé écrit dans {path}");
    }
}

internal sealed record ProbeCheck(int Step, string Question)
{
    public string Answer { get; set; } = "?";
}
