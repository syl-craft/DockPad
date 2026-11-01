using System.Globalization;
using System.Text.RegularExpressions;

namespace DockPad.Services.Updates;

public enum UpdateNoteKind { Paragraph, Heading, Subheading, Bullet }
public enum UpdateInlineKind { Text, Bold, Code, Italic }
public sealed record UpdateNoteInline(string Text, UpdateInlineKind Kind);
public sealed record UpdateNoteBlock(UpdateNoteKind Kind, int Indent, IReadOnlyList<UpdateNoteInline> Inlines);
public sealed record UpdateNotes(DateOnly? Date, IReadOnlyList<UpdateNoteBlock> Blocks)
{
    // Le titre de version est une métadonnée, jamais un titre des notes affichées.
    private static readonly Regex ReleaseHeading = new(@"^##\s+\[[^\]]+\]\s*[—–-]\s*(\d{4}-\d{2}-\d{2})\s*$");
    private static readonly Regex Bullet = new(@"^(\s*)-\s+(.*)$");
    private static readonly Regex Inline = new(@"`(?<code>[^`]+)`|\*\*(?<bold>.+?)\*\*|(?<!\*)\*(?<italic>[^*]+)\*(?!\*)|\[(?<link>[^\]]+)\]\((?:[^()\r\n]|\([^()\r\n]*\))*\)");

    public static UpdateNotes Parse(string markdown)
    {
        DateOnly? date = null;
        var blocks = new List<UpdateNoteBlock>();
        var paragraph = new List<string>();
        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new(UpdateNoteKind.Paragraph, 0, ParseInlines(string.Join(" ", paragraph))));
            paragraph.Clear();
        }

        foreach (var line in markdown.TrimStart('\uFEFF').Replace("\r", "").Split('\n'))
        {
            var trimmed = line.Trim();
            var release = ReleaseHeading.Match(trimmed);
            if (release.Success)
            {
                FlushParagraph();
                if (date == null && DateOnly.TryParseExact(release.Groups[1].Value, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) date = parsed;
                continue;
            }
            if (trimmed.Length == 0) { FlushParagraph(); continue; }
            var bullet = Bullet.Match(line);
            if (bullet.Success)
            {
                FlushParagraph();
                blocks.Add(new(UpdateNoteKind.Bullet, bullet.Groups[1].Value.Replace("\t", "    ").Length,
                    ParseInlines(bullet.Groups[2].Value)));
                continue;
            }
            if (trimmed.StartsWith("##", StringComparison.Ordinal))
            {
                FlushParagraph();
                blocks.Add(new(trimmed.StartsWith("###", StringComparison.Ordinal)
                    ? UpdateNoteKind.Subheading : UpdateNoteKind.Heading, 0, ParseInlines(trimmed.TrimStart('#', ' '))));
                continue;
            }
            paragraph.Add(trimmed);
        }
        FlushParagraph();
        return new(date, blocks);
    }

    public static IReadOnlyList<UpdateNoteInline> ParseInlines(string text)
    {
        var result = new List<UpdateNoteInline>();
        var offset = 0;
        foreach (Match match in Inline.Matches(text))
        {
            if (match.Index > offset) result.Add(new(text[offset..match.Index], UpdateInlineKind.Text));
            if (match.Groups["code"].Success) result.Add(new(match.Groups["code"].Value, UpdateInlineKind.Code));
            else if (match.Groups["bold"].Success) result.Add(new(match.Groups["bold"].Value, UpdateInlineKind.Bold));
            else if (match.Groups["italic"].Success) result.Add(new(match.Groups["italic"].Value, UpdateInlineKind.Italic));
            else result.AddRange(ParseInlines(match.Groups["link"].Value));
            offset = match.Index + match.Length;
        }
        if (offset < text.Length) result.Add(new(text[offset..], UpdateInlineKind.Text));
        return result;
    }
}
