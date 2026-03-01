using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Chaos.Client;
using Xunit;

namespace Chaos.Client.Tests;

/// <summary>
/// Verifies that MarkdownRenderer.Render() produces the correct WPF FlowDocument
/// structure for each supported syntax element.
/// </summary>
public class MarkdownRendererTests
{
    private static readonly Brush Text = Brushes.White;
    private static readonly Brush Link = Brushes.CornflowerBlue;

    // WPF DependencyObjects must be both created AND accessed on the same STA thread.
    // Each test passes its entire body (render + assert) into AssertOnSta so the
    // FlowDocument is never accessed cross-thread.
    private static void AssertOnSta(Action action)
    {
        Exception? ex = null;
        var t = new Thread(() => { try { action(); } catch (Exception e) { ex = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (ex != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
    }

    private static Paragraph Para(FlowDocument doc, int index = 0) =>
        (Paragraph)doc.Blocks.ElementAt(index);

    private static Run FirstRun(Paragraph para) =>
        para.Inlines.OfType<Run>().First();

    // Extracts the code TextBlock from a BlockUIContainer produced by MakeCodeBlock.
    // Structure: BlockUIContainer → Border → Grid → Children[2] (code TextBlock)
    private static TextBlock CodeTextBlock(Block block)
    {
        var buc    = (BlockUIContainer)block;
        var border = (Border)buc.Child;
        var grid   = (Grid)border.Child;
        return (TextBlock)grid.Children[2];
    }

    private static TextBlock LineNumTextBlock(Block block)
    {
        var buc    = (BlockUIContainer)block;
        var border = (Border)buc.Child;
        var grid   = (Grid)border.Child;
        return (TextBlock)grid.Children[0];
    }

    // ── Plain text ────────────────────────────────────────────────────────────

    [Fact]
    public void PlainText_SingleParagraph_WithCorrectText() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("hello world", Text, Link);
        Assert.Single(doc.Blocks);
        Assert.Equal("hello world", FirstRun(Para(doc)).Text);
    });

    [Fact]
    public void EmptyString_ReturnsEmptyDocument() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("", Text, Link);
        Assert.Empty(doc.Blocks);
    });

