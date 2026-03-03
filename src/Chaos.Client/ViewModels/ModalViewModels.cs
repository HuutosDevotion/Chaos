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

public class DeleteChannelModalViewModel : SubmenuViewModel
{
    private readonly Func<Task> _confirm;

    public string Message { get; }
    public ICommand Confirm { get; }
    public ICommand Cancel { get; }

    public DeleteChannelModalViewModel(string channelName, Func<Task> confirm)
    {
        Message = $"Delete \"{channelName}\"? This cannot be undone.";
        _confirm = confirm;
        Confirm = new RelayCommand(async _ => { Close(); await _confirm(); });
        Cancel = new RelayCommand(_ => Close());
    }
}

public class ImagePreviewModalViewModel : SubmenuViewModel
{
    public string ImageUrl { get; }
    public ImagePreviewModalViewModel(string imageUrl) => ImageUrl = imageUrl;
}

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
