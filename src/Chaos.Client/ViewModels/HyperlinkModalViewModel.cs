using System.Windows.Input;

namespace Chaos.Client.ViewModels;

public class HyperlinkModalViewModel : SubmenuViewModel
{
    private string _url;
    private string _displayText;
    private readonly Action<string, string> _confirm;

    public string Url
    {
        get => _url;
        set { _url = value; OnPropertyChanged(); }
    }

    public string DisplayText
    {
        get => _displayText;
        set { _displayText = value; OnPropertyChanged(); }
    }

    public ICommand Confirm { get; }
    public ICommand Cancel { get; }

    public HyperlinkModalViewModel(string initialUrl, string initialDisplay,
                                   Action<string, string> confirm)
    {
        _url = initialUrl;
        _displayText = initialDisplay;
        _confirm = confirm;
        Confirm = new RelayCommand(
            _ => { Close(); _confirm(Url.Trim(), DisplayText.Trim()); },
            _ => !string.IsNullOrWhiteSpace(Url));
        Cancel = new RelayCommand(_ => Close());
    }
}
