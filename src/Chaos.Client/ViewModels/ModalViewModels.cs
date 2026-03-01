using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Chaos.Shared;


namespace Chaos.Client.ViewModels;

public class CreateChannelModalViewModel : INotifyPropertyChanged
{
    private string _channelName = string.Empty;
    private bool _isVoiceType;

    private readonly Func<string, ChannelType, Task> _confirm;
    private readonly Action _cancel;

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

    public CreateChannelModalViewModel(Func<string, ChannelType, Task> confirm, Action cancel)
    {
        _confirm = confirm;
        _cancel = cancel;
        Confirm = new RelayCommand(
            async _ => await _confirm(ChannelName.Trim(), IsVoiceType ? ChannelType.Voice : ChannelType.Text),
            _ => !string.IsNullOrWhiteSpace(ChannelName));
        Cancel = new RelayCommand(_ => _cancel());
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class RenameChannelModalViewModel : INotifyPropertyChanged
{
    private string _channelName;

    private readonly Func<string, Task> _confirm;
    private readonly Action _cancel;

    public string ChannelName
    {
        get => _channelName;
        set { _channelName = value; OnPropertyChanged(); }
    }

    public ICommand Confirm { get; }
    public ICommand Cancel { get; }

    public RenameChannelModalViewModel(string initialName, Func<string, Task> confirm, Action cancel)
    {
        _channelName = initialName;
        _confirm = confirm;
        _cancel = cancel;
        Confirm = new RelayCommand(
            async _ => await _confirm(ChannelName.Trim()),
            _ => !string.IsNullOrWhiteSpace(ChannelName));
        Cancel = new RelayCommand(_ => _cancel());
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class DeleteChannelModalViewModel
{
    private readonly Func<Task> _confirm;
    private readonly Action _cancel;

    public string Message { get; }
    public ICommand Confirm { get; }
    public ICommand Cancel { get; }

    public DeleteChannelModalViewModel(string channelName, Func<Task> confirm, Action cancel)
    {
        Message = $"Delete \"{channelName}\"? This cannot be undone.";
        _confirm = confirm;
        _cancel = cancel;
        Confirm = new RelayCommand(async _ => await _confirm());
        Cancel = new RelayCommand(_ => _cancel());
    }
}

public class ImagePreviewModalViewModel
{
    public string ImageUrl { get; }
    public ImagePreviewModalViewModel(string imageUrl) => ImageUrl = imageUrl;
}

public class HyperlinkModalViewModel : INotifyPropertyChanged
{
    private string _url;
    private string _displayText;
    private readonly Action<string, string> _confirm;
    private readonly Action _cancel;

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
                                   Action<string, string> confirm, Action cancel)
    {
        _url = initialUrl;
        _displayText = initialDisplay;
        _confirm = confirm;
        _cancel = cancel;
        Confirm = new RelayCommand(
            _ => _confirm(Url.Trim(), DisplayText.Trim()),
            _ => !string.IsNullOrWhiteSpace(Url));
        Cancel = new RelayCommand(_ => _cancel());
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
