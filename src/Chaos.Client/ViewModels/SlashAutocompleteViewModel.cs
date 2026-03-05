using System.Collections.ObjectModel;
using Chaos.Shared;

namespace Chaos.Client.ViewModels;

public class SlashAutocompleteViewModel : SubmenuViewModel
{
    private List<SlashCommandDto> _allCommands = new();
    private int _selectedIndex = -1;

    public SlashAutocompleteViewModel()
    {
        MaxHeight = 200;
    }

    /// <summary>Called on UI thread just before the menu opens, so the view can size/position it.</summary>
    public Action? BeforeOpen { get; set; }

    public ObservableCollection<SlashCommandDto> Suggestions { get; } = new();

    public int SelectedIndex
    {
        get => _selectedIndex;
        set { if (_selectedIndex == value) return; _selectedIndex = value; OnPropertyChanged(); }
    }

    public void SetCommands(List<SlashCommandDto> commands)
    {
        _allCommands = commands;
    }

    public void Update(string text)
    {
        Suggestions.Clear();
        SelectedIndex = -1;

        foreach (var cmd in SlashCommandFilter.Filter(_allCommands, text))
            Suggestions.Add(cmd);

        if (Suggestions.Count > 0)
        {
            BeforeOpen?.Invoke();
            IsOpen = true;
        }
        else
        {
            IsOpen = false;
        }
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
