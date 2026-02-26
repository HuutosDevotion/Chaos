using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using NAudio.Wave;

namespace Chaos.Client.ViewModels;

public abstract class SettingsPageViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Name { get; }
    public ICommand Select { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }

    protected SettingsPageViewModel(string name, Action<SettingsPageViewModel> select)
    {
        Name = name;
        Select = new RelayCommand(_ => select(this));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class AppearanceSettingsViewModel : SettingsPageViewModel
{
    public AppSettings Settings { get; }

    public AppearanceSettingsViewModel(AppSettings settings, Action<SettingsPageViewModel> select)
        : base("Appearance", select)
    {
        Settings = settings;
    }
}

public class VoiceSettingsViewModel : SettingsPageViewModel
{
    public AppSettings Settings { get; }
    public ObservableCollection<string> InputDevices { get; } = new();
    public ObservableCollection<string> OutputDevices { get; } = new();

    public VoiceSettingsViewModel(AppSettings settings, Action<SettingsPageViewModel> select)
        : base("Voice", select)
    {
        Settings = settings;

        InputDevices.Add("Default");
        try
        {
            for (int i = 0; i < WaveInEvent.DeviceCount; i++)
                InputDevices.Add(WaveInEvent.GetCapabilities(i).ProductName);
        }
        catch { /* no capture devices available */ }

        OutputDevices.Add("Default");
        try
        {
            for (int i = 0; i < WaveOut.DeviceCount; i++)
                OutputDevices.Add(WaveOut.GetCapabilities(i).ProductName);
        }
        catch { /* no playback devices available */ }
    }
}

public class SettingsCategoryViewModel
{
    public string Name { get; }
    public IReadOnlyList<SettingsPageViewModel> Pages { get; }

    public SettingsCategoryViewModel(string name, IEnumerable<SettingsPageViewModel> pages)
    {
        Name = name;
        Pages = pages.ToList();
    }
}

public class SettingsModalViewModel : INotifyPropertyChanged
{
    private SettingsPageViewModel? _selectedPage;

    public IReadOnlyList<SettingsCategoryViewModel> Categories { get; }

    public SettingsPageViewModel? SelectedPage
    {
        get => _selectedPage;
        private set
        {
            if (_selectedPage is not null) _selectedPage.IsSelected = false;
            _selectedPage = value;
            if (_selectedPage is not null) _selectedPage.IsSelected = true;
            OnPropertyChanged();
        }
    }

    public ICommand Close { get; }

    public SettingsModalViewModel(AppSettings settings, Action close)
    {
        Action<SettingsPageViewModel> select = p => SelectedPage = p;

        var appearance = new AppearanceSettingsViewModel(settings, select);
        var voice = new VoiceSettingsViewModel(settings, select);

        Categories = new List<SettingsCategoryViewModel>
        {
            new("App Settings", new SettingsPageViewModel[] { appearance, voice })
        };

        Close = new RelayCommand(_ => close());
        SelectedPage = appearance;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
