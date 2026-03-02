using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using Chaos.Client.Services;
using Chaos.Shared;

namespace Chaos.Client.ViewModels;

public class EmojiAutocompleteViewModel : SubmenuViewModel
{
    private static readonly Regex Pattern = new(@":([A-Za-z0-9_]{2,})$", RegexOptions.Compiled);

    private int _selectedIndex = -1;

    public EmojiAutocompleteViewModel()
    {
        MaxHeight = 360;
    }

    /// <summary>Called on UI thread just before the menu opens, so the view can size/position it.</summary>
    public Action? BeforeOpen { get; set; }

    public ObservableCollection<EmojiSuggestionItem> Suggestions { get; } = new();

    public int SelectedIndex
    {
        get => _selectedIndex;
        set { if (_selectedIndex == value) return; _selectedIndex = value; OnPropertyChanged(); }
    }

    public void Update(string text, int cursorPos, EmojiService emojiService, bool slashOpen, Action<Action> safeDispatch)
    {
        if (!emojiService.IsLoaded || slashOpen)
        {
            safeDispatch(Dismiss);
            return;
        }

        string textUpToCursor = cursorPos <= text.Length ? text[..cursorPos] : text;
        var match = Pattern.Match(textUpToCursor);
        if (!match.Success)
        {
            safeDispatch(Dismiss);
            return;
        }

        string query = match.Groups[1].Value;
        var results = emojiService.Search(query, 15);

        var items = results.Select(emoji => new EmojiSuggestionItem
        {
            CommandName = $":{emoji.Name}:",
            Emoji = emoji,
            Image = emojiService.GetCachedImage(emoji),
        }).ToList();

        safeDispatch(() =>
        {
            Suggestions.Clear();
            SelectedIndex = -1;
            foreach (var item in items)
                Suggestions.Add(item);
            if (Suggestions.Count > 0)
            {
                BeforeOpen?.Invoke();
                IsOpen = true;
            }
            else
            {
                IsOpen = false;
            }
        });
    }

    public void Dismiss()
    {
        IsOpen = false;
        SelectedIndex = -1;
    }

    public void Navigate(int direction)
    {
        if (Suggestions.Count == 0) return;
        int next = SelectedIndex + direction;
        if (next < 0) next = Suggestions.Count - 1;
        else if (next >= Suggestions.Count) next = 0;
        SelectedIndex = next;
    }
}

public class EmojiSuggestionItem : System.ComponentModel.INotifyPropertyChanged
{
    private BitmapImage? _image;
    public string CommandName { get; set; } = string.Empty;
    public EmojiDto Emoji { get; set; } = new();
    public BitmapImage? Image
    {
        get => _image;
        set { _image = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Image))); }
    }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
