using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Chaos.Client.Services;
using Chaos.Shared;

namespace Chaos.Client.ViewModels;

public class VoiceMemberInfo : INotifyPropertyChanged
{
    private bool _isSpeaking;
    public string Username { get; set; } = string.Empty;
    public int VoiceUserId { get; set; }
    public bool IsSpeaking
    {
        get => _isSpeaking;
        set { _isSpeaking = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSpeaking))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public class ChannelViewModel : INotifyPropertyChanged
{
    public ChannelDto Channel { get; set; } = null!;
    public ObservableCollection<VoiceMemberInfo> VoiceMembers { get; } = new();
    public int Id => Channel.Id;
    public string Name => Channel.Name;
    public ChannelType Type => Channel.Type;
    public string Icon => Type == ChannelType.Voice ? "\U0001F50A" : "#";

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ChatService _chatService = new();
    private readonly VoiceService _voiceService = new();

    private string _serverAddress = "localhost:5000";
    private string _username = string.Empty;
    private string _messageText = string.Empty;
    private string _connectionStatus = "Disconnected";
    private bool _isConnected;
    private bool _isConnecting;
    private bool _isMuted;
    private bool _isDeafened;
    private ChannelViewModel? _selectedTextChannel;
    private int? _voiceChannelId;
    private int _userId;
    private double _micLevel;
    private string _voiceStatus = string.Empty;

    public ObservableCollection<ChannelViewModel> Channels { get; } = new();
    public ObservableCollection<MessageDto> Messages { get; } = new();

    public string ServerAddress
    {
        get => _serverAddress;
        set { _serverAddress = value; OnPropertyChanged(); }
    }

    public string Username
    {
        get => _username;
        set { _username = value; OnPropertyChanged(); }
    }

    public string MessageText
    {
        get => _messageText;
        set { _messageText = value; OnPropertyChanged(); }
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set { _connectionStatus = value; OnPropertyChanged(); }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanConnect)); }
    }

    public bool IsConnecting
    {
        get => _isConnecting;
        set { _isConnecting = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanConnect)); }
    }

    public bool CanConnect => !IsConnected && !IsConnecting;

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            _voiceService.IsMuted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MuteButtonText));
        }
    }

    public bool IsDeafened
    {
        get => _isDeafened;
        set
        {
            _isDeafened = value;
            _voiceService.IsDeafened = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DeafenButtonText));
        }
    }

    public double MicLevel
    {
        get => _micLevel;
        set { _micLevel = value; OnPropertyChanged(); }
    }

    public string VoiceStatus
    {
        get => _voiceStatus;
        set { _voiceStatus = value; OnPropertyChanged(); }
    }

    public string MuteButtonText => IsMuted ? "\U0001F507 Unmute" : "\U0001F3A4 Mute";
    public string DeafenButtonText => IsDeafened ? "\U0001F508 Undeafen" : "\U0001F50A Deafen";

    public bool IsInVoice => _voiceChannelId.HasValue;
    public string VoiceChannelName
    {
        get
        {
            if (!_voiceChannelId.HasValue) return string.Empty;
            var ch = Channels.FirstOrDefault(c => c.Id == _voiceChannelId.Value);
            return ch is not null ? $"Voice Connected - {ch.Name}" : "Voice Connected";
        }
    }

    public ChannelViewModel? SelectedTextChannel
    {
        get => _selectedTextChannel;
        set
        {
            if (_selectedTextChannel?.Id == value?.Id) return;
            _selectedTextChannel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTextChannel));
            OnPropertyChanged(nameof(SelectedChannelName));
            if (value is not null)
                _ = OnTextChannelSelected(value);
        }
    }

    public bool HasTextChannel => _selectedTextChannel is not null;
    public string SelectedChannelName => _selectedTextChannel is not null ? $"# {_selectedTextChannel.Name}" : string.Empty;

    public ICommand ConnectCommand => new RelayCommand(async _ => await ConnectAsync(), _ => CanConnect);
    public ICommand SendMessageCommand => new RelayCommand(async _ => await SendMessageAsync(), _ => IsConnected && !string.IsNullOrWhiteSpace(MessageText));
    public ICommand ToggleMuteCommand => new RelayCommand(_ => IsMuted = !IsMuted);
    public ICommand ToggleDeafenCommand => new RelayCommand(_ => IsDeafened = !IsDeafened);
    public ICommand ChannelClickCommand => new RelayCommand(async p => await OnChannelClicked(p as ChannelViewModel));
    public ICommand DisconnectVoiceCommand => new RelayCommand(async _ => await LeaveVoice());

    public MainViewModel()
    {
        _chatService.MessageReceived += OnMessageReceived;
        _chatService.UserJoinedVoice += OnUserJoinedVoice;
        _chatService.UserLeftVoice += OnUserLeftVoice;
        _chatService.VoiceMembersReceived += OnVoiceMembersReceived;
        _chatService.UserConnected += OnUserConnected;
        _chatService.UserDisconnected += OnUserDisconnected;
        _chatService.Disconnected += OnDisconnected;
        _voiceService.MicLevelChanged += level =>
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                MicLevel = level * 200;
                // Update speaking indicator for self
                if (_voiceChannelId.HasValue)
                {
                    var ch = Channels.FirstOrDefault(c => c.Id == _voiceChannelId.Value);
                    var me = ch?.VoiceMembers.FirstOrDefault(m => m.Username == Username);
                    if (me is not null)
                        me.IsSpeaking = level > 0.02f;
                }
            });
        };
        _voiceService.RemoteAudioLevel += (remoteUserId, level) =>
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (!_voiceChannelId.HasValue) return;
                var ch = Channels.FirstOrDefault(c => c.Id == _voiceChannelId.Value);
                var member = ch?.VoiceMembers.FirstOrDefault(m => m.VoiceUserId == remoteUserId);
                if (member is not null)
                    member.IsSpeaking = level > 0.02f;
            });
        };
        _voiceService.Error += error =>
        {
            Application.Current.Dispatcher.BeginInvoke(() => VoiceStatus = error);
        };

        _userId = Random.Shared.Next(1, 100000);
    }

    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(ServerAddress))
            return;

        IsConnecting = true;
        ConnectionStatus = "Connecting...";

        try
        {
            var url = ServerAddress.StartsWith("http") ? ServerAddress : $"http://{ServerAddress}";
            if (!url.EndsWith("/chathub")) url += "/chathub";

            await _chatService.ConnectAsync(url);
            await _chatService.SetUsername(Username);

            var channels = await _chatService.GetChannels();
            var voiceMembers = await _chatService.GetAllVoiceMembers();

            Application.Current.Dispatcher.Invoke(() =>
            {
                Channels.Clear();
                foreach (var ch in channels)
                {
                    var vm = new ChannelViewModel { Channel = ch };
                    if (voiceMembers.TryGetValue(ch.Id, out var members))
                    {
                        foreach (var m in members)
                            vm.VoiceMembers.Add(new VoiceMemberInfo { Username = m.Username, VoiceUserId = m.VoiceUserId });
                    }
                    Channels.Add(vm);
                }
            });

            IsConnected = true;
            ConnectionStatus = $"Connected as {Username}";
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Failed: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private async Task OnChannelClicked(ChannelViewModel? channel)
    {
        if (channel is null || !IsConnected) return;

        if (channel.Type == ChannelType.Text)
        {
            SelectedTextChannel = channel;
        }
        else if (channel.Type == ChannelType.Voice)
        {
            // Toggle: if already in this voice channel, leave it
            if (_voiceChannelId == channel.Id)
            {
                await LeaveVoice();
            }
            else
            {
                await JoinVoice(channel.Id);
            }
        }
    }

    private async Task OnTextChannelSelected(ChannelViewModel channel)
    {
        await _chatService.JoinTextChannel(channel.Id);
        var messages = await _chatService.GetMessages(channel.Id);
        Application.Current.Dispatcher.Invoke(() =>
        {
            Messages.Clear();
            foreach (var msg in messages)
                Messages.Add(msg);
        });
    }

    private async Task JoinVoice(int channelId)
    {
        VoiceStatus = string.Empty;
        await _chatService.JoinVoiceChannel(channelId, _userId);

        var host = ServerAddress.Replace("http://", "").Replace("https://", "");
        if (host.Contains(':'))
            host = host.Split(':')[0];
        if (string.IsNullOrEmpty(host))
            host = "localhost";

        _voiceService.Start(host, 9000, _userId, channelId);
        _voiceChannelId = channelId;
        OnPropertyChanged(nameof(IsInVoice));
        OnPropertyChanged(nameof(VoiceChannelName));
    }

    private async Task LeaveVoice()
    {
        _voiceService.Stop();
        await _chatService.LeaveVoiceChannel();
        _voiceChannelId = null;
        MicLevel = 0;
        VoiceStatus = string.Empty;
        OnPropertyChanged(nameof(IsInVoice));
        OnPropertyChanged(nameof(VoiceChannelName));
    }

    private async Task SendMessageAsync()
    {
        if (_selectedTextChannel is null || string.IsNullOrWhiteSpace(MessageText))
            return;

        await _chatService.SendMessage(_selectedTextChannel.Id, MessageText);
        MessageText = string.Empty;
    }

    private void OnMessageReceived(MessageDto msg)
    {
        if (msg.ChannelId == _selectedTextChannel?.Id)
            Application.Current.Dispatcher.Invoke(() => Messages.Add(msg));
    }

    private void OnUserJoinedVoice(string username, int channelId, int voiceUserId)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var ch = Channels.FirstOrDefault(c => c.Id == channelId);
            if (ch is not null && !ch.VoiceMembers.Any(m => m.Username == username))
                ch.VoiceMembers.Add(new VoiceMemberInfo { Username = username, VoiceUserId = voiceUserId });
        });
    }

    private void OnUserLeftVoice(string username, int channelId)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var ch = Channels.FirstOrDefault(c => c.Id == channelId);
            var member = ch?.VoiceMembers.FirstOrDefault(m => m.Username == username);
            if (member is not null)
                ch!.VoiceMembers.Remove(member);
        });
    }

    private void OnVoiceMembersReceived(int channelId, List<VoiceMemberDto> members)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var ch = Channels.FirstOrDefault(c => c.Id == channelId);
            if (ch is null) return;
            ch.VoiceMembers.Clear();
            foreach (var m in members)
                ch.VoiceMembers.Add(new VoiceMemberInfo { Username = m.Username, VoiceUserId = m.VoiceUserId });
        });
    }

    private void OnUserConnected(string username) { }

    private void OnUserDisconnected(string username)
    {
        // Remove from all voice channels
        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var ch in Channels)
            {
                var member = ch.VoiceMembers.FirstOrDefault(m => m.Username == username);
                if (member is not null)
                    ch.VoiceMembers.Remove(member);
            }
        });
    }

    private void OnDisconnected()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            IsConnected = false;
            ConnectionStatus = "Disconnected";
            _voiceService.Stop();
            _voiceChannelId = null;
            OnPropertyChanged(nameof(IsInVoice));
            OnPropertyChanged(nameof(VoiceChannelName));
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
