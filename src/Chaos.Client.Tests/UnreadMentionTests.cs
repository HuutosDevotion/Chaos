using Chaos.Client.ViewModels;
using Chaos.Shared;
using Xunit;
using System.Linq;

namespace Chaos.Client.Tests;

/// <summary>
/// Unit tests for unread count and mention badge state on ChannelViewModel,
/// and MentionNotification model used in the inbox.
/// </summary>
public class UnreadMentionTests
{
    private static ChannelViewModel TextChannel(int id = 1, string name = "general") =>
        new(new ChannelDto { Id = id, Name = name, Type = ChannelType.Text });

    // ── UnreadCount defaults ───────────────────────────────────────────────

    [Fact]
    public void NewChannel_UnreadCount_DefaultsToZero()
    {
        var ch = TextChannel();
        Assert.Equal(0, ch.UnreadCount);
    }

    [Fact]
    public void NewChannel_HasUnread_DefaultsFalse()
    {
        var ch = TextChannel();
        Assert.False(ch.HasUnread);
    }

    [Fact]
    public void NewChannel_HasMention_DefaultsFalse()
    {
        var ch = TextChannel();
        Assert.False(ch.HasMention);
    }

    // ── UnreadCount changes ────────────────────────────────────────────────

    [Fact]
    public void UnreadCount_Increment_SetsHasUnreadTrue()
    {
        var ch = TextChannel();
        ch.UnreadCount = 1;

        Assert.True(ch.HasUnread);
        Assert.Equal(1, ch.UnreadCount);
    }

    [Fact]
    public void UnreadCount_SetToZero_SetsHasUnreadFalse()
    {
        var ch = TextChannel();
        ch.UnreadCount = 3;
        ch.UnreadCount = 0;

        Assert.False(ch.HasUnread);
    }

    [Fact]
    public void UnreadCount_RaisesPropertyChanged()
    {
        var ch = TextChannel();
        var raised = new List<string?>();
        ch.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        ch.UnreadCount = 5;

        Assert.Contains(nameof(ChannelViewModel.UnreadCount), raised);
        Assert.Contains(nameof(ChannelViewModel.HasUnread), raised);
    }

    [Fact]
    public void UnreadCount_SetToSameValue_DoesNotRaise()
    {
        var ch = TextChannel();
        ch.UnreadCount = 2;

        var raised = false;
        ch.PropertyChanged += (_, _) => raised = true;

        ch.UnreadCount = 2;

        Assert.False(raised);
    }

    // ── MentionCount changes ────────────────────────────────────────────────

    [Fact]
    public void MentionCount_Increment_SetsHasMentionTrue()
    {
        var ch = TextChannel();
        ch.MentionCount = 1;

        Assert.True(ch.HasMention);
        Assert.Equal(1, ch.MentionCount);
    }

    [Fact]
    public void MentionCount_SetToZero_SetsHasMentionFalse()
    {
        var ch = TextChannel();
        ch.MentionCount = 3;
        ch.MentionCount = 0;

        Assert.False(ch.HasMention);
    }

    [Fact]
    public void MentionCount_RaisesPropertyChanged()
    {
        var ch = TextChannel();
        var raised = new List<string?>();
        ch.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        ch.MentionCount = 2;

        Assert.Contains(nameof(ChannelViewModel.MentionCount), raised);
        Assert.Contains(nameof(ChannelViewModel.HasMention), raised);
    }

    [Fact]
    public void MentionCount_SetToSameValue_DoesNotRaise()
    {
        var ch = TextChannel();
        ch.MentionCount = 1;

        var raised = false;
        ch.PropertyChanged += (_, _) => raised = true;

        ch.MentionCount = 1;

        Assert.False(raised);
    }

    // ── Simulated unread workflow ──────────────────────────────────────────

