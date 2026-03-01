using Chaos.Client;
using Xunit;

namespace Chaos.Client.Tests;

/// <summary>
/// Verifies the cursor-position detection helpers that drive toolbar button highlighting.
/// All methods under test live in FormatDetection (pure string/regex — no WPF, no STA needed).
/// </summary>
public class FormatDetectionTests
{
    // ── IsCursorInSpan — Bold ─────────────────────────────────────────────────

    // IsCursorInSpan uses inclusive boundaries: pos >= markerEnd AND pos <= closeMarkerStart.
    // Cursor at pos=2 in "**bold**" is right after the opening **, which counts as inside.
    // Cursor at pos=6 is right before the closing **, which also counts as inside.
    [Theory]
    [InlineData("**bold**", 1,  false)] // on opening marker (between * and *)
    [InlineData("**bold**", 2,  true)]  // right after opening ** — inside
    [InlineData("**bold**", 3,  true)]  // content char
    [InlineData("**bold**", 6,  true)]  // right before closing ** — inside
    [InlineData("**bold**", 7,  false)] // between closing * * — outside
    [InlineData("**bold**", 8,  false)] // after span
    [InlineData("no bold",  3,  false)] // no span at all
    public void Bold_CursorDetection(string text, int pos, bool expected)
    {
        Assert.Equal(expected,
            FormatDetection.IsCursorInSpan(text, pos, FormatDetection.BoldSpan, 2));
    }

    [Fact]
    public void Bold_MultilineSingleSpan_DetectedAcrossLines()
    {
        string text = "**line1\nline2**";
        // cursor in the middle of the span (after the \n)
        Assert.True(FormatDetection.IsCursorInSpan(text, 8, FormatDetection.BoldSpan, 2));
    }

    // ── IsCursorInSpan — Underline ────────────────────────────────────────────

    [Theory]
    [InlineData("__ul__", 2,  true)]
    [InlineData("__ul__", 0,  false)] // on marker
    [InlineData("__ul__", 5,  false)] // on closing marker
    [InlineData("no ul",  2,  false)]
    public void Underline_CursorDetection(string text, int pos, bool expected)
    {
        Assert.Equal(expected,
            FormatDetection.IsCursorInSpan(text, pos, FormatDetection.UnderlineSpan, 2));
    }

    [Fact]
    public void Underline_MultilineSpan_Detected()
    {
        string text = "__line1\nline2__";
        Assert.True(FormatDetection.IsCursorInSpan(text, 8, FormatDetection.UnderlineSpan, 2));
    }

    // ── IsCursorInSpan — Strikethrough ────────────────────────────────────────

    [Theory]
    [InlineData("~~st~~", 2, true)]
    [InlineData("~~st~~", 0, false)]
    [InlineData("~~st~~", 5, false)]
    public void Strike_CursorDetection(string text, int pos, bool expected)
    {
        Assert.Equal(expected,
            FormatDetection.IsCursorInSpan(text, pos, FormatDetection.StrikeSpan, 2));
    }

    [Fact]
    public void Strike_MultilineSpan_Detected()
    {
        string text = "~~line1\nline2~~";
        Assert.True(FormatDetection.IsCursorInSpan(text, 8, FormatDetection.StrikeSpan, 2));
    }

    // ── IsCursorInSpan — Code ─────────────────────────────────────────────────

    [Theory]
    [InlineData("`code`", 1, true)]  // first content char
    [InlineData("`code`", 0, false)] // on opening backtick
    [InlineData("`code`", 5, true)]  // right before closing backtick — inside
    [InlineData("`code`", 6, false)] // after closing backtick
    public void Code_CursorDetection(string text, int pos, bool expected)
    {
        Assert.Equal(expected,
            FormatDetection.IsCursorInSpan(text, pos, FormatDetection.CodeSpan, 1));
    }

    // ── IsCursorInItalicSpan ──────────────────────────────────────────────────

    [Theory]
    [InlineData("*italic*", 1, true)]   // first char of content
    [InlineData("*italic*", 7, true)]   // last char of content
    [InlineData("*italic*", 0, false)]  // on opening *
    [InlineData("*italic*", 8, false)]  // after closing *
    [InlineData("no star",  3, false)]  // no italic
    public void Italic_CursorDetection(string text, int pos, bool expected)
    {
        Assert.Equal(expected, FormatDetection.IsCursorInItalicSpan(text, pos));
    }

