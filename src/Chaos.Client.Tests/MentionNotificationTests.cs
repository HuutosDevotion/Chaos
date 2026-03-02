using Chaos.Client.ViewModels;
using Chaos.Shared;
using Xunit;

namespace Chaos.Client.Tests;

/// <summary>
/// Regression tests for the unread-indicator and @ mention notification system:
/// ChannelViewModel.UnreadCount, MentionCount, HasUnread, HasMentions, and
/// AppSettings.ShowInbox.
/// </summary>
public class MentionNotificationTests
{
    private static ChannelViewModel TextChannel() =>
        new(new ChannelDto { Id = 1, Name = "general", Type = ChannelType.Text });

    // ── UnreadCount defaults ──────────────────────────────────────────────────

    [Fact]
    public void UnreadCount_DefaultsToZero() => Assert.Equal(0, TextChannel().UnreadCount);

    [Fact]
    public void MentionCount_DefaultsToZero() => Assert.Equal(0, TextChannel().MentionCount);

    // ── HasUnread logic ───────────────────────────────────────────────────────

    [Fact]
    public void HasUnread_WhenUnreadZero_IsFalse() => Assert.False(TextChannel().HasUnread);

    [Fact]
    public void HasUnread_WhenUnreadPositiveAndNotSelected_IsTrue()
    {
        var vm = TextChannel();
        vm.UnreadCount = 3;
        Assert.True(vm.HasUnread);
    }

    [Fact]
    public void HasUnread_WhenUnreadPositiveButChannelIsSelected_IsFalse()
    {
        var vm = TextChannel();
        vm.IsSelected = true;
        vm.UnreadCount = 5;
        Assert.False(vm.HasUnread);
    }

    [Fact]
    public void HasUnread_SelectingChannelWithPendingUnread_BecomesFalse()
    {
        var vm = TextChannel();
        vm.UnreadCount = 2;
        Assert.True(vm.HasUnread);

        vm.IsSelected = true;
        Assert.False(vm.HasUnread);
    }

    [Fact]
    public void HasUnread_DeselectingChannelWithUnread_BecomesTrue()
    {
        var vm = TextChannel();
        vm.IsSelected = true;
        vm.UnreadCount = 1;
        Assert.False(vm.HasUnread);

        vm.IsSelected = false;
        Assert.True(vm.HasUnread);
    }

    // ── HasMentions logic ─────────────────────────────────────────────────────

    [Fact]
    public void HasMentions_WhenMentionCountZero_IsFalse() => Assert.False(TextChannel().HasMentions);

    [Fact]
    public void HasMentions_WhenMentionCountPositive_IsTrue()
    {
        var vm = TextChannel();
        vm.MentionCount = 1;
        Assert.True(vm.HasMentions);
    }

    [Fact]
    public void HasMentions_IsIndependentOfIsSelected()
    {
        var vm = TextChannel();
        vm.IsSelected = true;
        vm.MentionCount = 2;
        Assert.True(vm.HasMentions);
    }

    [Fact]
    public void HasMentions_AfterResettingToZero_IsFalse()
    {
        var vm = TextChannel();
        vm.MentionCount = 3;
        Assert.True(vm.HasMentions);

        vm.MentionCount = 0;
        Assert.False(vm.HasMentions);
    }

    // ── UnreadCount PropertyChanged ───────────────────────────────────────────

    [Fact]
    public void UnreadCount_SetNewValue_RaisesUnreadCount()
    {
        var vm = TextChannel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.UnreadCount = 1;

        Assert.Contains(nameof(ChannelViewModel.UnreadCount), raised);
    }

    [Fact]
    public void UnreadCount_SetNewValue_RaisesHasUnread()
    {
        var vm = TextChannel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.UnreadCount = 1;

        Assert.Contains(nameof(ChannelViewModel.HasUnread), raised);
    }

    [Fact]
    public void UnreadCount_SetSameValue_DoesNotRaise()
    {
        var vm = TextChannel();
        vm.UnreadCount = 3;

        var raised = false;
        vm.PropertyChanged += (_, _) => raised = true;

        vm.UnreadCount = 3;
        Assert.False(raised);
    }

    // ── MentionCount PropertyChanged ──────────────────────────────────────────

    [Fact]
    public void MentionCount_SetNewValue_RaisesMentionCount()
    {
        var vm = TextChannel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.MentionCount = 1;

        Assert.Contains(nameof(ChannelViewModel.MentionCount), raised);
    }

    [Fact]
    public void MentionCount_SetNewValue_RaisesHasMentions()
    {
        var vm = TextChannel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.MentionCount = 1;

        Assert.Contains(nameof(ChannelViewModel.HasMentions), raised);
    }

    [Fact]
    public void MentionCount_SetSameValue_DoesNotRaise()
    {
        var vm = TextChannel();
        vm.MentionCount = 2;

        var raised = false;
        vm.PropertyChanged += (_, _) => raised = true;

        vm.MentionCount = 2;
        Assert.False(raised);
    }

    // ── IsSelected raises HasUnread (regression) ──────────────────────────────

    [Fact]
    public void IsSelected_SetTrue_RaisesHasUnread()
    {
        var vm = TextChannel();
        vm.UnreadCount = 1;

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsSelected = true;

        Assert.Contains(nameof(ChannelViewModel.HasUnread), raised);
    }

    [Fact]
    public void IsSelected_SetFalse_RaisesHasUnread()
    {
        var vm = TextChannel();
        vm.IsSelected = true;
        vm.UnreadCount = 1;

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsSelected = false;

        Assert.Contains(nameof(ChannelViewModel.HasUnread), raised);
    }

    // ── AppSettings.ShowInbox ─────────────────────────────────────────────────

    [Fact]
    public void ShowInbox_DefaultsTrue() => Assert.True(new AppSettings().ShowInbox);

    [Fact]
    public void ShowInbox_SetFalse_RaisesPropertyChanged()
    {
        var s = new AppSettings();
        var raised = new List<string?>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        s.ShowInbox = false;

        Assert.Contains(nameof(AppSettings.ShowInbox), raised);
    }

    [Fact]
    public void ShowInbox_SetSameValue_DoesNotRaise()
    {
        var s = new AppSettings();
        var raised = false;
        s.PropertyChanged += (_, _) => raised = true;

        s.ShowInbox = true; // same as default

        Assert.False(raised);
    }

    [Fact]
    public void ShowInbox_ToggleBackAndForth_RaisesEachTime()
    {
        var s = new AppSettings(); // starts true
        var count = 0;
        s.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.ShowInbox)) count++;
        };

        s.ShowInbox = false;
        s.ShowInbox = true;

        Assert.Equal(2, count);
    }
}