    [Fact]
    public void UnreadWorkflow_IncrementThenClear()
    {
        var ch = TextChannel();

        // Simulate receiving messages while channel is not selected
        ch.UnreadCount++;
        ch.UnreadCount++;
        ch.UnreadCount++;
        Assert.Equal(3, ch.UnreadCount);
        Assert.True(ch.HasUnread);

        // Simulate selecting the channel (clears unread)
        ch.UnreadCount = 0;
        Assert.False(ch.HasUnread);
    }

    [Fact]
    public void MentionWorkflow_SetThenClearOnSelect()
    {
        var ch = TextChannel();

        // Simulate receiving mentions
        ch.UnreadCount = 1;
        ch.MentionCount = 1;
        Assert.True(ch.HasMention);
        Assert.True(ch.HasUnread);

        // Simulate selecting the channel (clears both)
        ch.UnreadCount = 0;
        ch.MentionCount = 0;
        Assert.False(ch.HasMention);
        Assert.False(ch.HasUnread);
    }

    // ── Multiple channels unread state ─────────────────────────────────────

    [Fact]
    public void MultipleChannels_IndependentUnreadState()
    {
        var ch1 = TextChannel(1, "general");
        var ch2 = TextChannel(2, "random");

        ch1.UnreadCount = 5;
        ch2.UnreadCount = 2;
        ch2.MentionCount = 1;

        Assert.True(ch1.HasUnread);
        Assert.False(ch1.HasMention);
        Assert.True(ch2.HasUnread);
        Assert.True(ch2.HasMention);

        // Clearing one doesn't affect the other
        ch1.UnreadCount = 0;
        Assert.False(ch1.HasUnread);
        Assert.True(ch2.HasUnread);
        Assert.True(ch2.HasMention);
    }
}

/// <summary>
/// Tests for MentionParser — the regex-based parser that splits message
/// content into plain text and @mention segments for highlighting.
/// Only connected usernames are treated as valid mentions.
/// </summary>
public class MentionParserTests
{
    private static readonly string[] ServerUsers = ["alice", "bob", "user_name_123", "admin"];

    // ── No validUsernames (null) — legacy/fallback: all @words are mentions ──

    [Fact]
    public void Parse_NullUsernames_AllAtWordsAreMentions()
    {
        var segments = MentionParser.Parse("Hey @anyone check this", null);

        var mentions = segments.Where(s => s.IsMention).ToList();
        Assert.Single(mentions);
        Assert.Equal("@anyone", mentions[0].Text);
    }

    // ── With validUsernames — only matching names are highlighted ────────────

    [Fact]
    public void Parse_NoMentions_ReturnsSinglePlainSegment()
    {
        var segments = MentionParser.Parse("Hello world", ServerUsers);

        Assert.Single(segments);
        Assert.Equal("Hello world", segments[0].Text);
        Assert.False(segments[0].IsMention);
    }

    [Fact]
    public void Parse_ValidMention_IsHighlighted()
    {
        var segments = MentionParser.Parse("Hey @alice how are you?", ServerUsers);

        Assert.Equal(3, segments.Count);
        Assert.Equal("Hey ", segments[0].Text);
        Assert.False(segments[0].IsMention);
        Assert.Equal("@alice", segments[1].Text);
        Assert.True(segments[1].IsMention);
        Assert.Equal(" how are you?", segments[2].Text);
        Assert.False(segments[2].IsMention);
    }

    [Fact]
    public void Parse_InvalidMention_NotHighlighted()
    {
        var segments = MentionParser.Parse("Hey @stranger what's up?", ServerUsers);

        // @stranger is not in ServerUsers, so it should NOT be a mention
        Assert.DoesNotContain(segments, s => s.IsMention);
        // But the text is preserved (collapsed into plain segments)
        var fullText = string.Concat(segments.Select(s => s.Text));
        Assert.Equal("Hey @stranger what's up?", fullText);
    }