    [Fact]
    public void Italic_MultilineSpan_Detected()
    {
        string text = "*line1\nline2*";
        Assert.True(FormatDetection.IsCursorInItalicSpan(text, 7));
    }

    [Fact]
    public void Italic_InsideBold_Detected()
    {
        // **bold *italic* bold** — cursor inside the inner italic
        string text = "**bold *italic* bold**";
        // cursor at index 8 ('i' in italic)
        Assert.True(FormatDetection.IsCursorInItalicSpan(text, 8));
    }

    [Fact]
    public void Italic_DelimitersOfBoldNotTreatedAsItalic()
    {
        // Bold span **text** — the ** chars must not be treated as italic markers
        string text = "**text**";
        // cursor in the middle of "text"
        Assert.False(FormatDetection.IsCursorInItalicSpan(text, 4));
    }

    [Fact]
    public void BoldItalic_CursorInsideTripleStar_DetectedAsBothBoldAndItalic()
    {
        string text = "***both***";
        int pos = 5; // inside content
        bool inBoldItalic = FormatDetection.IsCursorInSpan(text, pos, FormatDetection.BoldItalicSpan, 3);
        Assert.True(inBoldItalic);
        // When bold+italic, bold button is true (inBoldItalic) and italic button is true
        bool italic = inBoldItalic || FormatDetection.IsCursorInItalicSpan(text, pos);
        Assert.True(italic);
    }

    // ── IsLineInFencedBlock ───────────────────────────────────────────────────

    [Fact]
    public void FencedBlock_LineInsideBlock_ReturnsTrue()
    {
        string text = "```\nsome code\n```";
        // lineStartPos of "some code" is 4
        Assert.True(FormatDetection.IsLineInFencedBlock(text, 4));
    }

    [Fact]
    public void FencedBlock_LineOutsideBlock_ReturnsFalse()
    {
        string text = "```\nsome code\n```\nafter";
        // lineStartPos of "after" is 18
        Assert.False(FormatDetection.IsLineInFencedBlock(text, 18));
    }

    [Fact]
    public void FencedBlock_LineBeforeFence_ReturnsFalse()
    {
        string text = "before\n```\ncode\n```";
        // lineStartPos of "before" is 0
        Assert.False(FormatDetection.IsLineInFencedBlock(text, 0));
    }

    // ── Format combination detection ─────────────────────────────────────────
    //
    // DetectFormats mirrors UpdateFormatButtonStates logic — checks each format
    // independently and returns which ones are active at the given cursor position.

    private static (bool bold, bool italic, bool underline, bool strike) DetectFormats(string text, int pos)
    {
        bool inBoldItalic = FormatDetection.IsCursorInSpan(text, pos, FormatDetection.BoldItalicSpan, 3);
        return (
            bold:      inBoldItalic || FormatDetection.IsCursorInSpan(text, pos, FormatDetection.BoldSpan, 2),
            italic:    inBoldItalic || FormatDetection.IsCursorInItalicSpan(text, pos),
            underline: FormatDetection.IsCursorInSpan(text, pos, FormatDetection.UnderlineSpan, 2),
            strike:    FormatDetection.IsCursorInSpan(text, pos, FormatDetection.StrikeSpan, 2)
        );
    }

    [Fact]
    public void Combination_BoldUnderline_BothDetected()
    {
        // **__text__** — outer bold, inner underline
        var (bold, italic, underline, strike) = DetectFormats("**__text__**", 6);
        Assert.True(bold);
        Assert.False(italic);
        Assert.True(underline);
        Assert.False(strike);
    }

    [Fact]
    public void Combination_BoldStrike_BothDetected()
    {
        // **~~text~~** — outer bold, inner strike
        var (bold, italic, underline, strike) = DetectFormats("**~~text~~**", 6);
        Assert.True(bold);
        Assert.False(italic);
        Assert.False(underline);
        Assert.True(strike);
    }

    [Fact]
    public void Combination_ItalicUnderline_BothDetected()
    {
        // *__text__* — outer italic, inner underline
        var (bold, italic, underline, strike) = DetectFormats("*__text__*", 5);
        Assert.False(bold);
        Assert.True(italic);
        Assert.True(underline);
        Assert.False(strike);
    }

    [Fact]
    public void Combination_ItalicStrike_BothDetected()
    {
        // *~~text~~* — outer italic, inner strike
        var (bold, italic, underline, strike) = DetectFormats("*~~text~~*", 5);
        Assert.False(bold);
        Assert.True(italic);
        Assert.False(underline);
        Assert.True(strike);
    }

