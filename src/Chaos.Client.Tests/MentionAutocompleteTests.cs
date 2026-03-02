using Chaos.Client.ViewModels;
using Xunit;

namespace Chaos.Client.Tests;

/// <summary>
/// Regression tests for the @ mention autocomplete feature in MainViewModel:
/// MentionSuggestions, ShowMentionSuggestions, SelectedMentionSuggestionIndex,
/// and the methods UpdateMentionSuggestions (via MessageText setter),
/// NavigateMentionSuggestions, SelectMentionSuggestion, DismissMentionSuggestions.
/// </summary>
public class MentionAutocompleteTests
{
    private static MainViewModel VmWithUsers(params string[] users)
    {
        var vm = new MainViewModel();
        foreach (var u in users) vm.ConnectedUsers.Add(u);
        return vm;
    }

    // ── Initial state ─────────────────────────────────────────────────────────

    [Fact]
    public void MentionSuggestions_InitialState_IsEmpty() =>
        Assert.Empty(new MainViewModel().MentionSuggestions);

    [Fact]
    public void ShowMentionSuggestions_InitialState_IsFalse() =>
        Assert.False(new MainViewModel().ShowMentionSuggestions);

    [Fact]
    public void SelectedMentionSuggestionIndex_InitialState_IsNegativeOne() =>
        Assert.Equal(-1, new MainViewModel().SelectedMentionSuggestionIndex);

    // ── Trigger conditions ────────────────────────────────────────────────────

    [Fact]
    public void MessageText_AtSignAlone_ShowsAllUsers()
    {
        var vm = VmWithUsers("Alice", "Bob");
        vm.MessageText = "@";
        Assert.True(vm.ShowMentionSuggestions);
        Assert.Equal(2, vm.MentionSuggestions.Count);
    }

    [Fact]
    public void MessageText_AtSignWithPrefix_ShowsMatchingUsersOnly()
    {
        var vm = VmWithUsers("Alice", "Bob", "Albert");
        vm.MessageText = "@Al";
        Assert.True(vm.ShowMentionSuggestions);
        Assert.Contains("Alice",  vm.MentionSuggestions);
        Assert.Contains("Albert", vm.MentionSuggestions);
        Assert.DoesNotContain("Bob", vm.MentionSuggestions);
    }

    [Fact]
    public void MessageText_AtSignFilter_IsCaseInsensitive()
    {
        var vm = VmWithUsers("Alice");
        vm.MessageText = "@al";
        Assert.Contains("Alice", vm.MentionSuggestions);
    }

    [Fact]
    public void MessageText_AtSignNoMatch_SuggestionsHidden()
    {
        var vm = VmWithUsers("Alice");
        vm.MessageText = "@xyz";
        Assert.False(vm.ShowMentionSuggestions);
        Assert.Empty(vm.MentionSuggestions);
    }

    [Fact]
    public void MessageText_NoAtSign_SuggestionsHidden()
    {
        var vm = VmWithUsers("Alice");
        vm.MessageText = "hello";
        Assert.False(vm.ShowMentionSuggestions);
    }

    [Fact]
    public void MessageText_Empty_SuggestionsHidden()
    {
        var vm = VmWithUsers("Alice");
        vm.MessageText = "@";
        Assert.True(vm.ShowMentionSuggestions);
        vm.MessageText = "";
        Assert.False(vm.ShowMentionSuggestions);
    }

    [Fact]
    public void MessageText_MidTextAtSign_ShowsSuggestions()
    {
        var vm = VmWithUsers("Alice");
        vm.MessageText = "hey @Ali";
        Assert.True(vm.ShowMentionSuggestions);
        Assert.Contains("Alice", vm.MentionSuggestions);
    }

    [Fact]
    public void MessageText_CompletedMentionWithTrailingSpace_HidesSuggestions()
    {
        // "@Alice " ends with a space — the trailing-@ regex won't match
        var vm = VmWithUsers("Alice");
        vm.MessageText = "@Alice ";
        Assert.False(vm.ShowMentionSuggestions);
    }