    // ── Bold ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Bold_RunHasBoldFontWeight() => AssertOnSta(() =>
    {
        var run = FirstRun(Para(MarkdownRenderer.Render("**bold**", Text, Link)));
        Assert.Equal(FontWeights.Bold, run.FontWeight);
    });

    [Fact]
    public void Bold_ContentTextIsCorrect() => AssertOnSta(() =>
    {
        var run = FirstRun(Para(MarkdownRenderer.Render("**hello**", Text, Link)));
        Assert.Equal("hello", run.Text);
    });

    // ── Italic ────────────────────────────────────────────────────────────────

    [Fact]
    public void Italic_RunHasItalicFontStyle() => AssertOnSta(() =>
    {
        var run = FirstRun(Para(MarkdownRenderer.Render("*italic*", Text, Link)));
        Assert.Equal(FontStyles.Italic, run.FontStyle);
    });

    // ── Bold + Italic ─────────────────────────────────────────────────────────

    [Fact]
    public void BoldItalic_RunHasBothBoldAndItalic() => AssertOnSta(() =>
    {
        var run = FirstRun(Para(MarkdownRenderer.Render("***both***", Text, Link)));
        Assert.Equal(FontWeights.Bold,  run.FontWeight);
        Assert.Equal(FontStyles.Italic, run.FontStyle);
    });

    // ── Underline ─────────────────────────────────────────────────────────────

    [Fact]
    public void Underline_RunHasUnderlineDecoration() => AssertOnSta(() =>
    {
        var run = FirstRun(Para(MarkdownRenderer.Render("__underline__", Text, Link)));
        Assert.NotNull(run.TextDecorations);
        Assert.Contains(run.TextDecorations,
            d => d.Location == TextDecorationLocation.Underline);
    });

    // ── Strikethrough ─────────────────────────────────────────────────────────

    [Fact]
    public void Strike_RunHasStrikethroughDecoration() => AssertOnSta(() =>
    {
        var run = FirstRun(Para(MarkdownRenderer.Render("~~strike~~", Text, Link)));
        Assert.NotNull(run.TextDecorations);
        Assert.Contains(run.TextDecorations,
            d => d.Location == TextDecorationLocation.Strikethrough);
    });

    // ── Underline + Strike combined ───────────────────────────────────────────

    [Fact]
    public void UnderlineAndStrike_BothDecorations() => AssertOnSta(() =>
    {
        var run = FirstRun(Para(MarkdownRenderer.Render("__~~text~~__", Text, Link)));
        Assert.NotNull(run.TextDecorations);
        Assert.Contains(run.TextDecorations, d => d.Location == TextDecorationLocation.Underline);
        Assert.Contains(run.TextDecorations, d => d.Location == TextDecorationLocation.Strikethrough);
    });

    // ── Inline code ───────────────────────────────────────────────────────────

    [Fact]
    public void InlineCode_RendersInlineUIContainer() => AssertOnSta(() =>
    {
        var para = Para(MarkdownRenderer.Render("before `code` after", Text, Link));
        Assert.Contains(para.Inlines, il => il is InlineUIContainer);
    });

    // ── Multiline inline spans ────────────────────────────────────────────────

    [Fact]
    public void MultilineBold_ContainsLineBreak() => AssertOnSta(() =>
    {
        var para = Para(MarkdownRenderer.Render("**line1\nline2**", Text, Link));
        Assert.Contains(para.Inlines, il => il is LineBreak);
    });

    [Fact]
    public void MultilineBold_BothRunsAreBold() => AssertOnSta(() =>
    {
        var para = Para(MarkdownRenderer.Render("**line1\nline2**", Text, Link));
        Assert.All(para.Inlines.OfType<Run>(), r => Assert.Equal(FontWeights.Bold, r.FontWeight));
    });

    [Fact]
    public void MultilineItalic_ContainsLineBreak() => AssertOnSta(() =>
    {
        var para = Para(MarkdownRenderer.Render("*line1\nline2*", Text, Link));
        Assert.Contains(para.Inlines, il => il is LineBreak);
    });

    // ── Bullet list ───────────────────────────────────────────────────────────

    [Fact]
    public void BulletList_RendersListBlocks() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("- item1\n- item2", Text, Link);
        Assert.Equal(2, doc.Blocks.Count);
        Assert.All(doc.Blocks, b => Assert.IsType<List>(b));
    });

    // ── Numbered list ─────────────────────────────────────────────────────────

    [Fact]
    public void NumberedList_RendersListBlocks() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("1. first\n2. second", Text, Link);
        Assert.Equal(2, doc.Blocks.Count);
        Assert.All(doc.Blocks, b => Assert.IsType<List>(b));
    });

    // ── Fenced code block ─────────────────────────────────────────────────────

    [Fact]
    public void FencedCode_RendersBlockUIContainer() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("```\nsome code\n```", Text, Link);
        Assert.Single(doc.Blocks);
        Assert.IsType<BlockUIContainer>(doc.Blocks.First());
    });

    [Fact]
    public void FencedCode_SingleLine_ContentTextIsCorrect() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("```\nsome code\n```", Text, Link);
        Assert.Equal("some code", CodeTextBlock(doc.Blocks.First()).Text);
    });

    [Fact]
    public void FencedCode_MultipleLines_RendersAsOneBlock() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("```\nline1\nline2\nline3\n```", Text, Link);
        Assert.Single(doc.Blocks);
        Assert.IsType<BlockUIContainer>(doc.Blocks.First());
    });

    [Fact]
    public void FencedCode_MultipleLines_ContentPreservesNewlines() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("```\nline1\nline2\nline3\n```", Text, Link);
        Assert.Equal("line1\nline2\nline3", CodeTextBlock(doc.Blocks.First()).Text);
    });

    [Fact]
    public void FencedCode_MultipleLines_LineNumbersMatchCount() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("```\nline1\nline2\nline3\n```", Text, Link);
        Assert.Equal("1\n2\n3", LineNumTextBlock(doc.Blocks.First()).Text);
    });

    [Fact]
    public void FencedCode_WithStartLineNumber_LineNumbersStartCorrectly() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("```42\nlineA\nlineB\n```", Text, Link);
        Assert.Equal("42\n43", LineNumTextBlock(doc.Blocks.First()).Text);
    });

    [Fact]
    public void FencedCode_BetweenParagraphs_ProducesThreeBlocks() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("before\n```\ncode\n```\nafter", Text, Link);
        Assert.Equal(3, doc.Blocks.Count);
        Assert.IsType<Paragraph>(doc.Blocks.ElementAt(0));
        Assert.IsType<BlockUIContainer>(doc.Blocks.ElementAt(1));
        Assert.IsType<Paragraph>(doc.Blocks.ElementAt(2));
    });

    [Fact]
    public void WholeLine_SingleBacktick_RendersAsCodeBlock() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("`some code`", Text, Link);
        Assert.Single(doc.Blocks);
        Assert.IsType<BlockUIContainer>(doc.Blocks.First());
    });

    [Fact]
    public void WholeLine_TripleBacktick_RendersAsCodeBlock() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("```some code```", Text, Link);
        Assert.Single(doc.Blocks);
        Assert.IsType<BlockUIContainer>(doc.Blocks.First());
    });

    // ── Hyperlink ─────────────────────────────────────────────────────────────

    [Fact]
    public void Hyperlink_RendersHyperlinkInline() => AssertOnSta(() =>
    {
        var para = Para(MarkdownRenderer.Render("[click](https://example.com)", Text, Link));
        Assert.Contains(para.Inlines, il => il is Hyperlink);
    });

    [Fact]
    public void BareUrl_RendersHyperlinkInline() => AssertOnSta(() =>
    {
        var para = Para(MarkdownRenderer.Render("https://example.com", Text, Link));
        Assert.Contains(para.Inlines, il => il is Hyperlink);
    });

    // ── Mixed inline formats ──────────────────────────────────────────────────

    [Fact]
    public void MixedFormat_TextBeforeAndAfterBoldRun() => AssertOnSta(() =>
    {
        var para = Para(MarkdownRenderer.Render("before **bold** after", Text, Link));
        var runs = para.Inlines.OfType<Run>().ToList();
        Assert.True(runs.Count >= 3);
        Assert.Equal(FontWeights.Normal, runs[0].FontWeight);
        Assert.Equal(FontWeights.Bold,   runs[1].FontWeight);
        Assert.Equal(FontWeights.Normal, runs[2].FontWeight);
    });

    // ── Format combinations ───────────────────────────────────────────────────

    [Fact]
    public void BoldUnderline_RunHasBothProperties() => AssertOnSta(() =>
    {
        var run = FirstRun(Para(MarkdownRenderer.Render("**__text__**", Text, Link)));
        Assert.Equal(FontWeights.Bold, run.FontWeight);
        Assert.NotNull(run.TextDecorations);
        Assert.Contains(run.TextDecorations, d => d.Location == TextDecorationLocation.Underline);
    });

    [Fact]
    public void BoldStrike_RunHasBothProperties() => AssertOnSta(() =>
    {
        var run = FirstRun(Para(MarkdownRenderer.Render("**~~text~~**", Text, Link)));
        Assert.Equal(FontWeights.Bold, run.FontWeight);
        Assert.NotNull(run.TextDecorations);
        Assert.Contains(run.TextDecorations, d => d.Location == TextDecorationLocation.Strikethrough);
    });

    [Fact]
    public void ItalicUnderline_RunHasBothProperties() => AssertOnSta(() =>
    {
        var run = FirstRun(Para(MarkdownRenderer.Render("*__text__*", Text, Link)));
        Assert.Equal(FontStyles.Italic, run.FontStyle);
        Assert.NotNull(run.TextDecorations);
        Assert.Contains(run.TextDecorations, d => d.Location == TextDecorationLocation.Underline);
    });

    [Fact]
    public void ItalicStrike_RunHasBothProperties() => AssertOnSta(() =>
    {
        var run = FirstRun(Para(MarkdownRenderer.Render("*~~text~~*", Text, Link)));
        Assert.Equal(FontStyles.Italic, run.FontStyle);
        Assert.NotNull(run.TextDecorations);
        Assert.Contains(run.TextDecorations, d => d.Location == TextDecorationLocation.Strikethrough);
    });

    [Fact]
    public void BoldUnderlineStrike_RunHasAllThreeProperties() => AssertOnSta(() =>
    {
        var run = FirstRun(Para(MarkdownRenderer.Render("**__~~text~~__**", Text, Link)));
        Assert.Equal(FontWeights.Bold, run.FontWeight);
        Assert.NotNull(run.TextDecorations);
        Assert.Contains(run.TextDecorations, d => d.Location == TextDecorationLocation.Underline);
        Assert.Contains(run.TextDecorations, d => d.Location == TextDecorationLocation.Strikethrough);
    });

    [Fact]
    public void BoldItalicUnderline_RunHasAllThreeProperties() => AssertOnSta(() =>
    {
        var run = FirstRun(Para(MarkdownRenderer.Render("***__text__***", Text, Link)));
        Assert.Equal(FontWeights.Bold,  run.FontWeight);
        Assert.Equal(FontStyles.Italic, run.FontStyle);
        Assert.NotNull(run.TextDecorations);
        Assert.Contains(run.TextDecorations, d => d.Location == TextDecorationLocation.Underline);
    });

    [Fact]
    public void BoldItalicStrike_RunHasAllThreeProperties() => AssertOnSta(() =>
    {
        var run = FirstRun(Para(MarkdownRenderer.Render("***~~text~~***", Text, Link)));
        Assert.Equal(FontWeights.Bold,  run.FontWeight);
        Assert.Equal(FontStyles.Italic, run.FontStyle);
        Assert.NotNull(run.TextDecorations);
        Assert.Contains(run.TextDecorations, d => d.Location == TextDecorationLocation.Strikethrough);
    });

    [Fact]
    public void AllFourFormats_RunHasAllProperties() => AssertOnSta(() =>
    {
        var run = FirstRun(Para(MarkdownRenderer.Render("***__~~text~~__***", Text, Link)));
        Assert.Equal(FontWeights.Bold,  run.FontWeight);
        Assert.Equal(FontStyles.Italic, run.FontStyle);
        Assert.NotNull(run.TextDecorations);
        Assert.Contains(run.TextDecorations, d => d.Location == TextDecorationLocation.Underline);
        Assert.Contains(run.TextDecorations, d => d.Location == TextDecorationLocation.Strikethrough);
    });

    // ── Blockquote ────────────────────────────────────────────────────────────

    [Fact]
    public void Blockquote_RendersBlockUIContainer() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("> quoted text", Text, Link);
        Assert.Single(doc.Blocks);
        Assert.IsType<BlockUIContainer>(doc.Blocks.First());
    });
}
