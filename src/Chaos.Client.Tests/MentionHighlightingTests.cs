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
/// Regression tests for @mention pill rendering in MarkdownRenderer.Render().
/// Verifies that @username tokens are rendered as styled InlineUIContainer pills
/// when a currentUsername is provided, with gold colouring for self-mentions
/// and blurple for mentions of other users.
/// </summary>
public class MentionHighlightingTests
{
    private static readonly Brush Text = Brushes.White;
    private static readonly Brush Link = Brushes.CornflowerBlue;

    private static void AssertOnSta(Action action)
    {
        Exception? ex = null;
        var t = new Thread(() => { try { action(); } catch (Exception e) { ex = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (ex != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
    }

    private static Paragraph Para(FlowDocument doc) => (Paragraph)doc.Blocks.First();

    // InlineUIContainer → Border → TextBlock
    private static TextBlock MentionTextBlock(InlineUIContainer pill) =>
        (TextBlock)((Border)pill.Child).Child;

    private static InlineUIContainer FirstPill(Paragraph para) =>
        para.Inlines.OfType<InlineUIContainer>().First();

    // ── Basic rendering ───────────────────────────────────────────────────────

    [Fact]
    public void Mention_WithCurrentUsername_RendersInlineUIContainer() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("@alice", Text, Link, currentUsername: "bob");
        Assert.Contains(Para(doc).Inlines, il => il is InlineUIContainer);
    });

    [Fact]
    public void Mention_WithoutCurrentUsername_RendersAsPlainText() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("@alice", Text, Link);
        var para = Para(doc);
        Assert.DoesNotContain(para.Inlines, il => il is InlineUIContainer);
        Assert.Contains(para.Inlines.OfType<Run>(), r => r.Text.Contains("@alice"));
    });

