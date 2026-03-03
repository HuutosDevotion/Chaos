using System.Windows.Input;

namespace Chaos.Client.ViewModels;

public class SettingsModalViewModel : SubmenuViewModel
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

    public ICommand CloseCommand { get; }

    public SettingsModalViewModel(AppSettings settings)
    {
        Action<SettingsPageViewModel> select = p => SelectedPage = p;

        var appearance = new AppearanceSettingsViewModel(settings, select);
        var voice = new VoiceSettingsViewModel(settings, select);

        Categories = new List<SettingsCategoryViewModel>
        {
            new("App Settings", new SettingsPageViewModel[] { appearance, voice })
        };

        CloseCommand = new RelayCommand(_ => { voice.StopMicTest(); voice.StopThresholdMonitor(); Close(); });
        SelectedPage = appearance;
    }
}