    [Fact]
    public void Combination_UnderlineStrike_BothDetected()
    {
        // __~~text~~__ — outer underline, inner strike
        var (bold, italic, underline, strike) = DetectFormats("__~~text~~__", 6);
        Assert.False(bold);
        Assert.False(italic);
        Assert.True(underline);
        Assert.True(strike);
    }

    [Fact]
    public void Combination_BoldUnderlineStrike_AllThreeDetected()
    {
        // **__~~text~~__** — bold > underline > strike
        var (bold, italic, underline, strike) = DetectFormats("**__~~text~~__**", 8);
        Assert.True(bold);
        Assert.False(italic);
        Assert.True(underline);
        Assert.True(strike);
    }

    [Fact]
    public void Combination_BoldItalicUnderline_AllThreeDetected()
    {
        // ***__text__*** — bold+italic > underline
        var (bold, italic, underline, strike) = DetectFormats("***__text__***", 7);
        Assert.True(bold);
        Assert.True(italic);
        Assert.True(underline);
        Assert.False(strike);
    }

    [Fact]
    public void Combination_BoldItalicStrike_AllThreeDetected()
    {
        // ***~~text~~*** — bold+italic > strike
        var (bold, italic, underline, strike) = DetectFormats("***~~text~~***", 7);
        Assert.True(bold);
        Assert.True(italic);
        Assert.False(underline);
        Assert.True(strike);
    }

    [Fact]
    public void Combination_AllFour_AllDetected()
    {
        // ***__~~text~~__*** — bold+italic > underline > strike
        var (bold, italic, underline, strike) = DetectFormats("***__~~text~~__***", 9);
        Assert.True(bold);
        Assert.True(italic);
        Assert.True(underline);
        Assert.True(strike);
    }

    [Fact]
    public void Combination_CursorOutsideAllSpans_NoneDetected()
    {
        // Cursor after the closing markers — nothing active
        string text = "**bold** plain";
        var (bold, italic, underline, strike) = DetectFormats(text, 10);
        Assert.False(bold);
        Assert.False(italic);
        Assert.False(underline);
        Assert.False(strike);
    }

    [Fact]
    public void Combination_BoldOnly_OthersNotDetected()
    {
        // Cursor inside bold but outside any inner span
        var (bold, italic, underline, strike) = DetectFormats("**text**", 4);
        Assert.True(bold);
        Assert.False(italic);
        Assert.False(underline);
        Assert.False(strike);
    }

    // ── MaskBoldDelimiters ────────────────────────────────────────────────────

    [Fact]
    public void MaskBoldDelimiters_MasksOpeningAndClosingStars()
    {
        string text = "**bold**";
        bool[] masked = FormatDetection.MaskBoldDelimiters(text);
        Assert.True(masked[0]);  // first *
        Assert.True(masked[1]);  // second *
        Assert.False(masked[2]); // 'b' — content not masked
        Assert.True(masked[6]);  // closing *
        Assert.True(masked[7]);  // closing *
    }

    [Fact]
    public void MaskBoldDelimiters_BoldItalic_MasksThreeCharsEachSide()
    {
        string text = "***bi***";
        bool[] masked = FormatDetection.MaskBoldDelimiters(text);
        Assert.True(masked[0]);
        Assert.True(masked[1]);
        Assert.True(masked[2]);
        Assert.False(masked[3]); // 'b' — content
        Assert.True(masked[5]);
        Assert.True(masked[6]);
        Assert.True(masked[7]);
    }

    [Fact]
    public void MaskBoldDelimiters_DoesNotMaskContentChars()
    {
        string text = "**hello world**";
        bool[] masked = FormatDetection.MaskBoldDelimiters(text);
        // content indices 2..12 should all be false
        for (int i = 2; i <= 12; i++)
            Assert.False(masked[i]);
    }

    [Fact]
    public void Italic_InsideBoldItalicTriple_NotFalselyMasked()
    {
        // ***bold italic*** — the lone * inside would be at position... well there aren't lone *s here.
        // But **bold *italic* end** — the inner * (at index 7) must NOT be masked.
        string text = "**bold *italic* end**";
        bool[] masked = FormatDetection.MaskBoldDelimiters(text);
        Assert.False(masked[7]);  // opening lone * of inner italic
        Assert.False(masked[14]); // closing lone * of inner italic
    }
}