    [Fact]
    public void MessageText_NoUsersConnected_SuggestionsHidden()
    {
        var vm = new MainViewModel();
        vm.MessageText = "@";
        Assert.False(vm.ShowMentionSuggestions);
        Assert.Empty(vm.MentionSuggestions);
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    [Fact]
    public void NavigateMentionSuggestions_DownFromStart_SelectsFirstItem()
    {
        var vm = VmWithUsers("Alice", "Bob");
        vm.MessageText = "@";
        vm.NavigateMentionSuggestions(1);
        Assert.Equal(0, vm.SelectedMentionSuggestionIndex);
    }

    [Fact]
    public void NavigateMentionSuggestions_DownFromLast_WrapsToFirst()
    {
        var vm = VmWithUsers("Alice", "Bob");
        vm.MessageText = "@";
        vm.SelectedMentionSuggestionIndex = 1; // last of 2
        vm.NavigateMentionSuggestions(1);
        Assert.Equal(0, vm.SelectedMentionSuggestionIndex);
    }

    [Fact]
    public void NavigateMentionSuggestions_UpFromFirst_WrapsToLast()
    {
        var vm = VmWithUsers("Alice", "Bob");
        vm.MessageText = "@";
        vm.SelectedMentionSuggestionIndex = 0;
        vm.NavigateMentionSuggestions(-1);
        Assert.Equal(1, vm.SelectedMentionSuggestionIndex);
    }

    [Fact]
    public void NavigateMentionSuggestions_EmptyList_NoChange()
    {
        var vm = new MainViewModel();
        vm.NavigateMentionSuggestions(1); // should not throw
        Assert.Equal(-1, vm.SelectedMentionSuggestionIndex);
    }

    [Fact]
    public void NavigateMentionSuggestions_MultipleSteps_CorrectIndex()
    {
        var vm = VmWithUsers("Alice", "Bob", "Carol");
        vm.MessageText = "@";
        vm.NavigateMentionSuggestions(1); // 0
        vm.NavigateMentionSuggestions(1); // 1
        vm.NavigateMentionSuggestions(1); // 2
        Assert.Equal(2, vm.SelectedMentionSuggestionIndex);
    }

    // ── SelectMentionSuggestion ───────────────────────────────────────────────

    [Fact]
    public void SelectMentionSuggestion_ReplacesAtTokenWithUsername()
    {
        var vm = VmWithUsers("Alice");
        vm.MessageText = "@Ali";
        vm.SelectMentionSuggestion("Alice");
        Assert.StartsWith("@Alice", vm.MessageText);
    }

    [Fact]
    public void SelectMentionSuggestion_AddsTrailingSpace()
    {
        var vm = VmWithUsers("Alice");
        vm.MessageText = "@Ali";
        vm.SelectMentionSuggestion("Alice");
        Assert.EndsWith(" ", vm.MessageText);
    }

    [Fact]
    public void SelectMentionSuggestion_PreservesPrecedingText()
    {
        var vm = VmWithUsers("Alice");
        vm.MessageText = "hey @Ali";
        vm.SelectMentionSuggestion("Alice");
        Assert.StartsWith("hey @Alice", vm.MessageText);
    }

    [Fact]
    public void SelectMentionSuggestion_AtSignAlone_ProducesAtUsernameSpace()
    {
        var vm = VmWithUsers("Alice");
        vm.MessageText = "@";
        vm.SelectMentionSuggestion("Alice");
        Assert.Equal("@Alice ", vm.MessageText);
    }

    [Fact]
    public void SelectMentionSuggestion_DismissesSuggestions()
    {
        var vm = VmWithUsers("Alice");
        vm.MessageText = "@Al";
        vm.SelectMentionSuggestion("Alice");
        Assert.False(vm.ShowMentionSuggestions);
        Assert.Empty(vm.MentionSuggestions);
        Assert.Equal(-1, vm.SelectedMentionSuggestionIndex);
    }

    [Fact]
    public void SelectMentionSuggestion_ResultingTextHidesNewSuggestions()
    {
        // "@Alice " ends with space, so no new @ trigger
        var vm = VmWithUsers("Alice");
        vm.MessageText = "@Al";
        vm.SelectMentionSuggestion("Alice");
        Assert.False(vm.ShowMentionSuggestions);
    }

    // ── DismissMentionSuggestions ─────────────────────────────────────────────

    [Fact]
    public void DismissMentionSuggestions_ClearsAllState()
    {
        var vm = VmWithUsers("Alice");
        vm.MessageText = "@Al";
        vm.SelectedMentionSuggestionIndex = 0;

        vm.DismissMentionSuggestions();

        Assert.False(vm.ShowMentionSuggestions);
        Assert.Empty(vm.MentionSuggestions);
        Assert.Equal(-1, vm.SelectedMentionSuggestionIndex);
    }

    [Fact]
    public void DismissMentionSuggestions_WhenAlreadyHidden_NoChange()
    {
        var vm = new MainViewModel();
        vm.DismissMentionSuggestions(); // should not throw
        Assert.False(vm.ShowMentionSuggestions);
    }

    // ── PropertyChanged notifications ─────────────────────────────────────────

    [Fact]
    public void ShowMentionSuggestions_SetTrue_RaisesPropertyChanged()
    {
        var vm = VmWithUsers("Alice");
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.MessageText = "@Al";

        Assert.Contains(nameof(MainViewModel.ShowMentionSuggestions), raised);
    }

    [Fact]
    public void ShowMentionSuggestions_Dismiss_RaisesPropertyChanged()
    {
        var vm = VmWithUsers("Alice");
        vm.MessageText = "@Al";

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.DismissMentionSuggestions();

        Assert.Contains(nameof(MainViewModel.ShowMentionSuggestions), raised);
    }

    [Fact]
    public void SelectedMentionSuggestionIndex_Navigate_RaisesPropertyChanged()
    {
        var vm = VmWithUsers("Alice", "Bob");
        vm.MessageText = "@";

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.NavigateMentionSuggestions(1);

        Assert.Contains(nameof(MainViewModel.SelectedMentionSuggestionIndex), raised);
    }

    [Fact]
    public void SelectedMentionSuggestionIndex_ResetOnNewSuggestions_RaisesPropertyChanged()
    {
        var vm = VmWithUsers("Alice");
        vm.MessageText = "@";
        vm.SelectedMentionSuggestionIndex = 0;

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.MessageText = "@Al"; // triggers UpdateMentionSuggestions, resets index to -1

        Assert.Contains(nameof(MainViewModel.SelectedMentionSuggestionIndex), raised);
        Assert.Equal(-1, vm.SelectedMentionSuggestionIndex);
    }

    // ── ConnectedUsers reflected in suggestions ───────────────────────────────

    [Fact]
    public void MentionSuggestions_ReflectsConnectedUsersAtTimeOfTyping()
    {
        var vm = new MainViewModel();
        vm.MessageText = "@";
        Assert.Empty(vm.MentionSuggestions);

        vm.ConnectedUsers.Add("Alice");
        vm.MessageText = "@A"; // re-trigger — now Alice is present
        Assert.Contains("Alice", vm.MentionSuggestions);
    }

    [Fact]
    public void MentionSuggestions_UserRemovedBeforeTyping_NotIncluded()
    {
        var vm = VmWithUsers("Alice", "Bob");
        vm.ConnectedUsers.Remove("Bob");
        vm.MessageText = "@";
        Assert.DoesNotContain("Bob", vm.MentionSuggestions);
    }
}