    [Fact]
    public void Parse_MixedValidAndInvalid_OnlyValidHighlighted()
    {
        var segments = MentionParser.Parse("@alice and @stranger and @bob", ServerUsers);

        var mentions = segments.Where(s => s.IsMention).ToList();
        Assert.Equal(2, mentions.Count);
        Assert.Equal("@alice", mentions[0].Text);
        Assert.Equal("@bob", mentions[1].Text);

        // @stranger should be plain text, not a mention
        var strangerSegment = segments.First(s => s.Text.Contains("@stranger"));
        Assert.False(strangerSegment.IsMention);
    }

    [Fact]
    public void Parse_MultipleMentions_AllValidHighlighted()
    {
        var segments = MentionParser.Parse("@alice and @bob check this", ServerUsers);

        var mentions = segments.Where(s => s.IsMention).ToList();
        Assert.Equal(2, mentions.Count);
        Assert.Equal("@alice", mentions[0].Text);
        Assert.Equal("@bob", mentions[1].Text);
    }

    [Fact]
    public void Parse_CaseInsensitive_MatchesRegardlessOfCase()
    {
        var segments = MentionParser.Parse("Hey @ALICE and @Bob", ServerUsers);

        var mentions = segments.Where(s => s.IsMention).ToList();
        Assert.Equal(2, mentions.Count);
        Assert.Equal("@ALICE", mentions[0].Text);
        Assert.Equal("@Bob", mentions[1].Text);
    }

    [Fact]
    public void Parse_MentionAtStart_NoLeadingPlainSegment()
    {
        var segments = MentionParser.Parse("@admin please help", ServerUsers);

        Assert.Equal("@admin", segments[0].Text);
        Assert.True(segments[0].IsMention);
    }

    [Fact]
    public void Parse_MentionAtEnd_NoTrailingPlainSegment()
    {
        var segments = MentionParser.Parse("Thanks @bob", ServerUsers);

        Assert.Equal(2, segments.Count);
        Assert.Equal("Thanks ", segments[0].Text);
        Assert.Equal("@bob", segments[1].Text);
        Assert.True(segments[1].IsMention);
    }

    [Fact]
    public void Parse_OnlyMention_SingleMentionSegment()
    {
        var segments = MentionParser.Parse("@alice", ServerUsers);

        Assert.Single(segments);
        Assert.Equal("@alice", segments[0].Text);
        Assert.True(segments[0].IsMention);
    }

    [Fact]
    public void Parse_MentionWithUnderscores_IncludesFullUsername()
    {
        var segments = MentionParser.Parse("Hello @user_name_123!", ServerUsers);

        var mention = segments.First(s => s.IsMention);
        Assert.Equal("@user_name_123", mention.Text);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmpty()
    {
        var segments = MentionParser.Parse("", ServerUsers);
        Assert.Empty(segments);
    }

    [Fact]
    public void Parse_NullString_ReturnsEmpty()
    {
        var segments = MentionParser.Parse(null!, ServerUsers);
        Assert.Empty(segments);
    }

    [Fact]
    public void Parse_AtSignAlone_NotAMention()
    {
        var segments = MentionParser.Parse("email me @ home", ServerUsers);

        Assert.True(segments.All(s => !s.IsMention));
    }

    [Fact]
    public void Parse_UnknownAtWord_NotHighlighted()
    {
        // "everyone" is not in ServerUsers — should not be highlighted
        var segments = MentionParser.Parse("@everyone look", ServerUsers);

        Assert.DoesNotContain(segments, s => s.IsMention);
    }

    [Fact]
    public void Parse_EmptyUserList_NoMentionsHighlighted()
    {
        var segments = MentionParser.Parse("Hey @alice", Array.Empty<string>());

        Assert.DoesNotContain(segments, s => s.IsMention);
    }

    [Fact]
    public void Parse_PreservesFullText_RegardlessOfMentionValidity()
    {
        var input = "Hey @alice and @stranger and @bob!";
        var segments = MentionParser.Parse(input, ServerUsers);

        var reconstructed = string.Concat(segments.Select(s => s.Text));
        Assert.Equal(input, reconstructed);
    }
}
