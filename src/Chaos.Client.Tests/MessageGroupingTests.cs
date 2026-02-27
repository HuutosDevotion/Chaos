using Chaos.Client.ViewModels;
using Chaos.Shared;
using Xunit;

namespace Chaos.Client.Tests;

/// <summary>
/// Unit tests for the message grouping algorithm.
/// Replicates the ShouldShowHeader / RecomputeGrouping logic from MainViewModel
/// to verify behaviour in isolation without needing WPF services.
/// </summary>
public class MessageGroupingTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private static MessageViewModel Msg(string author, DateTime? timestamp = null) =>
        new(new MessageDto { Author = author, Content = "hello", Timestamp = timestamp ?? DateTime.UtcNow });

    /// <summary>Mirrors MainViewModel.ShouldShowHeader.</summary>
    private static bool ShouldShowHeader(MessageViewModel msg, MessageViewModel? prev, bool grouped) =>
        !grouped || prev is null || prev.Author != msg.Author
        || (msg.Timestamp - prev.Timestamp).TotalMinutes > 5;

    /// <summary>Mirrors MainViewModel.RecomputeGrouping.</summary>
    private static void Regroup(IList<MessageViewModel> messages, bool grouped)
    {
        MessageViewModel? prev = null;
        foreach (var msg in messages)
        {
            msg.ShowHeader = ShouldShowHeader(msg, prev, grouped);
            prev = msg;
        }
    }

    // ── grouping disabled ──────────────────────────────────────────────────────

    [Fact]
    public void GroupingDisabled_SingleMessage_ShowsHeader()
    {
        var msgs = new[] { Msg("Alice") };
        Regroup(msgs, grouped: false);
        Assert.True(msgs[0].ShowHeader);
    }

    [Fact]
    public void GroupingDisabled_SameAuthorConsecutive_BothShowHeader()
    {
        var msgs = new[] { Msg("Alice"), Msg("Alice") };
        Regroup(msgs, grouped: false);
        Assert.True(msgs[0].ShowHeader);
        Assert.True(msgs[1].ShowHeader);
    }

    [Fact]
    public void GroupingDisabled_DifferentAuthors_AllShowHeader()
    {
        var msgs = new[] { Msg("Alice"), Msg("Bob"), Msg("Alice") };
        Regroup(msgs, grouped: false);
        Assert.All(msgs, m => Assert.True(m.ShowHeader));
    }

    // ── grouping enabled ───────────────────────────────────────────────────────

    [Fact]
    public void GroupingEnabled_FirstMessage_AlwaysShowsHeader()
    {
        var msgs = new[] { Msg("Alice") };
        Regroup(msgs, grouped: true);
        Assert.True(msgs[0].ShowHeader);
    }

    [Fact]
    public void GroupingEnabled_SameAuthorWithinFiveMinutes_SecondHidesHeader()
    {
        var t = DateTime.UtcNow;
        var msgs = new[] { Msg("Alice", t), Msg("Alice", t.AddMinutes(2)) };
        Regroup(msgs, grouped: true);
        Assert.True(msgs[0].ShowHeader);
        Assert.False(msgs[1].ShowHeader);
    }

    [Fact]
    public void GroupingEnabled_LongSameAuthorRun_OnlyFirstShowsHeader()
    {
        var t = DateTime.UtcNow;
        var msgs = new[]
        {
            Msg("Alice", t),
            Msg("Alice", t.AddMinutes(1)),
            Msg("Alice", t.AddMinutes(2)),
            Msg("Alice", t.AddMinutes(3)),
        };
        Regroup(msgs, grouped: true);
        Assert.True(msgs[0].ShowHeader);
        Assert.False(msgs[1].ShowHeader);
        Assert.False(msgs[2].ShowHeader);
        Assert.False(msgs[3].ShowHeader);
    }

    [Fact]
    public void GroupingEnabled_AuthorChange_ShowsHeaderForNewAuthor()
    {
        var t = DateTime.UtcNow;
        var msgs = new[] { Msg("Alice", t), Msg("Bob", t.AddMinutes(1)) };
        Regroup(msgs, grouped: true);
        Assert.True(msgs[0].ShowHeader);
        Assert.True(msgs[1].ShowHeader);
    }

    [Fact]
    public void GroupingEnabled_AlternatingAuthors_AllShowHeader()
    {
        var t = DateTime.UtcNow;
        var msgs = new[]
        {
            Msg("Alice", t),
            Msg("Bob",   t.AddMinutes(1)),
            Msg("Alice", t.AddMinutes(2)),
            Msg("Bob",   t.AddMinutes(3)),
        };
        Regroup(msgs, grouped: true);
        Assert.All(msgs, m => Assert.True(m.ShowHeader));
    }

    [Fact]
    public void GroupingEnabled_AuthorReturnsAfterOther_ShowsHeader()
    {
        var t = DateTime.UtcNow;
        var msgs = new[]
        {
            Msg("Alice", t),
            Msg("Bob",   t.AddMinutes(1)),
            Msg("Alice", t.AddMinutes(2)),
        };
        Regroup(msgs, grouped: true);
        Assert.True(msgs[0].ShowHeader);  // Alice: first
        Assert.True(msgs[1].ShowHeader);  // Bob: new author
        Assert.True(msgs[2].ShowHeader);  // Alice: different from prev (Bob)
    }

    [Fact]
    public void GroupingEnabled_MixedRuns_CorrectHeaders()
    {
        // Alice x2, Bob x3, Alice x1  — all within 5 min of each other
        var t = DateTime.UtcNow;
        var msgs = new[]
        {
            Msg("Alice", t),
            Msg("Alice", t.AddMinutes(1)),
            Msg("Bob",   t.AddMinutes(2)),
            Msg("Bob",   t.AddMinutes(2).AddSeconds(30)),
            Msg("Bob",   t.AddMinutes(3)),
            Msg("Alice", t.AddMinutes(4)),
        };
        Regroup(msgs, grouped: true);
        Assert.True(msgs[0].ShowHeader);  // Alice (1st)
        Assert.False(msgs[1].ShowHeader); // Alice (2nd, grouped)
        Assert.True(msgs[2].ShowHeader);  // Bob (1st)
        Assert.False(msgs[3].ShowHeader); // Bob (2nd, grouped)
        Assert.False(msgs[4].ShowHeader); // Bob (3rd, grouped)
        Assert.True(msgs[5].ShowHeader);  // Alice (new run)
    }

    // ── 5-minute timeout ───────────────────────────────────────────────────────

    [Fact]
    public void GroupingEnabled_SameAuthorAfterFiveMinutes_ShowsHeader()
    {
        var t = DateTime.UtcNow;
        var msgs = new[] { Msg("Alice", t), Msg("Alice", t.AddMinutes(6)) };
        Regroup(msgs, grouped: true);
        Assert.True(msgs[0].ShowHeader);
        Assert.True(msgs[1].ShowHeader);
    }

    [Fact]
    public void GroupingEnabled_SameAuthorExactlyFiveMinutes_GroupsMessages()
    {
        var t = DateTime.UtcNow;
        var msgs = new[] { Msg("Alice", t), Msg("Alice", t.AddMinutes(5)) };
        Regroup(msgs, grouped: true);
        Assert.True(msgs[0].ShowHeader);
        Assert.False(msgs[1].ShowHeader);
    }

    [Fact]
    public void GroupingEnabled_SameAuthorJustOverFiveMinutes_ShowsHeader()
    {
        var t = DateTime.UtcNow;
        var msgs = new[] { Msg("Alice", t), Msg("Alice", t.AddMinutes(5).AddSeconds(1)) };
        Regroup(msgs, grouped: true);
        Assert.True(msgs[0].ShowHeader);
        Assert.True(msgs[1].ShowHeader);
    }

    [Fact]
    public void GroupingEnabled_TimeoutBreaksGroup_ThenResumesIfClose()
    {
        var t = DateTime.UtcNow;
        var msgs = new[]
        {
            Msg("Alice", t),
            Msg("Alice", t.AddMinutes(1)),   // grouped
            Msg("Alice", t.AddMinutes(8)),   // timeout — new header
            Msg("Alice", t.AddMinutes(9)),   // grouped again
        };
        Regroup(msgs, grouped: true);
        Assert.True(msgs[0].ShowHeader);
        Assert.False(msgs[1].ShowHeader);
        Assert.True(msgs[2].ShowHeader);
        Assert.False(msgs[3].ShowHeader);
    }

    // ── toggling grouping ──────────────────────────────────────────────────────

    [Fact]
    public void ToggleGroupingOn_UpdatesShowHeaderInPlace()
    {
        var t = DateTime.UtcNow;
        var msgs = new[] { Msg("Alice", t), Msg("Alice", t.AddMinutes(1)) };
        Regroup(msgs, grouped: false); // both show header
        Regroup(msgs, grouped: true);  // second should now hide

        Assert.True(msgs[0].ShowHeader);
        Assert.False(msgs[1].ShowHeader);
    }

    [Fact]
    public void ToggleGroupingOff_AllMessagesShowHeader()
    {
        var t = DateTime.UtcNow;
        var msgs = new[] { Msg("Alice", t), Msg("Alice", t.AddMinutes(1)) };
        Regroup(msgs, grouped: true);  // second hidden
        Regroup(msgs, grouped: false); // both should show now

        Assert.True(msgs[0].ShowHeader);
        Assert.True(msgs[1].ShowHeader);
    }
}