    [Fact]
    public void Mention_PillTextContainsAtPrefixAndUsername() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("@alice", Text, Link, currentUsername: "bob");
        Assert.Equal("@alice", MentionTextBlock(FirstPill(Para(doc))).Text);
    });

    [Fact]
    public void Mention_PillBaselineAlignment_IsCenter() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("@alice", Text, Link, currentUsername: "bob");
        Assert.Equal(BaselineAlignment.Center, FirstPill(Para(doc)).BaselineAlignment);
    });

    [Fact]
    public void Mention_PillTextIsSemiBold() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("@alice", Text, Link, currentUsername: "bob");
        Assert.Equal(FontWeights.SemiBold, MentionTextBlock(FirstPill(Para(doc))).FontWeight);
    });

    // ── Self vs other colouring ───────────────────────────────────────────────

    [Fact]
    public void Mention_OtherUser_UsesBlurpleForeground() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("@alice", Text, Link, currentUsername: "bob");
        var fg = (SolidColorBrush)MentionTextBlock(FirstPill(Para(doc))).Foreground;
        // Blurple: RGB(0xC9, 0xCC, 0xFF)
        Assert.Equal(0xC9, fg.Color.R);
        Assert.Equal(0xCC, fg.Color.G);
        Assert.Equal(0xFF, fg.Color.B);
    });

    [Fact]
    public void Mention_SelfUser_UsesGoldForeground() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("@alice", Text, Link, currentUsername: "alice");
        var fg = (SolidColorBrush)MentionTextBlock(FirstPill(Para(doc))).Foreground;
        // Gold: RGB(0xFF, 0xD0, 0x58)
        Assert.Equal(0xFF, fg.Color.R);
        Assert.Equal(0xD0, fg.Color.G);
        Assert.Equal(0x58, fg.Color.B);
    });

    [Fact]
    public void Mention_SelfMatch_IsCaseInsensitive() => AssertOnSta(() =>
    {
        // @Alice in message, currentUsername = "alice" — should still be gold
        var doc = MarkdownRenderer.Render("@Alice", Text, Link, currentUsername: "alice");
        var fg = (SolidColorBrush)MentionTextBlock(FirstPill(Para(doc))).Foreground;
        Assert.Equal(0xFF, fg.Color.R);
        Assert.Equal(0xD0, fg.Color.G);
        Assert.Equal(0x58, fg.Color.B);
    });

    [Fact]
    public void Mention_SelfMatch_UsernameInMessageLowercase() => AssertOnSta(() =>
    {
        // @alice in message, currentUsername = "Alice" — should still be gold
        var doc = MarkdownRenderer.Render("@alice", Text, Link, currentUsername: "Alice");
        var fg = (SolidColorBrush)MentionTextBlock(FirstPill(Para(doc))).Foreground;
        Assert.Equal(0xFF, fg.Color.R);
        Assert.Equal(0xD0, fg.Color.G);
        Assert.Equal(0x58, fg.Color.B);
    });

    // ── Mixed text ────────────────────────────────────────────────────────────

    [Fact]
    public void Mention_InMixedText_LeadingRunPreserved() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("hey @alice!", Text, Link, currentUsername: "bob");
        var firstRun = Para(doc).Inlines.OfType<Run>().First();
        Assert.Equal("hey ", firstRun.Text);
    });

    [Fact]
    public void Mention_InMixedText_TrailingRunPreserved() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("hey @alice!", Text, Link, currentUsername: "bob");
        var runs = Para(doc).Inlines.OfType<Run>().ToList();
        Assert.Contains(runs, r => r.Text == "!");
    });

    [Fact]
    public void Mention_InMixedText_PillPresentBetweenRuns() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("hey @alice!", Text, Link, currentUsername: "bob");
        var inlines = Para(doc).Inlines.ToList();
        int pillIdx  = inlines.FindIndex(il => il is InlineUIContainer);
        Assert.True(pillIdx > 0,  "pill should not be first inline");
        Assert.True(pillIdx < inlines.Count - 1, "pill should not be last inline");
    });

    // ── Multiple mentions ─────────────────────────────────────────────────────

    [Fact]
    public void Mention_MultipleMentions_AllRenderAsPills() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("@alice and @bob", Text, Link, currentUsername: "carol");
        Assert.Equal(2, Para(doc).Inlines.OfType<InlineUIContainer>().Count());
    });

    [Fact]
    public void Mention_MultipleMentions_CorrectTexts() => AssertOnSta(() =>
    {
        var doc  = MarkdownRenderer.Render("@alice and @bob", Text, Link, currentUsername: "carol");
        var pills = Para(doc).Inlines.OfType<InlineUIContainer>().ToList();
        Assert.Equal("@alice", MentionTextBlock(pills[0]).Text);
        Assert.Equal("@bob",   MentionTextBlock(pills[1]).Text);
    });

    [Fact]
    public void Mention_SelfAndOther_DifferentColours() => AssertOnSta(() =>
    {
        var doc   = MarkdownRenderer.Render("@alice and @bob", Text, Link, currentUsername: "alice");
        var pills = Para(doc).Inlines.OfType<InlineUIContainer>().ToList();

        var selfFg  = (SolidColorBrush)MentionTextBlock(pills[0]).Foreground; // @alice = self
        var otherFg = (SolidColorBrush)MentionTextBlock(pills[1]).Foreground; // @bob = other

        // self → gold
        Assert.Equal(0xFF, selfFg.Color.R);
        Assert.Equal(0xD0, selfFg.Color.G);
        Assert.Equal(0x58, selfFg.Color.B);

        // other → blurple
        Assert.Equal(0xC9, otherFg.Color.R);
        Assert.Equal(0xCC, otherFg.Color.G);
        Assert.Equal(0xFF, otherFg.Color.B);
    });

    // ── No false positives ────────────────────────────────────────────────────

    [Fact]
    public void PlainText_NoPill_WhenCurrentUsernameProvided() => AssertOnSta(() =>
    {
        var doc = MarkdownRenderer.Render("hello world", Text, Link, currentUsername: "bob");
        Assert.DoesNotContain(Para(doc).Inlines, il => il is InlineUIContainer);
    });

    [Fact]
    public void Mention_InsideInlineCode_NotRenderedAsPill() => AssertOnSta(() =>
    {
        // Whole-line single-backtick → code block, @alice stays raw inside it
        var doc = MarkdownRenderer.Render("`@alice`", Text, Link, currentUsername: "alice");
        Assert.Single(doc.Blocks);
        Assert.IsType<BlockUIContainer>(doc.Blocks.First());
    });

    [Fact]
    public void Mention_InsideInlineBoldSpan_StillRendersPill() => AssertOnSta(() =>
    {
        // Bold wraps a mention — AddInlines is called with the inner text
        var doc = MarkdownRenderer.Render("**@alice**", Text, Link, currentUsername: "alice");
        Assert.Contains(Para(doc).Inlines, il => il is InlineUIContainer);
    });
}
