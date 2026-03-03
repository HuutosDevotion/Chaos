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

public class InlineFormatPreviewTests
{
    private static readonly Brush DefaultFg = Brushes.White;
    private static readonly Brush MutedBrush = Brushes.Gray;
    private static readonly FontFamily ConsolasFont = new("Consolas");

    private static void AssertOnSta(Action action)
    {
        Exception? ex = null;
        var t = new Thread(() => { try { action(); } catch (Exception e) { ex = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (ex != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
    }

    private static (RichTextBox rtb, Paragraph para) CreateRtb(string text)
    {
        var rtb = new RichTextBox();
        var para = new Paragraph(new Run(text));
        rtb.Document.Blocks.Clear();
        rtb.Document.Blocks.Add(para);
        rtb.CaretPosition = para.ContentEnd;
        return (rtb, para);
    }

    private static InlineFormatPreview CreatePreview() =>
        new(DefaultFg, MutedBrush);

    private static Run[] GetRuns(Paragraph para) =>
        para.Inlines.OfType<Run>().ToArray();

    // ── Bold ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Bold_ContentRunHasBoldWeight() => AssertOnSta(() =>
    {
        var (rtb, para) = CreateRtb("**bold**");
        CreatePreview().Apply(rtb);
        var runs = GetRuns(para);
        var contentRun = runs.First(r => r.Text == "bold");
        Assert.Equal(FontWeights.Bold, contentRun.FontWeight);
    });

    // ── Italic ────────────────────────────────────────────────────────────────

    [Fact]
    public void Italic_ContentRunHasItalicStyle() => AssertOnSta(() =>
    {
        var (rtb, para) = CreateRtb("*italic*");
        CreatePreview().Apply(rtb);
        var runs = GetRuns(para);
        var contentRun = runs.First(r => r.Text == "italic");
        Assert.Equal(FontStyles.Italic, contentRun.FontStyle);
    });

    // ── Strikethrough ─────────────────────────────────────────────────────────

    [Fact]
    public void Strike_ContentRunHasStrikethroughDecoration() => AssertOnSta(() =>
    {
        var (rtb, para) = CreateRtb("~~strike~~");
        CreatePreview().Apply(rtb);
        var runs = GetRuns(para);
        var contentRun = runs.First(r => r.Text == "strike");
        Assert.NotNull(contentRun.TextDecorations);
        Assert.Contains(contentRun.TextDecorations,
            d => d.Location == TextDecorationLocation.Strikethrough);
    });

    // ── Underline ─────────────────────────────────────────────────────────────

    [Fact]
    public void Underline_ContentRunHasUnderlineDecoration() => AssertOnSta(() =>
    {
        var (rtb, para) = CreateRtb("__underline__");
        CreatePreview().Apply(rtb);
        var runs = GetRuns(para);
        var contentRun = runs.First(r => r.Text == "underline");
        Assert.NotNull(contentRun.TextDecorations);
        Assert.Contains(contentRun.TextDecorations,
            d => d.Location == TextDecorationLocation.Underline);
    });

    // ── Inline code ───────────────────────────────────────────────────────────

    [Fact]
    public void InlineCode_ContentRunHasConsolasFont() => AssertOnSta(() =>
    {
        var (rtb, para) = CreateRtb("`code`");
        CreatePreview().Apply(rtb);
        var runs = GetRuns(para);
        var contentRun = runs.First(r => r.Text == "code");
        Assert.Equal(ConsolasFont.Source, contentRun.FontFamily.Source);
    });

    // ── Triple backtick inline code ───────────────────────────────────────────

    [Fact]
    public void TripleCode_ContentRunHasConsolasFont() => AssertOnSta(() =>
    {
        var (rtb, para) = CreateRtb("```code```");
        CreatePreview().Apply(rtb);
        var runs = GetRuns(para);
        var contentRun = runs.First(r => r.Text == "code");
        Assert.Equal(ConsolasFont.Source, contentRun.FontFamily.Source);
    });

    // ── Delimiters muted ──────────────────────────────────────────────────────

    [Fact]
    public void Delimiters_UseMutedBrush() => AssertOnSta(() =>
    {
        var (rtb, para) = CreateRtb("**text**");
        CreatePreview().Apply(rtb);
        var runs = GetRuns(para);
        var delimRuns = runs.Where(r => r.Text == "**").ToArray();
        Assert.Equal(2, delimRuns.Length);
        Assert.All(delimRuns, r => Assert.Equal(MutedBrush, r.Foreground));
    });

    // ── Plain text ────────────────────────────────────────────────────────────

    [Fact]
    public void PlainText_NoFormattingApplied() => AssertOnSta(() =>
    {
        var (rtb, para) = CreateRtb("hello world");
        CreatePreview().Apply(rtb);
        var runs = GetRuns(para);
        Assert.Single(runs);
        Assert.Equal(FontWeights.Normal, runs[0].FontWeight);
        Assert.Equal(DefaultFg, runs[0].Foreground);
    });

    // ── Nested bold + underline ───────────────────────────────────────────────

    [Fact]
    public void Nested_BoldAndUnderline() => AssertOnSta(() =>
    {
        var (rtb, para) = CreateRtb("**__nested__**");
        CreatePreview().Apply(rtb);
        var runs = GetRuns(para);
        var contentRun = runs.First(r => r.Text == "nested");
        Assert.Equal(FontWeights.Bold, contentRun.FontWeight);
        Assert.NotNull(contentRun.TextDecorations);
        Assert.Contains(contentRun.TextDecorations,
            d => d.Location == TextDecorationLocation.Underline);
    });

    // ── Bold + italic (***) ───────────────────────────────────────────────────

    [Fact]
    public void BoldItalic_ContentRunHasBothStyles() => AssertOnSta(() =>
    {
        var (rtb, para) = CreateRtb("***both***");
        CreatePreview().Apply(rtb);
        var runs = GetRuns(para);
        var contentRun = runs.First(r => r.Text == "both");
        Assert.Equal(FontWeights.Bold, contentRun.FontWeight);
        Assert.Equal(FontStyles.Italic, contentRun.FontStyle);
    });

    // ── Multiple spans ────────────────────────────────────────────────────────

    [Fact]
    public void MultipleSpans_BoldAndItalicFormatted() => AssertOnSta(() =>
    {
        var (rtb, para) = CreateRtb("**bold** and *italic*");
        CreatePreview().Apply(rtb);
        var runs = GetRuns(para);
        var boldRun = runs.First(r => r.Text == "bold");
        var italicRun = runs.First(r => r.Text == "italic");
        Assert.Equal(FontWeights.Bold, boldRun.FontWeight);
        Assert.Equal(FontStyles.Italic, italicRun.FontStyle);
    });

    // ── Unclosed delimiter ────────────────────────────────────────────────────

    [Fact]
    public void Unclosed_NoFormattingApplied() => AssertOnSta(() =>
    {
        var (rtb, para) = CreateRtb("**unclosed");
        CreatePreview().Apply(rtb);
        var runs = GetRuns(para);
        Assert.DoesNotContain(runs, r => r.FontWeight == FontWeights.Bold);
    });

    // ── Code protects inner formatting ────────────────────────────────────────

    [Fact]
    public void CodeProtects_NoBoldInsideCode() => AssertOnSta(() =>
    {
        var (rtb, para) = CreateRtb("`**not bold**`");
        CreatePreview().Apply(rtb);
        var runs = GetRuns(para);
        var contentRun = runs.First(r => r.Text == "**not bold**");
        Assert.Equal(ConsolasFont.Source, contentRun.FontFamily.Source);
        Assert.Equal(FontWeights.Normal, contentRun.FontWeight);
    });

    // ── Fenced code block ─────────────────────────────────────────────────────

    [Fact]
    public void FencedBlock_ContentHasConsolasAndDelimitersMuted() => AssertOnSta(() =>
    {
        var rtb = new RichTextBox();
        var para = new Paragraph();
        para.Inlines.Add(new Run("```"));
        para.Inlines.Add(new LineBreak());
        para.Inlines.Add(new Run("code line"));
        para.Inlines.Add(new LineBreak());
        para.Inlines.Add(new Run("```"));
        rtb.Document.Blocks.Clear();
        rtb.Document.Blocks.Add(para);
        rtb.CaretPosition = para.ContentEnd;

        CreatePreview().Apply(rtb);

        var runs = GetRuns(para);
        // First and last runs are ``` delimiters — should be muted
        Assert.Equal(MutedBrush, runs.First().Foreground);
        Assert.Equal(MutedBrush, runs.Last().Foreground);
        // Middle run is fenced content — should have Consolas
        var contentRun = runs.First(r => r.Text == "code line");
        Assert.Equal(ConsolasFont.Source, contentRun.FontFamily.Source);
    });

    // ── Unclosed fence ────────────────────────────────────────────────────────

    [Fact]
    public void UnclosedFence_DelimiterMutedButContentNotStyled() => AssertOnSta(() =>
    {
        var rtb = new RichTextBox();
        var para = new Paragraph();
        para.Inlines.Add(new Run("```"));
        para.Inlines.Add(new LineBreak());
        para.Inlines.Add(new Run("some text"));
        rtb.Document.Blocks.Clear();
        rtb.Document.Blocks.Add(para);
        rtb.CaretPosition = para.ContentEnd;

        CreatePreview().Apply(rtb);

        var runs = GetRuns(para);
        // The ``` run should be muted (unpaired fence delimiter)
        var fenceRun = runs.First(r => r.Text == "```");
        Assert.Equal(MutedBrush, fenceRun.Foreground);
        // The text after the unclosed fence is NOT styled as code
        var textRun = runs.First(r => r.Text == "some text");
        Assert.NotEqual(ConsolasFont.Source, textRun.FontFamily.Source);
    });

    // ── IsCaretInFencedBlock ──────────────────────────────────────────────────

    [Fact]
    public void IsCaretInFenced_InsideBlock_ReturnsTrue() => AssertOnSta(() =>
    {
        var rtb = new RichTextBox();
        var para = new Paragraph();
        para.Inlines.Add(new Run("```"));
        para.Inlines.Add(new LineBreak());
        var codeRun = new Run("inside code");
        para.Inlines.Add(codeRun);
        para.Inlines.Add(new LineBreak());
        para.Inlines.Add(new Run("```"));
        rtb.Document.Blocks.Clear();
        rtb.Document.Blocks.Add(para);
        // Place caret inside the code content
        rtb.CaretPosition = codeRun.ContentEnd;

        Assert.True(InlineFormatPreview.IsCaretInFencedBlock(rtb));
    });

    [Fact]
    public void IsCaretInFenced_OutsideBlock_ReturnsFalse() => AssertOnSta(() =>
    {
        var rtb = new RichTextBox();
        var para = new Paragraph(new Run("plain text"));
        rtb.Document.Blocks.Clear();
        rtb.Document.Blocks.Add(para);
        rtb.CaretPosition = para.ContentEnd;

        Assert.False(InlineFormatPreview.IsCaretInFencedBlock(rtb));
    });

    [Fact]
    public void IsCaretInFenced_AfterClosedBlock_ReturnsFalse() => AssertOnSta(() =>
    {
        var rtb = new RichTextBox();
        var para = new Paragraph();
        para.Inlines.Add(new Run("```"));
        para.Inlines.Add(new LineBreak());
        para.Inlines.Add(new Run("code"));
        para.Inlines.Add(new LineBreak());
        para.Inlines.Add(new Run("```"));
        para.Inlines.Add(new LineBreak());
        var afterRun = new Run("after");
        para.Inlines.Add(afterRun);
        rtb.Document.Blocks.Clear();
        rtb.Document.Blocks.Add(para);
        // Place caret after the closed block
        rtb.CaretPosition = afterRun.ContentEnd;

        Assert.False(InlineFormatPreview.IsCaretInFencedBlock(rtb));
    });
}
