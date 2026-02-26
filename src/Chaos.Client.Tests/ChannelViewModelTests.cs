using System.ComponentModel;
using Chaos.Client.ViewModels;
using Chaos.Shared;
using Xunit;

namespace Chaos.Client.Tests;

/// <summary>
/// Unit tests for ChannelViewModel — the binding model that drives channel
/// highlight states in the sidebar (selected text channel, active voice channel).
/// </summary>
public class ChannelViewModelTests
{
    private static ChannelViewModel TextChannel(string name = "general") =>
        new(new ChannelDto { Id = 1, Name = name, Type = ChannelType.Text });

    private static ChannelViewModel VoiceChannel(string name = "Voice Chat") =>
        new(new ChannelDto { Id = 2, Name = name, Type = ChannelType.Voice });

    // ── defaults ─────────────────────────────────────────────────────────────

    [Fact]
    public void NewChannel_IsSelected_DefaultsFalse()
    {
        Assert.False(TextChannel().IsSelected);
    }

    [Fact]
    public void NewChannel_IsActiveVoice_DefaultsFalse()
    {
        Assert.False(VoiceChannel().IsActiveVoice);
    }

    // ── IsSelected (current text channel highlight) ───────────────────────────

    [Fact]
    public void IsSelected_SetTrue_RaisesPropertyChanged()
    {
        var vm = TextChannel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsSelected = true;

        Assert.Contains(nameof(ChannelViewModel.IsSelected), raised);
    }

    [Fact]
    public void IsSelected_SetToSameValue_DoesNotRaisePropertyChanged()
    {
        var vm = TextChannel();
        vm.IsSelected = true; // establish initial state

        var raised = false;
        vm.PropertyChanged += (_, _) => raised = true;

        vm.IsSelected = true; // same value — should be no-op

        Assert.False(raised);
    }

    [Fact]
    public void IsSelected_SetFalseAfterTrue_RaisesPropertyChanged()
    {
        var vm = TextChannel();
        vm.IsSelected = true;

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsSelected = false;

        Assert.Contains(nameof(ChannelViewModel.IsSelected), raised);
    }

    /// <summary>
    /// Simulates MainViewModel switching the selected text channel:
    /// the old channel loses its highlight, the new one gains it.
    /// </summary>
    [Fact]
    public void SwitchingSelection_DeselectedOldAndSelectsNew()
    {
        var channelA = TextChannel("general");
        var channelB = TextChannel("random");

        // Simulate selecting channelA
        channelA.IsSelected = true;
        Assert.True(channelA.IsSelected);
        Assert.False(channelB.IsSelected);

        // Simulate switching to channelB (as MainViewModel.SelectedTextChannel setter does)
        channelA.IsSelected = false;
        channelB.IsSelected = true;

        Assert.False(channelA.IsSelected);
        Assert.True(channelB.IsSelected);
    }

    // ── IsActiveVoice (active voice channel highlight) ────────────────────────

    [Fact]
    public void IsActiveVoice_SetTrue_RaisesPropertyChanged()
    {
        var vm = VoiceChannel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsActiveVoice = true;

        Assert.Contains(nameof(ChannelViewModel.IsActiveVoice), raised);
    }

    [Fact]
    public void IsActiveVoice_SetToSameValue_DoesNotRaisePropertyChanged()
    {
        var vm = VoiceChannel();
        vm.IsActiveVoice = true;

        var raised = false;
        vm.PropertyChanged += (_, _) => raised = true;

        vm.IsActiveVoice = true;

        Assert.False(raised);
    }

    [Fact]
    public void IsActiveVoice_SetFalseAfterTrue_RaisesPropertyChanged()
    {
        var vm = VoiceChannel();
        vm.IsActiveVoice = true;

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsActiveVoice = false;

        Assert.Contains(nameof(ChannelViewModel.IsActiveVoice), raised);
    }

    /// <summary>
    /// Simulates switching voice channels: the old channel loses the active
    /// indicator, the new one gains it (as JoinVoice does in MainViewModel).
    /// </summary>
    [Fact]
    public void SwitchingVoiceChannel_ClearsPreviousActiveVoice()
    {
        var channelA = VoiceChannel("Voice Chat");
        var channelB = VoiceChannel("Gaming");

        channelA.IsActiveVoice = true;
        Assert.True(channelA.IsActiveVoice);
        Assert.False(channelB.IsActiveVoice);

        // Simulate JoinVoice clearing previous before setting new
        channelA.IsActiveVoice = false;
        channelB.IsActiveVoice = true;

        Assert.False(channelA.IsActiveVoice);
        Assert.True(channelB.IsActiveVoice);
    }

    // ── icons ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TextChannel_HasHashIcon()
    {
        Assert.Equal("#", TextChannel().Icon);
    }

    [Fact]
    public void VoiceChannel_HasSpeakerIcon()
    {
        Assert.Equal("\U0001F50A", VoiceChannel().Icon);
    }

    // ── name ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Name_SetNewValue_RaisesPropertyChanged()
    {
        var vm = TextChannel("general");
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Name = "announcements";

        Assert.Contains(nameof(ChannelViewModel.Name), raised);
        Assert.Equal("announcements", vm.Name);
    }

    [Fact]
    public void Name_SetToSameValue_DoesNotRaisePropertyChanged()
    {
        var vm = TextChannel("general");
        var raised = false;
        vm.PropertyChanged += (_, _) => raised = true;

        vm.Name = "general";

        Assert.False(raised);
    }
}
