using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chaos.Client.Audio;
using Chaos.Client.Models;
using System.Windows.Threading;
using Chaos.Client.Services;
using Chaos.Shared;

namespace Chaos.Client.ViewModels;

public class MessageViewModel : INotifyPropertyChanged
{
    private bool _showHeader = true;
    private readonly AppSettings? _settings;
    private readonly string _baseUrl = string.Empty;

    public MessageDto Message { get; }
    public string Author => Message.Author;
    public DateTime Timestamp => Message.Timestamp;
    public string Content => Message.Content;
    public string? ImageUrl => Message.ImageUrl is null ? null
        : (Message.ImageUrl.StartsWith("http://") || Message.ImageUrl.StartsWith("https://"))
            ? Message.ImageUrl
            : $"{_baseUrl}{Message.ImageUrl}";
    public bool HasImage => Message.HasImage;
    public int ChannelId => Message.ChannelId;

    public bool ShowHeader
    {
        get => _showHeader;
        set
        {
            if (_showHeader == value) return;
            _showHeader = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Padding));
        }
    }

    /// <summary>
    /// Per-message padding. Group starters get MessageSpacing on top;
    /// continuation messages within a group get a fixed 1px gap.
    /// </summary>
    public Thickness Padding
    {
        get
        {
            double top = _showHeader ? (_settings?.MessageSpacing ?? 4.0) : 1.0;
            return new Thickness(16, top, 16, 0);
        }
    }

    public MessageViewModel(MessageDto message, AppSettings? settings = null, string baseUrl = "")
    {
        Message = message;
        _settings = settings;
        _baseUrl = baseUrl;
        if (_settings is not null)
            _settings.PropertyChanged += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.MessageSpacing) && _showHeader)
            OnPropertyChanged(nameof(Padding));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

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
    private readonly NoiseGate _noiseGate = new();
    private readonly PushToTalkService _pttService = new();
    private readonly ConnectionQualityService _connectionQuality;
    private readonly IKeyValueStore _settingsStore;
    private readonly DispatcherTimer _settingsSaveTimer;

    public AppSettings Settings { get; }

    private double _windowLeft = -999999;
    private double _windowTop = -999999;
    private double _windowWidth = 0;
    private double _windowHeight = 0;
    private bool   _windowMaximized = false;

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

    // Connection quality
    private int _pingMs;
    private int _qualityBars = 4;
    private bool _isPttActive;

    private object? _activeModal;

    public ObservableCollection<ChannelViewModel> Channels { get; } = new();
    public ObservableCollection<MessageViewModel> Messages { get; } = new();
    public ObservableCollection<SlashCommandDto> SlashSuggestions { get; } = new();

    public string ServerAddress
    {
        get => _serverAddress;
        set { _serverAddress = value; OnPropertyChanged(); }
    }

    public string Username
    {
        get => _username;
        set { _username = value; OnPropertyChanged(); _settingsSaveTimer?.Stop(); _settingsSaveTimer?.Start(); }
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

    // Connection quality
    public int PingMs
    {
        get => _pingMs;
        set { _pingMs = value; OnPropertyChanged(); OnPropertyChanged(nameof(PingTooltip)); }
    }
    public int QualityBars
    {
        get => _qualityBars;
        set
        {
            _qualityBars = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Bar1));
            OnPropertyChanged(nameof(Bar2));
            OnPropertyChanged(nameof(Bar3));
            OnPropertyChanged(nameof(Bar4));
        }
    }

    private static readonly SolidColorBrush ActiveBarBrush = new(Color.FromRgb(0x23, 0xA5, 0x59));
    private static readonly SolidColorBrush InactiveBarBrush = new(Color.FromRgb(0x4E, 0x50, 0x58));

    public Brush Bar1 => QualityBars >= 1 ? ActiveBarBrush : InactiveBarBrush;
    public Brush Bar2 => QualityBars >= 2 ? ActiveBarBrush : InactiveBarBrush;
    public Brush Bar3 => QualityBars >= 3 ? ActiveBarBrush : InactiveBarBrush;
    public Brush Bar4 => QualityBars >= 4 ? ActiveBarBrush : InactiveBarBrush;
    public string PingTooltip => $"Ping: {_pingMs}ms | Loss: {_connectionQuality.PacketLossPercent:F1}%";

    // PTT state
    public bool IsPttActive
    {
        get => _isPttActive;
        set { _isPttActive = value; OnPropertyChanged(); }
    }
    public bool IsPttMode => Settings.VoiceMode == VoiceMode.PushToTalk;
    public string PttKeyDisplay => Settings.PttKey;

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
    public ICommand CreateChannelCommand => new RelayCommand(_ => OpenCreateChannelModal(), _ => IsConnected);
    public ICommand RenameChannelCommand => new RelayCommand(p => OpenRenameChannelModal(p as ChannelViewModel), p => IsConnected && p is ChannelViewModel);
    public ICommand DeleteChannelCommand => new RelayCommand(p => OpenDeleteChannelModal(p as ChannelViewModel), p => IsConnected && p is ChannelViewModel);
    public ICommand SendMessageCommand => new RelayCommand(async _ => await SendMessageAsync(), _ => IsConnected && (!string.IsNullOrWhiteSpace(MessageText) || HasPendingImage));
    public ICommand ToggleMuteCommand => new RelayCommand(_ => IsMuted = !IsMuted);
    public ICommand ToggleDeafenCommand => new RelayCommand(_ => IsDeafened = !IsDeafened);
    public ICommand ChannelClickCommand => new RelayCommand(async p => await OnChannelClicked(p as ChannelViewModel));
    public ICommand DisconnectVoiceCommand => new RelayCommand(async _ => await LeaveVoice());
    public ICommand ToggleScreenShareCommand => new RelayCommand(async _ => await ToggleScreenShare(), _ => IsInVoice);
    public ICommand WatchStreamCommand => new RelayCommand(async p => await WatchStream(p as VoiceMemberInfo), p => p is VoiceMemberInfo m && m.IsStreaming && IsConnected);
    public ICommand StopWatchingCommand => new RelayCommand(_ => StopWatchingStream());
    public ICommand PopOutStreamCommand => new RelayCommand(_ => PopOutStream(), _ => IsWatchingStream);
    public ICommand OpenSettingsCommand => new RelayCommand(_ => OpenSettingsModal());

    public MainViewModel() : this(new LocalJsonKeyValueStore()) { }

    public MainViewModel(IKeyValueStore store)
    {
        _settingsStore = store;
        _connectionQuality = new ConnectionQualityService(_chatService);

        Settings = new AppSettings
        {
            FontSize           = _settingsStore.Get("FontSize",           14.0),
            MessageSpacing     = _settingsStore.Get("MessageSpacing",     4.0),
            UiScale            = _settingsStore.Get("UiScale",            1.0),
            GroupMessages      = _settingsStore.Get("GroupMessages",      false),
            InputDevice        = _settingsStore.Get("InputDevice",        "Default"),
            OutputDevice       = _settingsStore.Get("OutputDevice",       "Default"),
            InputVolume        = _settingsStore.Get("InputVolume",        1.0f),
            OutputVolume       = _settingsStore.Get("OutputVolume",       1.0f),
            VoiceMode          = _settingsStore.Get("VoiceMode",          VoiceMode.VoiceActivity),
            PttKey             = _settingsStore.Get("PttKey",             "OemTilde"),
            PttReleaseDelay    = _settingsStore.Get("PttReleaseDelay",    200),
            VadSensitivity     = _settingsStore.Get("VadSensitivity",     0.5f),
            NoiseSuppression   = _settingsStore.Get("NoiseSuppression",   true),
            OpusBitrate        = _settingsStore.Get("OpusBitrate",        48000),
            DefaultStreamQuality = _settingsStore.Get("DefaultStreamQuality", StreamQuality.Medium),
            MaxStreamFps       = _settingsStore.Get("MaxStreamFps",       30),
        };

        _username = _settingsStore.Get("Username", string.Empty);

        _windowLeft      = _settingsStore.Get("WindowLeft",      -999999.0);
        _windowTop       = _settingsStore.Get("WindowTop",       -999999.0);
        _windowWidth     = _settingsStore.Get("WindowWidth",      0.0);
        _windowHeight    = _settingsStore.Get("WindowHeight",     0.0);
        _windowMaximized = _settingsStore.Get("WindowMaximized",  false);

        _voiceService.InputDeviceName = Settings.InputDevice;
        _voiceService.OutputDeviceName = Settings.OutputDevice;
        _voiceService.InputVolume = Settings.InputVolume;
        _voiceService.OutputVolume = Settings.OutputVolume;

        // Configure noise gate
        _noiseGate.Sensitivity = Settings.VadSensitivity;
        if (Settings.NoiseSuppression)
            _voiceService.SetTransmitGate((pcm, count) => _noiseGate.Process(pcm, count));

        // Configure PTT
        if (Enum.TryParse<Key>(Settings.PttKey, out var pttKey))
            _pttService.PttKey = pttKey;

        ConfigureVoiceMode();

        _pttService.KeyStateChanged += isDown =>
        {
            SafeDispatchAsync(() => IsPttActive = isDown);
        };

        // Connection quality
        _voiceService.PacketReceived += (userId, seqNum) =>
            _connectionQuality.OnVoicePacketReceived(userId, seqNum);
        _connectionQuality.Updated += () =>
        {
            SafeDispatchAsync(() =>
            {
                PingMs = _connectionQuality.PingMs;
                QualityBars = _connectionQuality.QualityBars;
            });
        };

        _settingsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _settingsSaveTimer.Tick += (_, _) => { _settingsSaveTimer.Stop(); FlushSettings(); };

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
            SafeDispatchAsync(() =>
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
            SafeDispatchAsync(() =>
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
            SafeDispatchAsync(() => VoiceStatus = error);
        };

        _userId = Random.Shared.Next(1, 100000);

        Settings.PropertyChanged += (_, e) =>
        {
            _settingsSaveTimer.Stop();
            _settingsSaveTimer.Start();

            if (e.PropertyName == nameof(AppSettings.GroupMessages))
                RecomputeGrouping();
            if (e.PropertyName == nameof(AppSettings.InputDevice))
            {
                _voiceService.InputDeviceName = Settings.InputDevice;
                if (IsInVoice) RestartVoice();
            }
            if (e.PropertyName == nameof(AppSettings.OutputDevice))
            {
                _voiceService.OutputDeviceName = Settings.OutputDevice;
                if (IsInVoice) RestartVoice();
            }
            if (e.PropertyName == nameof(AppSettings.InputVolume))
                _voiceService.InputVolume = Settings.InputVolume;
            if (e.PropertyName == nameof(AppSettings.OutputVolume))
                _voiceService.OutputVolume = Settings.OutputVolume;
            if (e.PropertyName == nameof(AppSettings.VoiceMode))
            {
                ConfigureVoiceMode();
                OnPropertyChanged(nameof(IsPttMode));
            }
            if (e.PropertyName == nameof(AppSettings.PttKey))
            {
                if (Enum.TryParse<Key>(Settings.PttKey, out var key))
                    _pttService.PttKey = key;
                OnPropertyChanged(nameof(PttKeyDisplay));
            }
            if (e.PropertyName == nameof(AppSettings.VadSensitivity))
                _noiseGate.Sensitivity = Settings.VadSensitivity;
            if (e.PropertyName == nameof(AppSettings.NoiseSuppression))
                ConfigureVoiceMode();
        };
    }

    private static void SafeDispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        dispatcher.Invoke(action);
    }

    private static void SafeDispatchAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(action);
    }

    private void RestartVoice()
    {
        if (!_voiceChannelId.HasValue) return;
        var host = ServerAddress.Replace("http://", "").Replace("https://", "");
        if (host.Contains('/')) host = host.Split('/')[0];
        if (host.Contains(':')) host = host.Split(':')[0];
        if (string.IsNullOrEmpty(host)) host = "localhost";
        _voiceService.Start(host, 9000, _userId, _voiceChannelId.Value);
    }

    private void ConfigureVoiceMode()
    {
        if (Settings.VoiceMode == VoiceMode.PushToTalk)
        {
            _voiceService.SetTransmitGate(null);
            _voiceService.SetPttCheck(() => _pttService.IsActive);
            _pttService.Install();
        }
        else
        {
            _voiceService.SetPttCheck(null);
            _pttService.Uninstall();
            if (Settings.NoiseSuppression)
                _voiceService.SetTransmitGate((pcm, count) => _noiseGate.Process(pcm, count));
            else
                _voiceService.SetTransmitGate(null);
        }
    }

    private bool ShouldShowHeader(MessageViewModel msg, MessageViewModel? prev) =>
        !Settings.GroupMessages || prev is null || prev.Author != msg.Author;

    private void RecomputeGrouping()
    {
        MessageViewModel? prev = null;
        foreach (var msg in Messages)
        {
            msg.ShowHeader = ShouldShowHeader(msg, prev);
            prev = msg;
        }
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

            SafeDispatch(() =>
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
        SafeDispatch(() =>
        {
            Messages.Clear();
            MessageViewModel? prev = null;
            foreach (var msg in messages)
            {
                var vm = new MessageViewModel(msg, Settings, _chatService.BaseUrl);
                vm.ShowHeader = ShouldShowHeader(vm, prev);
                Messages.Add(vm);
                prev = vm;
            }
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
        _connectionQuality.Start();
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
        _connectionQuality.Stop();
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
            SafeDispatch(() =>
            {
                var vm = new MessageViewModel(msg, Settings, _chatService.BaseUrl);
                vm.ShowHeader = ShouldShowHeader(vm, Messages.LastOrDefault());
                Messages.Add(vm);
            });
    }

    private void OnUserJoinedVoice(string username, int channelId, int voiceUserId)
    {
        SafeDispatch(() =>
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
        SafeDispatch(() =>
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

        SafeDispatch(() =>
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
        SafeDispatch(() =>
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
        SafeDispatch(() =>
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
        SafeDispatch(() =>
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
        SafeDispatch(() =>
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
        SafeDispatch(() =>
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

    public void UpdateWindowBounds(double left, double top, double width, double height, bool maximized)
    {
        _windowLeft      = left;
        _windowTop       = top;
        _windowWidth     = width;
        _windowHeight    = height;
        _windowMaximized = maximized;
    }

    public (double Left, double Top, double Width, double Height, bool Maximized) GetWindowBounds() =>
        (_windowLeft, _windowTop, _windowWidth, _windowHeight, _windowMaximized);

    public void FlushSettings()
    {
        _settingsStore.Set("FontSize",       Settings.FontSize);
        _settingsStore.Set("MessageSpacing", Settings.MessageSpacing);
        _settingsStore.Set("UiScale",        Settings.UiScale);
        _settingsStore.Set("GroupMessages",  Settings.GroupMessages);
        _settingsStore.Set("InputDevice",    Settings.InputDevice);
        _settingsStore.Set("OutputDevice",   Settings.OutputDevice);
        _settingsStore.Set("InputVolume",    Settings.InputVolume);
        _settingsStore.Set("OutputVolume",   Settings.OutputVolume);
        _settingsStore.Set("VoiceMode",      Settings.VoiceMode);
        _settingsStore.Set("PttKey",         Settings.PttKey);
        _settingsStore.Set("PttReleaseDelay", Settings.PttReleaseDelay);
        _settingsStore.Set("VadSensitivity", Settings.VadSensitivity);
        _settingsStore.Set("NoiseSuppression", Settings.NoiseSuppression);
        _settingsStore.Set("OpusBitrate",    Settings.OpusBitrate);
        _settingsStore.Set("DefaultStreamQuality", Settings.DefaultStreamQuality);
        _settingsStore.Set("MaxStreamFps",   Settings.MaxStreamFps);
        _settingsStore.Set("Username",       Username);
        _settingsStore.Set("WindowLeft",      _windowLeft);
        _settingsStore.Set("WindowTop",       _windowTop);
        _settingsStore.Set("WindowWidth",     _windowWidth);
        _settingsStore.Set("WindowHeight",    _windowHeight);
        _settingsStore.Set("WindowMaximized", _windowMaximized);
    }

    public async ValueTask DisposeAsync()
    {
        _screenShareService.Dispose();
        _connectionQuality.Dispose();
        _pttService.Dispose();
        _settingsSaveTimer.Stop();
        FlushSettings();
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
