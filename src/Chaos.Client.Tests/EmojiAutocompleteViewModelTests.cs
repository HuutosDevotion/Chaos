using Chaos.Client.Services;
using Chaos.Client.ViewModels;
using Xunit;

namespace Chaos.Client.Tests;

public class EmojiAutocompleteViewModelTests
{
    private readonly EmojiService _emojiService;
    private readonly EmojiAutocompleteViewModel _vm;
    private readonly Action<Action> _dispatch;

    public EmojiAutocompleteViewModelTests()
    {
        _emojiService = new EmojiService();
        _emojiService.Initialize("testuser", new InMemoryKeyValueStore());
        _vm = new EmojiAutocompleteViewModel();
        _dispatch = action => action();
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public void Update_ColonPrefixWithQuery_ShowsSuggestions()
    {
        _vm.Update(":grinning", 9, _emojiService, false, _dispatch);
        Assert.True(_vm.IsOpen);
        Assert.NotEmpty(_vm.Suggestions);
    }

    [Fact]
    public void Update_NoColonPrefix_Dismissed()
    {
        _vm.Update("hello", 5, _emojiService, false, _dispatch);
        Assert.False(_vm.IsOpen);
    }

    [Fact]
    public void Update_SingleCharAfterColon_Dismissed()
    {
        _vm.Update(":s", 2, _emojiService, false, _dispatch);
        Assert.False(_vm.IsOpen);
    }

    [Fact]
    public void Update_ExactMatch_PopulatesSuggestions()
    {
        _vm.Update(":star", 5, _emojiService, false, _dispatch);
        Assert.True(_vm.IsOpen);
        Assert.Contains(_vm.Suggestions, s => s.CommandName == ":star:");
    }

    [Fact]
    public void Update_SlashOpen_Dismissed()
    {
        _vm.Update(":grinning", 9, _emojiService, true, _dispatch);
        Assert.False(_vm.IsOpen);
    }

    [Fact]
    public void Update_NoMatches_Dismissed()
    {
        _vm.Update(":zzzznotreal", 12, _emojiService, false, _dispatch);
        Assert.False(_vm.IsOpen);
    }

    [Fact]
    public void Update_Max15Results()
    {
        _vm.Update(":face", 5, _emojiService, false, _dispatch);
        Assert.True(_vm.Suggestions.Count <= 15);
    }

    // ── Navigate ─────────────────────────────────────────────────────────────

    [Fact]
    public void Navigate_DownFromMinusOne_GoesToZero()
    {
        _vm.Update(":grinning", 9, _emojiService, false, _dispatch);
        Assert.True(_vm.Suggestions.Count > 0);

        _vm.Navigate(1);
        Assert.Equal(0, _vm.SelectedIndex);
    }

    [Fact]
    public void Navigate_DownWrapsToZeroAtEnd()
    {
        _vm.Update(":grinning", 9, _emojiService, false, _dispatch);
        int count = _vm.Suggestions.Count;
        Assert.True(count > 0);

        // Navigate past the last item
        for (int i = 0; i <= count; i++) _vm.Navigate(1);

        Assert.Equal(0, _vm.SelectedIndex);
    }

    [Fact]
    public void Navigate_UpFromZeroWrapsToLast()
    {
        _vm.Update(":grinning", 9, _emojiService, false, _dispatch);
        int count = _vm.Suggestions.Count;
        Assert.True(count > 0);

        _vm.Navigate(1);  // go to 0
        _vm.Navigate(-1); // wrap to last
        Assert.Equal(count - 1, _vm.SelectedIndex);
    }

    // ── Dismiss ──────────────────────────────────────────────────────────────

    [Fact]
    public void Dismiss_SetsIsOpenFalseAndResetsIndex()
    {
        _vm.Update(":grinning", 9, _emojiService, false, _dispatch);
        Assert.True(_vm.IsOpen);

        _vm.Dismiss();
        Assert.False(_vm.IsOpen);
        Assert.Equal(-1, _vm.SelectedIndex);
    }

    // ── Suggestions format ───────────────────────────────────────────────────

    [Fact]
    public void Suggestions_CommandNameInColonFormat()
    {
        _vm.Update(":grinning", 9, _emojiService, false, _dispatch);
        Assert.NotEmpty(_vm.Suggestions);
        Assert.All(_vm.Suggestions, s =>
        {
            Assert.StartsWith(":", s.CommandName);
            Assert.EndsWith(":", s.CommandName);
        });
    }
}
