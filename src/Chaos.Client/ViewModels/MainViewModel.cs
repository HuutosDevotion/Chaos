using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Chaos.Client.Services;
using Chaos.Shared;

namespace Chaos.Client.ViewModels;

public class VoiceMemberInfo : INotifyPropertyChanged
{
    private bool _isSpeaking;
    private float _volume = 1.0f;
    public string Username { get; set; } = string.Empty;
    public int VoiceUserId { get; set; }
    public bool IsSpeaking
    {
        get => _isSpeaking;
        set { _isSpeaking = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSpeaking))); }
    }
    public float Volume
    {
        get => _volume;
        set
        {
            if (Math.Abs(_volume - value) < 0.001f) return;
            _volume = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Volume)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public class ChannelViewModel : INotifyPropertyChanged
{
    private string _name;
    private bool _isSelected;
    private bool _isActiveVoice;
    public ChannelViewModel(ChannelDto channel) { Channel = channel; _name = channel.Name; }
    public ChannelDto Channel { get; set; }
    public ObservableCollection<VoiceMemberInfo> VoiceMembers { get; } = new();
    public int Id => Channel.Id;
    public ChannelType Type => Channel.Type;
    public string Icon => Type == ChannelType.Voice ? "\U0001F50A" : "#";
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }
    public bool IsActiveVoice
    {
        get => _isActiveVoice;
        set { if (_isActiveVoice == value) return; _isActiveVoice = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActiveVoice))); }
    }
    public string Name
    {
        get => _name;
        set { if (_name == value) return; _name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly ChatService _chatService = new();
    private readonly VoiceService _voiceService = new();

    public AppSettings Settings { get; } = new();

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
    private byte[]? _pendingImageData;
    private string _pendingImageFilename = string.Empty;
    private BitmapSource? _pendingImagePreview;
    private List<SlashCommandDto> _allCommands = new();
    private int _selectedSuggestionIndex = -1;
    private bool _showSlashSuggestions;

    private object? _activeModal;

    public ObservableCollection<ChannelViewModel> Channels { get; } = new();
    public ObservableCollection<MessageDto> Messages { get; } = new();
    public ObservableCollection<SlashCommandDto> SlashSuggestions { get; } = new();

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
        set { _messageText = value; OnPropertyChanged(); UpdateSlashSuggestions(value); }
    }

    public bool ShowSlashSuggestions
    {
        get => _showSlashSuggestions;
        set { _showSlashSuggestions = value; OnPropertyChanged(); }
    }

    public int SelectedSuggestionIndex
    {
        get => _selectedSuggestionIndex;
        set { _selectedSuggestionIndex = value; OnPropertyChanged(); }
    }

    public object? ActiveModal
    {
        get => _activeModal;
        private set { _activeModal = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsAnyModalOpen)); }
    }

    public bool IsAnyModalOpen => _activeModal is not null;

    public void CloseModal() => ActiveModal = null;

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

    public BitmapSource? PendingImagePreview
    {
        get => _pendingImagePreview;
        private set { _pendingImagePreview = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPendingImage)); }
    }

    public bool HasPendingImage => _pendingImagePreview is not null;

    public void SetPendingImage(byte[] data, string filename, BitmapSource preview)
    {
        _pendingImageData = data;
        _pendingImageFilename = filename;
        PendingImagePreview = preview;
    }

    public void ClearPendingImage()
    {
        _pendingImageData = null;
        _pendingImageFilename = string.Empty;
        PendingImagePreview = null;
    }

    public ICommand ClearPendingImageCommand => new RelayCommand(_ => ClearPendingImage());

    private void UpdateSlashSuggestions(string text)
    {
        SlashSuggestions.Clear();
        SelectedSuggestionIndex = -1;

        foreach (var cmd in SlashCommandFilter.Filter(_allCommands, text))
            SlashSuggestions.Add(cmd);

        ShowSlashSuggestions = SlashSuggestions.Count > 0;
    }

    public void SelectSuggestion(SlashCommandDto cmd)
    {
        MessageText = $"/{cmd.Name} ";
        SelectedSuggestionIndex = -1;
    }

    public void DismissSuggestions()
    {
        ShowSlashSuggestions = false;
        SelectedSuggestionIndex = -1;
    }

    public void NavigateSuggestions(int direction)
    {
        if (SlashSuggestions.Count == 0) return;
        int next = SelectedSuggestionIndex + direction;
        if (next < 0) next = SlashSuggestions.Count - 1;
        else if (next >= SlashSuggestions.Count) next = 0;
        SelectedSuggestionIndex = next;
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
            if (_selectedTextChannel is not null) _selectedTextChannel.IsSelected = false;
            _selectedTextChannel = value;
            if (_selectedTextChannel is not null) _selectedTextChannel.IsSelected = true;
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
    public ICommand CreateChannelCommand => new RelayCommand(_ => OpenCreateChannelModal(), _ => IsConnected);
    public ICommand RenameChannelCommand => new RelayCommand(p => OpenRenameChannelModal(p as ChannelViewModel), p => IsConnected && p is ChannelViewModel);
    public ICommand DeleteChannelCommand => new RelayCommand(p => OpenDeleteChannelModal(p as ChannelViewModel), p => IsConnected && p is ChannelViewModel);
    public ICommand SendMessageCommand => new RelayCommand(async _ => await SendMessageAsync(), _ => IsConnected && (!string.IsNullOrWhiteSpace(MessageText) || HasPendingImage));
    public ICommand ToggleMuteCommand => new RelayCommand(_ => IsMuted = !IsMuted);
    public ICommand ToggleDeafenCommand => new RelayCommand(_ => IsDeafened = !IsDeafened);
    public ICommand ChannelClickCommand => new RelayCommand(async p => await OnChannelClicked(p as ChannelViewModel));
    public ICommand DisconnectVoiceCommand => new RelayCommand(async _ => await LeaveVoice());
    public ICommand OpenSettingsCommand => new RelayCommand(_ => OpenSettingsModal());

    public MainViewModel()
    {
        _chatService.MessageReceived += OnMessageReceived;
        _chatService.UserJoinedVoice += OnUserJoinedVoice;
        _chatService.UserLeftVoice += OnUserLeftVoice;
        _chatService.VoiceMembersReceived += OnVoiceMembersReceived;
        _chatService.UserConnected += OnUserConnected;
        _chatService.UserDisconnected += OnUserDisconnected;
        _chatService.Disconnected += OnDisconnected;
        _chatService.ChannelCreated += OnChannelCreated;
        _chatService.ChannelDeleted += OnChannelDeleted;
        _chatService.ChannelRenamed += OnChannelRenamed;
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
            _allCommands = await _chatService.GetAvailableCommandsAsync();

            Application.Current.Dispatcher.Invoke(() =>
            {
                Channels.Clear();
                foreach (var ch in channels.OrderBy(c => c.Type))
                {
                    var vm = new ChannelViewModel(ch);
                    if (voiceMembers.TryGetValue(ch.Id, out var members))
                    {
                        foreach (var m in members)
                        {
                            var info = new VoiceMemberInfo { Username = m.Username, VoiceUserId = m.VoiceUserId };
                            SubscribeVoiceMemberVolume(info);
                            vm.VoiceMembers.Add(info);
                        }
                    }
                    Channels.Add(vm);
                }
            });

            IsConnected = true;
            ConnectionStatus = $"Connected as {Username}";

            var firstText = Channels.FirstOrDefault(c => c.Type == ChannelType.Text);
            if (firstText is not null)
                SelectedTextChannel = firstText;
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

    private void SubscribeVoiceMemberVolume(VoiceMemberInfo member)
    {
        member.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VoiceMemberInfo.Volume))
                _voiceService.SetUserVolume(member.VoiceUserId, member.Volume);
        };
    }

    private async Task JoinVoice(int channelId)
    {
        if (_voiceChannelId.HasValue)
        {
            var prev = Channels.FirstOrDefault(c => c.Id == _voiceChannelId.Value);
            if (prev is not null) prev.IsActiveVoice = false;
        }

        VoiceStatus = string.Empty;
        await _chatService.JoinVoiceChannel(channelId, _userId);

        var host = ServerAddress.Replace("http://", "").Replace("https://", "");
        if (host.Contains(':'))
            host = host.Split(':')[0];
        if (string.IsNullOrEmpty(host))
            host = "localhost";

        _voiceService.Start(host, 9000, _userId, channelId);
        _voiceChannelId = channelId;

        var ch = Channels.FirstOrDefault(c => c.Id == channelId);
        if (ch is not null) ch.IsActiveVoice = true;

        OnPropertyChanged(nameof(IsInVoice));
        OnPropertyChanged(nameof(VoiceChannelName));
    }

    private async Task LeaveVoice()
    {
        if (_voiceChannelId.HasValue)
        {
            var ch = Channels.FirstOrDefault(c => c.Id == _voiceChannelId.Value);
            if (ch is not null) ch.IsActiveVoice = false;
        }
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
        if (_selectedTextChannel is null) return;
        if (string.IsNullOrWhiteSpace(MessageText) && !HasPendingImage) return;

        if (_pendingImageData is not null)
        {
            var url = await _chatService.UploadImageAsync(_pendingImageData, _pendingImageFilename);
            if (url is not null)
                await _chatService.SendMessage(_selectedTextChannel.Id, string.Empty, url);
            ClearPendingImage();
        }

        if (!string.IsNullOrWhiteSpace(MessageText))
        {
            await _chatService.SendMessage(_selectedTextChannel.Id, MessageText, null);
            MessageText = string.Empty;
        }
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
            {
                var info = new VoiceMemberInfo { Username = username, VoiceUserId = voiceUserId };
                SubscribeVoiceMemberVolume(info);
                ch.VoiceMembers.Add(info);
            }
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
            {
                var info = new VoiceMemberInfo { Username = m.Username, VoiceUserId = m.VoiceUserId };
                SubscribeVoiceMemberVolume(info);
                ch.VoiceMembers.Add(info);
            }
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

    private void OpenCreateChannelModal()
    {
        ActiveModal = new CreateChannelModalViewModel(
            confirm: async (name, type) =>
            {
                ActiveModal = null;
                var dto = await _chatService.CreateChannelAsync(name, type);
                if (dto?.Type == ChannelType.Text)
                {
                    var channel = Channels.FirstOrDefault(c => c.Id == dto.Id);
                    if (channel is not null)
                        SelectedTextChannel = channel;
                }
            },
            cancel: () => ActiveModal = null);
    }

    private void OpenRenameChannelModal(ChannelViewModel? channel)
    {
        if (channel is null) return;
        ActiveModal = new RenameChannelModalViewModel(
            initialName: channel.Name,
            confirm: async name =>
            {
                ActiveModal = null;
                await _chatService.RenameChannelAsync(channel.Id, name);
            },
            cancel: () => ActiveModal = null);
    }

    private void OpenDeleteChannelModal(ChannelViewModel? channel)
    {
        if (channel is null) return;
        ActiveModal = new DeleteChannelModalViewModel(
            channelName: channel.Name,
            confirm: async () =>
            {
                ActiveModal = null;
                await _chatService.DeleteChannelAsync(channel.Id);
            },
            cancel: () => ActiveModal = null);
    }

    private void OpenSettingsModal() =>
        ActiveModal = new SettingsModalViewModel(Settings, () => ActiveModal = null);

    private void OnChannelCreated(ChannelDto dto)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (Channels.Any(c => c.Id == dto.Id)) return;
            var vm = new ChannelViewModel(dto);
            if (dto.Type == ChannelType.Text)
            {
                // Insert before the first voice channel
                var insertAt = Channels.Count(c => c.Type == ChannelType.Text);
                Channels.Insert(insertAt, vm);
            }
            else
            {
                Channels.Add(vm);
            }
        });
    }

    private void OnChannelDeleted(int channelId)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var vm = Channels.FirstOrDefault(c => c.Id == channelId);
            if (vm is null) return;
            Channels.Remove(vm);
            if (_selectedTextChannel?.Id == channelId) { SelectedTextChannel = null; Messages.Clear(); }
            if (_voiceChannelId == channelId)
            {
                _voiceService.Stop();
                _voiceChannelId = null;
                MicLevel = 0;
                OnPropertyChanged(nameof(IsInVoice));
                OnPropertyChanged(nameof(VoiceChannelName));
                _ = _chatService.LeaveVoiceChannel();
            }
        });
    }

    private void OnChannelRenamed(ChannelDto dto)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var vm = Channels.FirstOrDefault(c => c.Id == dto.Id);
            if (vm is null) return;
            vm.Name = dto.Name;
            if (_selectedTextChannel?.Id == dto.Id) OnPropertyChanged(nameof(SelectedChannelName));
            if (_voiceChannelId == dto.Id) OnPropertyChanged(nameof(VoiceChannelName));
        });
    }

    private void OnDisconnected()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            IsConnected = false;
            ConnectionStatus = "Disconnected";
            _voiceService.Stop();
            if (_voiceChannelId.HasValue)
            {
                var ch = Channels.FirstOrDefault(c => c.Id == _voiceChannelId.Value);
                if (ch is not null) ch.IsActiveVoice = false;
            }
            _voiceChannelId = null;
            OnPropertyChanged(nameof(IsInVoice));
            OnPropertyChanged(nameof(VoiceChannelName));
        });
    }

    public async ValueTask DisposeAsync()
    {
        _voiceService.Dispose();
        await _chatService.DisposeAsync();
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
