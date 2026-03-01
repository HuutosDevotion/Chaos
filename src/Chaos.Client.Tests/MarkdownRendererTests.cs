using System;
using System.Linq;
using System.Threading;
using System.Windows;
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

    // WPF objects require an STA thread.
    private static FlowDocument Render(string text) => RunOnSta(() =>
        MarkdownRenderer.Render(text, Text, Link));

    private static T RunOnSta<T>(Func<T> func)
    {
        T result = default!;
        Exception? ex = null;
        var t = new Thread(() => { try { result = func(); } catch (Exception e) { ex = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (ex != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
        return result;
    }

    private static Paragraph Para(FlowDocument doc, int index = 0) =>
        (Paragraph)doc.Blocks.ElementAt(index);

    private static Run FirstRun(Paragraph para) =>
        para.Inlines.OfType<Run>().First();

    // ── Plain text ────────────────────────────────────────────────────────────

    [Fact]
    public void PlainText_SingleParagraph_WithCorrectText()
    {
        var doc = Render("hello world");
        Assert.Single(doc.Blocks);
        Assert.Equal("hello world", FirstRun(Para(doc)).Text);
    }

    [Fact]
    public void EmptyString_ReturnsEmptyDocument()
    {
        var doc = Render("");
        Assert.Empty(doc.Blocks);
    }

    // ── Bold ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Bold_RunHasBoldFontWeight()
    {
        var run = FirstRun(Para(Render("**bold**")));
        Assert.Equal(FontWeights.Bold, run.FontWeight);
    }

    [Fact]
    public void Bold_ContentTextIsCorrect()
    {
        var run = FirstRun(Para(Render("**hello**")));
        Assert.Equal("hello", run.Text);
    }

    // ── Italic ────────────────────────────────────────────────────────────────

    [Fact]
    public void Italic_RunHasItalicFontStyle()
    {
        var run = FirstRun(Para(Render("*italic*")));
        Assert.Equal(FontStyles.Italic, run.FontStyle);
    }

    // ── Bold + Italic ─────────────────────────────────────────────────────────

    [Fact]
    public void BoldItalic_RunHasBothBoldAndItalic()
    {
        var run = FirstRun(Para(Render("***both***")));
        Assert.Equal(FontWeights.Bold,   run.FontWeight);
        Assert.Equal(FontStyles.Italic, run.FontStyle);
    }

    // ── Underline ─────────────────────────────────────────────────────────────

    [Fact]
    public void Underline_RunHasUnderlineDecoration()
    {
        var run = FirstRun(Para(Render("__underline__")));
        Assert.NotNull(run.TextDecorations);
        Assert.Contains(run.TextDecorations,
            d => d.Location == TextDecorationLocation.Underline);
    }

    // ── Strikethrough ─────────────────────────────────────────────────────────

    [Fact]
    public void Strike_RunHasStrikethroughDecoration()
    {
        var run = FirstRun(Para(Render("~~strike~~")));
        Assert.NotNull(run.TextDecorations);
        Assert.Contains(run.TextDecorations,
            d => d.Location == TextDecorationLocation.Strikethrough);
    }

    // ── Underline + Strike combined ───────────────────────────────────────────

    [Fact]
    public void UnderlineAndStrike_BothDecorations()
    {
        // Nested: __~~text~~__
        var run = FirstRun(Para(Render("__~~text~~__")));
        Assert.NotNull(run.TextDecorations);
        Assert.Contains(run.TextDecorations, d => d.Location == TextDecorationLocation.Underline);
        Assert.Contains(run.TextDecorations, d => d.Location == TextDecorationLocation.Strikethrough);
    }

    // ── Inline code ───────────────────────────────────────────────────────────

    [Fact]
    public void InlineCode_RendersInlineUIContainer()
    {
        var para = Para(Render("before `code` after"));
        Assert.Contains(para.Inlines, il => il is InlineUIContainer);
    }

    // ── Alignment ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(":left hello:",    TextAlignment.Left)]
    [InlineData(":center hello:",  TextAlignment.Center)]
    [InlineData(":right hello:",   TextAlignment.Right)]
    [InlineData(":justify hello:", TextAlignment.Justify)]
    public void AlignmentDirective_SetsCorrectTextAlignment(string input, TextAlignment expected)
    {
        var para = Para(Render(input));
        Assert.Equal(expected, para.TextAlignment);
    }

    [Fact]
    public void AlignmentDirective_ContentIsRendered()
    {
        var para = Para(Render(":center hello:"));
        Assert.Equal("hello", FirstRun(para).Text);
    }

    [Fact]
    public void AlignmentWithBold_RendersContentWithBoldInsideAlignment()
    {
        var para = Para(Render(":center **bold**:"));
        Assert.Equal(TextAlignment.Center, para.TextAlignment);
        var run = para.Inlines.OfType<Run>().First();
        Assert.Equal(FontWeights.Bold, run.FontWeight);
    }

    // ── Multiline inline spans ────────────────────────────────────────────────

    [Fact]
    public void MultilineBold_ContainsLineBreak()
    {
        // **line1\nline2** collapses to a single paragraph with a LineBreak between runs
        var para = Para(Render("**line1\nline2**"));
        Assert.Contains(para.Inlines, il => il is LineBreak);
    }

    [Fact]
    public void MultilineBold_BothRunsAreBold()
    {
        var para = Para(Render("**line1\nline2**"));
        var runs = para.Inlines.OfType<Run>().ToList();
        Assert.All(runs, r => Assert.Equal(FontWeights.Bold, r.FontWeight));
    }

    [Fact]
    public void MultilineItalic_ContainsLineBreak()
    {
        var para = Para(Render("*line1\nline2*"));
        Assert.Contains(para.Inlines, il => il is LineBreak);
    }

    // ── Multiline alignment ───────────────────────────────────────────────────

    [Fact]
    public void MultilineAlignment_SingleParagraphWithLineBreak()
    {
        var doc = Render(":center line1\nline2:");
        Assert.Single(doc.Blocks);
        var para = Para(doc);
        Assert.Equal(TextAlignment.Center, para.TextAlignment);
        Assert.Contains(para.Inlines, il => il is LineBreak);
    }

    // ── Bullet list ───────────────────────────────────────────────────────────

    [Fact]
    public void BulletList_RendersListBlocks()
    {
        var doc = Render("- item1\n- item2");
        Assert.Equal(2, doc.Blocks.Count);
        Assert.All(doc.Blocks, b => Assert.IsType<List>(b));
    }

    // ── Numbered list ─────────────────────────────────────────────────────────

    [Fact]
    public void NumberedList_RendersListBlocks()
    {
        var doc = Render("1. first\n2. second");
        Assert.Equal(2, doc.Blocks.Count);
        Assert.All(doc.Blocks, b => Assert.IsType<List>(b));
    }

    // ── Fenced code block ─────────────────────────────────────────────────────

    [Fact]
    public void FencedCode_RendersBlockUIContainer()
    {
        var doc = Render("```\nsome code\n```");
        Assert.Single(doc.Blocks);
        Assert.IsType<BlockUIContainer>(doc.Blocks.First());
    }

    // ── Hyperlink ─────────────────────────────────────────────────────────────

    [Fact]
    public void Hyperlink_RendersHyperlinkInline()
    {
        var para = Para(Render("[click](https://example.com)"));
        Assert.Contains(para.Inlines, il => il is Hyperlink);
    }

    [Fact]
    public void BareUrl_RendersHyperlinkInline()
    {
        var para = Para(Render("https://example.com"));
        Assert.Contains(para.Inlines, il => il is Hyperlink);
    }

    // ── Mixed inline formats ──────────────────────────────────────────────────

    [Fact]
    public void MixedFormat_TextBeforeAndAfterBoldRun()
    {
        var para = Para(Render("before **bold** after"));
        var runs = para.Inlines.OfType<Run>().ToList();
        Assert.True(runs.Count >= 3);
        Assert.Equal(FontWeights.Normal, runs[0].FontWeight);
        Assert.Equal(FontWeights.Bold,   runs[1].FontWeight);
        Assert.Equal(FontWeights.Normal, runs[2].FontWeight);
    }

    // ── Blockquote ────────────────────────────────────────────────────────────

    [Fact]
    public void Blockquote_RendersBlockUIContainer()
    {
        var doc = Render("> quoted text");
        Assert.Single(doc.Blocks);
        Assert.IsType<BlockUIContainer>(doc.Blocks.First());
    }
}
