using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Chaos.Client.Models;
using Chaos.Client.Services;
using Chaos.Shared;
using Chaos.Client;

namespace Chaos.Client.ViewModels;

public class VoiceMemberInfo : INotifyPropertyChanged
{
    private bool _isSpeaking;
    private float _volume = 1.0f;
    private bool _isStreaming;
    private bool _isMuted;
    private bool _isDeafened;
    public string Username { get; set; } = string.Empty;
    public int VoiceUserId { get; set; }
    public int StreamVideoUserId { get; set; }
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
    public bool IsStreaming
    {
        get => _isStreaming;
        set { _isStreaming = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStreaming))); }
    }
    public bool IsMuted
    {
        get => _isMuted;
        set { _isMuted = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMuted))); }
    }
    public bool IsDeafened
    {
        get => _isDeafened;
        set { _isDeafened = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDeafened))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public class ChannelViewModel : INotifyPropertyChanged
{
    private string _name;
    private bool _isSelected;
    private bool _isActiveVoice;
    private bool _hasActiveStream;
    public ChannelViewModel(ChannelDto channel)
    {
        Channel = channel;
        _name = channel.Name;
        VoiceMembers.CollectionChanged += (_, _) => RefreshStreamState();
    }
    public ChannelDto Channel { get; set; }
    public ObservableCollection<VoiceMemberInfo> VoiceMembers { get; } = new();
    public void RefreshStreamState()
    {
        HasActiveStream = VoiceMembers.Any(m => m.IsStreaming);
    }
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
    public bool HasActiveStream
    {
        get => _hasActiveStream;
        set { if (_hasActiveStream == value) return; _hasActiveStream = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasActiveStream))); }
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
    private readonly ScreenShareService _screenShareService = new();

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
    private bool _isScreenSharing;
    private bool _isWatchingStream;
    private string _watchingStreamUsername = string.Empty;
    private int _watchingStreamVideoUserId;
    private BitmapSource? _streamFrame;
    private StreamViewerWindow? _popOutWindow;
    private bool _isPoppedOut;
    private bool _streamMinimized;

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
            _ = _chatService.UpdateMuteState(_isMuted, _isDeafened);
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
            _ = _chatService.UpdateMuteState(_isMuted, _isDeafened);
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

    // Screen sharing properties
    public bool IsScreenSharing
    {
        get => _isScreenSharing;
        set { _isScreenSharing = value; OnPropertyChanged(); OnPropertyChanged(nameof(ScreenShareButtonText)); }
    }

    public bool IsWatchingStream
    {
        get => _isWatchingStream;
        set { _isWatchingStream = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowInlineStream)); OnPropertyChanged(nameof(ContentHeaderText)); }
    }

    public string WatchingStreamUsername
    {
        get => _watchingStreamUsername;
        set { _watchingStreamUsername = value; OnPropertyChanged(); OnPropertyChanged(nameof(ContentHeaderText)); }
    }

    public BitmapSource? StreamFrame
    {
        get => _streamFrame;
        set { _streamFrame = value; OnPropertyChanged(); }
    }

    public bool IsPoppedOut
    {
        get => _isPoppedOut;
        set { _isPoppedOut = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowInlineStream)); OnPropertyChanged(nameof(ContentHeaderText)); }
    }

    public bool ShowInlineStream => _isWatchingStream && !_isPoppedOut && !_streamMinimized;

    public string ScreenShareButtonText => IsScreenSharing ? "Stop Sharing" : "Share Screen";

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
            OnPropertyChanged(nameof(ContentHeaderText));
            if (value is not null)
                _ = OnTextChannelSelected(value);
        }
    }

    public bool HasTextChannel => _selectedTextChannel is not null;
    public string SelectedChannelName => _selectedTextChannel is not null ? $"# {_selectedTextChannel.Name}" : string.Empty;

    public string ContentHeaderText
    {
        get
        {
            if (_isWatchingStream && !_isPoppedOut)
            {
                var chName = _voiceChannelId.HasValue
                    ? Channels.FirstOrDefault(c => c.Id == _voiceChannelId.Value)?.Name ?? "Voice"
                    : "Voice";
                return $"\U0001F534 {_watchingStreamUsername} - {chName}";
            }
            return SelectedChannelName;
        }
    }

    public ICommand ConnectCommand => new RelayCommand(async _ => await ConnectAsync(), _ => CanConnect);
    public ICommand CreateChannelCommand => new RelayCommand(async _ => await CreateChannelAsync(), _ => IsConnected);
    public ICommand RenameChannelCommand => new RelayCommand(async p => await RenameChannelAsync(p as ChannelViewModel), p => IsConnected && p is ChannelViewModel);
    public ICommand DeleteChannelCommand => new RelayCommand(async p => await DeleteChannelAsync(p as ChannelViewModel), p => IsConnected && p is ChannelViewModel);
    public ICommand SendMessageCommand => new RelayCommand(async _ => await SendMessageAsync(), _ => IsConnected && (!string.IsNullOrWhiteSpace(MessageText) || HasPendingImage));
    public ICommand ToggleMuteCommand => new RelayCommand(_ => IsMuted = !IsMuted);
    public ICommand ToggleDeafenCommand => new RelayCommand(_ => IsDeafened = !IsDeafened);
    public ICommand ChannelClickCommand => new RelayCommand(async p => await OnChannelClicked(p as ChannelViewModel));
    public ICommand DisconnectVoiceCommand => new RelayCommand(async _ => await LeaveVoice());
    public ICommand ToggleScreenShareCommand => new RelayCommand(async _ => await ToggleScreenShare(), _ => IsInVoice);
    public ICommand WatchStreamCommand => new RelayCommand(async p => await WatchStream(p as VoiceMemberInfo), p => p is VoiceMemberInfo m && m.IsStreaming && IsConnected);
    public ICommand StopWatchingCommand => new RelayCommand(_ => StopWatchingStream());
    public ICommand PopOutStreamCommand => new RelayCommand(_ => PopOutStream(), _ => IsWatchingStream);

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
        _chatService.UserStartedScreenShare += OnUserStartedScreenShare;
        _chatService.UserStoppedScreenShare += OnUserStoppedScreenShare;
        _chatService.UserMuteStateChanged += OnUserMuteStateChanged;
        _screenShareService.FrameReceived += OnStreamFrameReceived;
        _screenShareService.Error += error =>
        {
            Application.Current.Dispatcher.BeginInvoke(async () =>
            {
                VoiceStatus = error;
                // Auto-stop screen sharing if the captured window was closed
                if (IsScreenSharing && !_screenShareService.IsStreaming)
                {
                    await _chatService.StopScreenShare();
                    IsScreenSharing = false;
                    StopWatchingStream();
                }
            });
        };
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
            var screenShareMembers = await _chatService.GetAllScreenShareMembers();

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
                            var info = new VoiceMemberInfo { Username = m.Username, VoiceUserId = m.VoiceUserId, IsMuted = m.IsMuted, IsDeafened = m.IsDeafened };
                            SubscribeVoiceMemberVolume(info);
                            // Check if this member is screen sharing
                            if (screenShareMembers.TryGetValue(ch.Id, out var sharers))
                            {
                                var sharer = sharers.FirstOrDefault(s => s.Username == m.Username);
                                if (sharer is not null)
                                {
                                    info.IsStreaming = true;
                                    info.StreamVideoUserId = sharer.VideoUserId;
                                }
                            }
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
            // Hide inline stream when switching to a text channel (stream keeps running)
            _streamMinimized = true;
            OnPropertyChanged(nameof(ShowInlineStream));
            OnPropertyChanged(nameof(ContentHeaderText));
            SelectedTextChannel = channel;
        }
        else if (channel.Type == ChannelType.Voice)
        {
            // Click to join (or switch). Leave only via disconnect button.
            if (_voiceChannelId != channel.Id)
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
        // Stop screen sharing & watching first
        if (IsScreenSharing)
        {
            _screenShareService.StopStreaming();
            await _chatService.StopScreenShare();
            IsScreenSharing = false;
        }
        StopWatchingStream();
        _screenShareService.Stop();

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

    // ── Screen Sharing ──────────────────────────────────

    private async Task ToggleScreenShare()
    {
        if (IsScreenSharing)
        {
            _screenShareService.StopStreaming();
            await _chatService.StopScreenShare();
            IsScreenSharing = false;
            StopWatchingStream();
        }
        else
        {
            var dialog = new ScreenShareDialog { Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() != true) return;

            var quality = dialog.SelectedQuality;
            var target = dialog.SelectedTarget;
            if (target is null) return;

            var host = ServerAddress.Replace("http://", "").Replace("https://", "");
            if (host.Contains(':')) host = host.Split(':')[0];
            if (string.IsNullOrEmpty(host)) host = "localhost";

            // Use a separate video userId (offset from voice userId)
            var videoUserId = _userId + 50000;

            _screenShareService.StartStreaming(host, 9001, videoUserId, _voiceChannelId!.Value, quality, target);
            await _chatService.StartScreenShare(videoUserId, (int)quality);
            IsScreenSharing = true;

            // Show self-preview
            _watchingStreamVideoUserId = videoUserId;
            WatchingStreamUsername = Username;
            _streamMinimized = false;
            IsWatchingStream = true;
        }
    }

    private async Task WatchStream(VoiceMemberInfo? member)
    {
        if (member is null || !member.IsStreaming) return;

        // If already watching this person's stream but minimized or popped out, just restore inline
        if (_isWatchingStream && _watchingStreamVideoUserId == member.StreamVideoUserId)
        {
            _streamMinimized = false;
            IsPoppedOut = false;
            OnPropertyChanged(nameof(ShowInlineStream));
            OnPropertyChanged(nameof(ContentHeaderText));
            return;
        }

        // Auto-join the voice channel if not already in one
        var memberChannel = Channels.FirstOrDefault(c => c.VoiceMembers.Contains(member));
        if (memberChannel is not null && _voiceChannelId != memberChannel.Id)
            await JoinVoice(memberChannel.Id);

        if (!_voiceChannelId.HasValue) return;

        StopWatchingStream();

        // For the streamer watching their own preview, just set the UI state —
        // self-preview frames come from the capture loop, no receive loop needed
        var isSelfPreview = IsScreenSharing && member.Username == Username;
        if (!isSelfPreview)
        {
            var host = ServerAddress.Replace("http://", "").Replace("https://", "");
            if (host.Contains(':')) host = host.Split(':')[0];
            if (string.IsNullOrEmpty(host)) host = "localhost";

            var viewerUserId = _userId + 200000;
            _screenShareService.StartWatching(host, 9001, viewerUserId, _voiceChannelId!.Value);
        }

        _watchingStreamVideoUserId = member.StreamVideoUserId;
        WatchingStreamUsername = member.Username;
        _streamMinimized = false;
        IsWatchingStream = true;
    }

    private void StopWatchingStream()
    {
        // Full cleanup for viewers (close UDP + send BYE); no-op for streamers' capture loop
        if (!IsScreenSharing)
            _screenShareService.Stop();
        else
            _screenShareService.StopWatching();

        _streamMinimized = false;
        IsWatchingStream = false;
        IsPoppedOut = false;
        StreamFrame = null;
        WatchingStreamUsername = string.Empty;
        _watchingStreamVideoUserId = 0;

        if (_popOutWindow is not null)
        {
            _popOutWindow.Hide();
        }
    }

    private void PopOutStream()
    {
        if (_popOutWindow is null)
        {
            _popOutWindow = new StreamViewerWindow();
            _popOutWindow.Closed += (_, _) =>
            {
                // Window is dead after Close() — can't reuse, must null ref
                _popOutWindow = null;
                StopWatchingStream();
            };
            _popOutWindow.CloseRequested += () =>
            {
                // Custom X button uses Hide() — window stays alive for reuse
                StopWatchingStream();
            };
            _popOutWindow.PopInRequested += () =>
            {
                _streamMinimized = false;
                IsPoppedOut = false;
            };
        }

        _popOutWindow.SetStreamerName(WatchingStreamUsername);
        if (StreamFrame is not null)
            _popOutWindow.UpdateFrame(StreamFrame);
        _popOutWindow.Show();
        _popOutWindow.Activate();
        IsPoppedOut = true;
    }

    private void OnStreamFrameReceived(int senderId, BitmapSource frame)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (!_isWatchingStream) return;

            // Show frames from the streamer we're watching
            // (senderId is the streamer's videoUserId, _watchingStreamVideoUserId is what we want)
            if (_watchingStreamVideoUserId != 0 && senderId != _watchingStreamVideoUserId) return;

            StreamFrame = frame;
            _popOutWindow?.UpdateFrame(frame);
        });
    }

    private void OnUserMuteStateChanged(string username, int channelId, bool isMuted, bool isDeafened)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var ch = Channels.FirstOrDefault(c => c.Id == channelId);
            var member = ch?.VoiceMembers.FirstOrDefault(m => m.Username == username);
            if (member is not null)
            {
                member.IsMuted = isMuted;
                member.IsDeafened = isDeafened;
            }
        });
    }

    private void OnUserStartedScreenShare(string username, int channelId, int videoUserId, int quality)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var ch = Channels.FirstOrDefault(c => c.Id == channelId);
            var member = ch?.VoiceMembers.FirstOrDefault(m => m.Username == username);
            if (member is not null)
            {
                member.IsStreaming = true;
                member.StreamVideoUserId = videoUserId;
            }
            ch?.RefreshStreamState();
        });
    }

    private void OnUserStoppedScreenShare(string username, int channelId)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var ch = Channels.FirstOrDefault(c => c.Id == channelId);
            var member = ch?.VoiceMembers.FirstOrDefault(m => m.Username == username);
            if (member is not null)
            {
                member.IsStreaming = false;
                member.StreamVideoUserId = 0;
            }
            ch?.RefreshStreamState();

            // If we were watching this person, stop
            if (WatchingStreamUsername == username)
                StopWatchingStream();
        });
    }

    // ── Messaging ──────────────────────────────────────

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

    private async void OnVoiceMembersReceived(int channelId, List<VoiceMemberDto> members)
    {
        var screenShareMembers = await _chatService.GetAllScreenShareMembers();

        Application.Current.Dispatcher.Invoke(() =>
        {
            var ch = Channels.FirstOrDefault(c => c.Id == channelId);
            if (ch is null) return;

            ch.VoiceMembers.Clear();
            foreach (var m in members)
            {
                var info = new VoiceMemberInfo { Username = m.Username, VoiceUserId = m.VoiceUserId, IsMuted = m.IsMuted, IsDeafened = m.IsDeafened };
                SubscribeVoiceMemberVolume(info);
                if (screenShareMembers.TryGetValue(channelId, out var sharers))
                {
                    var sharer = sharers.FirstOrDefault(s => s.Username == m.Username);
                    if (sharer is not null)
                    {
                        info.IsStreaming = true;
                        info.StreamVideoUserId = sharer.VideoUserId;
                    }
                }
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

    private async Task CreateChannelAsync()
    {
        var dialog = new ChannelDialog("Create Channel", string.Empty, showTypeSelector: true)
            { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;
        var dto = await _chatService.CreateChannelAsync(dialog.ChannelName, dialog.SelectedType);
        if (dto?.Type == ChannelType.Text)
        {
            var channel = Channels.FirstOrDefault(c => c.Id == dto.Id);
            if (channel is not null)
                SelectedTextChannel = channel;
        }
    }

    private async Task RenameChannelAsync(ChannelViewModel? channel)
    {
        if (channel is null) return;
        var dialog = new ChannelDialog("Rename Channel", channel.Name, showTypeSelector: false)
            { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;
        await _chatService.RenameChannelAsync(channel.Id, dialog.ChannelName);
    }

    private async Task DeleteChannelAsync(ChannelViewModel? channel)
    {
        if (channel is null) return;
        var result = MessageBox.Show($"Delete \"{channel.Name}\"? This cannot be undone.",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        await _chatService.DeleteChannelAsync(channel.Id);
    }

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
            _screenShareService.Stop();
            IsScreenSharing = false;
            StopWatchingStream();
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
        _screenShareService.Dispose();
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
