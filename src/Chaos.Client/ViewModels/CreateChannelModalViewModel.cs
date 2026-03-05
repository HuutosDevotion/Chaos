using System.Windows.Input;
using Chaos.Shared;

namespace Chaos.Client.ViewModels;

public class CreateChannelModalViewModel : SubmenuViewModel
{
    private string _channelName = string.Empty;
    private bool _isVoiceType;

    private readonly Func<string, ChannelType, Task> _confirm;

    public string ChannelName
    {
        get => _channelName;
        set { _channelName = value; OnPropertyChanged(); }
    }

    public bool IsVoiceType
    {
        get => _isVoiceType;
        set { _isVoiceType = value; OnPropertyChanged(); }
    }

    public ICommand Confirm { get; }
    public ICommand Cancel { get; }

    public CreateChannelModalViewModel(Func<string, ChannelType, Task> confirm)
    {
        _confirm = confirm;
        Confirm = new RelayCommand(
            async _ => { Close(); await _confirm(ChannelName.Trim(), IsVoiceType ? ChannelType.Voice : ChannelType.Text); },
            _ => !string.IsNullOrWhiteSpace(ChannelName));
        Cancel = new RelayCommand(_ => Close());
    }
}
