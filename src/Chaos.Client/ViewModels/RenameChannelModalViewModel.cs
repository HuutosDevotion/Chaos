using System.Windows.Input;

namespace Chaos.Client.ViewModels;

public class RenameChannelModalViewModel : SubmenuViewModel
{
    private string _channelName;

    private readonly Func<string, Task> _confirm;

    public string ChannelName
    {
        get => _channelName;
        set { _channelName = value; OnPropertyChanged(); }
    }

    public ICommand Confirm { get; }
    public ICommand Cancel { get; }

    public RenameChannelModalViewModel(string initialName, Func<string, Task> confirm)
    {
        _channelName = initialName;
        _confirm = confirm;
        Confirm = new RelayCommand(
            async _ => { Close(); await _confirm(ChannelName.Trim()); },
            _ => !string.IsNullOrWhiteSpace(ChannelName));
        Cancel = new RelayCommand(_ => Close());
    }
}
