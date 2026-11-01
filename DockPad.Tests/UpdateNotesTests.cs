using DockPad.Services.Updates;

namespace DockPad.Tests;

public class UpdateNotesTests
{
    [Fact]
    public void ReleaseHeadingBecomesADateAndHeadingsListsAndParagraphsRemainStructured()
    {
        var notes = UpdateNotes.Parse("""
            ## [1.32.0] — 2026-11-01
            ## Added
            ### Updates
            First line
            continues here.

            - One **important** change with `--mcp`.
              - Nested change
                - Deeply nested change
            """);
        Assert.Equal(new DateOnly(2026, 11, 1), notes.Date);
        Assert.Equal(new[] { UpdateNoteKind.Heading, UpdateNoteKind.Subheading, UpdateNoteKind.Paragraph,
            UpdateNoteKind.Bullet, UpdateNoteKind.Bullet, UpdateNoteKind.Bullet }, notes.Blocks.Select(b => b.Kind));
        Assert.Equal("First line continues here.", Assert.Single(notes.Blocks[2].Inlines).Text);
        Assert.Equal(new[] { 0, 2, 4 }, notes.Blocks.Skip(3).Select(b => b.Indent));
        Assert.Contains(notes.Blocks[3].Inlines, i => i.Kind == UpdateInlineKind.Bold && i.Text == "important");
        Assert.Contains(notes.Blocks[3].Inlines, i => i.Kind == UpdateInlineKind.Code && i.Text == "--mcp");
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\r\n")]
    public void EmptyNotesHaveNoPlaceholderContent(string markdown)
    {
        var notes = UpdateNotes.Parse(markdown);
        Assert.Null(notes.Date);
        Assert.Empty(notes.Blocks);
    }

    [Fact]
    public void MissingOrInvalidDatesAreNotInvented()
    {
        Assert.Null(UpdateNotes.Parse("### Fixes\n- Changes").Date);
        var invalid = UpdateNotes.Parse("## [1.0.0] — 2026-02-30\nChanges");
        Assert.Null(invalid.Date);
        Assert.Single(invalid.Blocks);
    }

    [Fact]
    public void OnlyPairedMarkdownDelimitersAreRemoved()
    {
        var notes = UpdateNotes.Parse("## Heading\r\n\r\n- **Bold** and `code` and *stray* and #tag and `unfinished");
        var text = string.Concat(notes.Blocks.SelectMany(b => b.Inlines).Select(i => i.Text));
        Assert.Equal("HeadingBold and code and stray and #tag and `unfinished", text);
    }

    [Theory]
    [InlineData("`p'; commande; #`", "p'; commande; #")]
    [InlineData("C# et **C#**", "C# et C#")]
    [InlineData("`# * ** code`", "# * ** code")]
    [InlineData("*italique* et **gras**", "italique et gras")]
    [InlineData("[texte](https://example.test)", "texte")]
    [InlineData("[texte](javascript:alert(1))", "texte")]
    public void ChangelogTextKeepsLiteralCharactersAndLinkLabels(string markdown, string expected) =>
        Assert.Equal(expected, string.Concat(UpdateNotes.ParseInlines(markdown).Select(i => i.Text)));

    [Fact]
    public void LeadingBomDoesNotHideTheReleaseHeading()
    {
        var notes = UpdateNotes.Parse("\uFEFF## [1.30.1] — 2026-10-01\n- `p'; commande; #`");
        Assert.Equal(new DateOnly(2026, 10, 1), notes.Date);
        Assert.Single(notes.Blocks);
        Assert.Equal("p'; commande; #", Assert.Single(notes.Blocks[0].Inlines).Text);
    }

    [Fact]
    public void ConsecutiveInlineSpansKeepPunctuationAndTheFinalParagraph()
    {
        var notes = UpdateNotes.Parse("**One**`two`!\n\nFinal paragraph.");
        Assert.Equal(new[] { UpdateInlineKind.Bold, UpdateInlineKind.Code, UpdateInlineKind.Text }, notes.Blocks[0].Inlines.Select(i => i.Kind));
        Assert.Equal("Final paragraph.", Assert.Single(notes.Blocks[1].Inlines).Text);
    }
}
