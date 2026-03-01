using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
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

    private int _unreadCount;
    public int UnreadCount
    {
        get => _unreadCount;
        set
        {
            if (_unreadCount == value) return;
            _unreadCount = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UnreadCount)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasUnread)));
        }
    }

    private int _mentionCount;
    public int MentionCount
    {
        get => _mentionCount;
        set
        {
            if (_mentionCount == value) return;
            _mentionCount = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MentionCount)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasMention)));
        }
    }

    public bool HasMention => MentionCount > 0;

    public bool HasUnread => UnreadCount > 0;

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly ChatService _chatService = new();
    private readonly VoiceService _voiceService = new();
    private readonly IKeyValueStore _settingsStore;
    private readonly DispatcherTimer _settingsSaveTimer;
    private readonly Dictionary<int, DateTime> _remoteLastSpoke = new();
    private readonly DispatcherTimer _remoteSpeakingTimer;
    private static readonly TimeSpan RemoteSpeakingHoldTime = TimeSpan.FromMilliseconds(500);

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
    private readonly Dictionary<string, DateTime> _typingUsers = new();
    private readonly System.Timers.Timer _typingCleanupTimer = new(1000) { AutoReset = true };
    private DateTime _lastTypingSent = DateTime.MinValue;
    private string _typingText = string.Empty;

    private object? _activeModal;
    private List<string> _allKnownUsers = new();
    private bool _showMentionSuggestions;
    private int _selectedMentionIndex = -1;

    public ObservableCollection<ChannelViewModel> Channels { get; } = new();
    public ObservableCollection<MessageViewModel> Messages { get; } = new();
    public ObservableCollection<SlashCommandDto> SlashSuggestions { get; } = new();
    public ObservableCollection<string> ConnectedUsers { get; } = new();
    public ObservableCollection<string> MentionSuggestions { get; } = new();

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

    public string TypingText
    {
        get => _typingText;
        private set { _typingText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasTypingUsers)); }
    }

    public bool HasTypingUsers => !string.IsNullOrEmpty(_typingText);

    public string MessageText
    {
        get => _messageText;
        set
        {
            _messageText = value;
            OnPropertyChanged();
            UpdateSlashSuggestions(value);
            UpdateMentionSuggestions(value);
            if (!string.IsNullOrEmpty(value) && _selectedTextChannel is not null && IsConnected)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastTypingSent).TotalSeconds >= 2)
                {
                    _lastTypingSent = now;
                    _ = _chatService.StartTypingAsync(_selectedTextChannel.Id);
                }
            }
        }
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
        private set { _activeModal = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsAnyModalOpen)); OnPropertyChanged(nameof(IsImagePreviewOpen)); }
    }

    public bool IsImagePreviewOpen => _activeModal is ImagePreviewModalViewModel;
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
            OnPropertyChanged(nameof(MuteTooltipText));
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
            OnPropertyChanged(nameof(DeafenTooltipText));
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

    public bool ShowMentionSuggestions
    {
        get => _showMentionSuggestions;
        set { _showMentionSuggestions = value; OnPropertyChanged(); }
    }

    public int SelectedMentionIndex
    {
        get => _selectedMentionIndex;
        set { _selectedMentionIndex = value; OnPropertyChanged(); }
    }

    private void UpdateMentionSuggestions(string text)
    {
        MentionSuggestions.Clear();
        SelectedMentionIndex = -1;

        if (string.IsNullOrEmpty(text))
        {
            ShowMentionSuggestions = false;
            return;
        }

        // Find the last '@' that is not preceded by a word character
        int atIndex = -1;
        for (int i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] == '@')
            {
                if (i == 0 || !char.IsLetterOrDigit(text[i - 1]))
                    atIndex = i;
                break;
            }
            if (text[i] == ' ' && atIndex == -1)
            {
                // Keep looking for @ before this space
            }
        }

        if (atIndex < 0)
        {
            ShowMentionSuggestions = false;
            return;
        }

        var partial = text[(atIndex + 1)..];
        if (partial.Contains(' '))
        {
            ShowMentionSuggestions = false;
            return;
        }

        foreach (var user in _allKnownUsers)
        {
            if (user.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                MentionSuggestions.Add(user);
        }

        ShowMentionSuggestions = MentionSuggestions.Count > 0;
    }

    public void SelectMentionSuggestion(string username)
    {
        var text = MessageText;
        // Find the last '@'
        int atIndex = text.LastIndexOf('@');
        if (atIndex >= 0)
            MessageText = text[..atIndex] + $"@{username} ";
        ShowMentionSuggestions = false;
        SelectedMentionIndex = -1;
    }

    public void NavigateMentionSuggestions(int direction)
    {
        if (MentionSuggestions.Count == 0) return;
        int next = SelectedMentionIndex + direction;
        if (next < 0) next = MentionSuggestions.Count - 1;
        else if (next >= MentionSuggestions.Count) next = 0;
        SelectedMentionIndex = next;
    }

    public void DismissMentionSuggestions()
    {
        ShowMentionSuggestions = false;
        SelectedMentionIndex = -1;
    }

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

    public string MuteButtonText => IsMuted ? "\U0001F507" : "\U0001F3A4";
    public string MuteTooltipText => IsMuted ? "Unmute" : "Mute";
    public string DeafenButtonText => IsDeafened ? "\U0001F508" : "\U0001F50A";
    public string DeafenTooltipText => IsDeafened ? "Undeafen" : "Deafen";

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
            if (_selectedTextChannel is not null)
            {
                _selectedTextChannel.IsSelected = true;
                _selectedTextChannel.UnreadCount = 0;
                _selectedTextChannel.MentionCount = 0;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTextChannel));
            OnPropertyChanged(nameof(SelectedChannelName));
            if (value is not null)
                _ = OnTextChannelSelected(value);
        }
    }

    public bool HasTextChannel => _selectedTextChannel is not null;
    public string SelectedChannelName => _selectedTextChannel is not null ? $"# {_selectedTextChannel.Name}" : string.Empty;
    public string ConnectedUsersHeader => $"MEMBERS — {ConnectedUsers.Count}";

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

    public MainViewModel() : this(new LocalJsonKeyValueStore()) { }

    public MainViewModel(IKeyValueStore store)
    {
        _settingsStore = store;

        Settings = new AppSettings
        {
            FontSize        = _settingsStore.Get("FontSize",        14.0),
            MessageSpacing  = _settingsStore.Get("MessageSpacing",  4.0),
            UiScale         = _settingsStore.Get("UiScale",         1.0),
            GroupMessages   = _settingsStore.Get("GroupMessages",   false),
            InputDevice     = _settingsStore.Get("InputDevice",     "Default"),
            OutputDevice    = _settingsStore.Get("OutputDevice",    "Default"),
            InputVolume     = _settingsStore.Get("InputVolume",     1.0f),
            OutputVolume    = _settingsStore.Get("OutputVolume",    1.0f),
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
        _voiceService.MicThreshold = Settings.MicThreshold;

        _settingsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _settingsSaveTimer.Tick += (_, _) => { _settingsSaveTimer.Stop(); FlushSettings(); };

        _remoteSpeakingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _remoteSpeakingTimer.Tick += (_, _) =>
        {
            if (!_voiceChannelId.HasValue || _remoteLastSpoke.Count == 0) return;
            var ch = Channels.FirstOrDefault(c => c.Id == _voiceChannelId.Value);
            if (ch is null) return;
            var now = DateTime.UtcNow;
            foreach (var (userId, lastSpoke) in _remoteLastSpoke)
            {
                if ((now - lastSpoke) > RemoteSpeakingHoldTime)
                {
                    var member = ch.VoiceMembers.FirstOrDefault(m => m.VoiceUserId == userId);
                    if (member is not null) member.IsSpeaking = false;
                }
            }
        };
        _remoteSpeakingTimer.Start();

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
        _chatService.UserTyping += OnUserTyping;
        _chatService.NewMessageIndicator += OnNewMessageIndicator;
        _typingCleanupTimer.Elapsed += (_, _) => CleanupTypingUsers();
        _typingCleanupTimer.Start();
        _voiceService.MicLevelChanged += level =>
        {
            SafeDispatchAsync(() =>
            {
                MicLevel = level * 200;
                // Update speaking indicator for self — matches when audio is actually being sent
                if (_voiceChannelId.HasValue)
                {
                    var ch = Channels.FirstOrDefault(c => c.Id == _voiceChannelId.Value);
                    var me = ch?.VoiceMembers.FirstOrDefault(m => m.Username == Username);
                    if (me is not null)
                        me.IsSpeaking = _voiceService.IsGateOpen;
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
                if (member is null) return;

                float openThreshold = Settings.MicThreshold;
                float closeThreshold = openThreshold * 0.8f;
                if (level > openThreshold || (member.IsSpeaking && level > closeThreshold))
                {
                    member.IsSpeaking = true;
                    _remoteLastSpoke[remoteUserId] = DateTime.UtcNow;
                }
                else if (_remoteLastSpoke.TryGetValue(remoteUserId, out var lastSpoke)
                         && (DateTime.UtcNow - lastSpoke) > RemoteSpeakingHoldTime)
                {
                    member.IsSpeaking = false;
                }
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
            if (e.PropertyName == nameof(AppSettings.MicThreshold))
                _voiceService.MicThreshold = Settings.MicThreshold;
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

    private bool ShouldShowHeader(MessageViewModel msg, MessageViewModel? prev) =>
        !Settings.GroupMessages || prev is null || prev.Author != msg.Author
        || (msg.Timestamp - prev.Timestamp).TotalMinutes > 5;

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
            var connectedUsers = await _chatService.GetConnectedUsers();
            _allCommands = await _chatService.GetAvailableCommandsAsync();
            _allKnownUsers = await _chatService.GetAllKnownUsersAsync();

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
                            var info = new VoiceMemberInfo { Username = m.Username, VoiceUserId = m.VoiceUserId };
                            SubscribeVoiceMemberVolume(info);
                            vm.VoiceMembers.Add(info);
                        }
                    }
                    Channels.Add(vm);
                }

                ConnectedUsers.Clear();
                foreach (var u in connectedUsers)
                    ConnectedUsers.Add(u);
                OnPropertyChanged(nameof(ConnectedUsersHeader));
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
            _typingUsers.Clear();
            TypingText = string.Empty;
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
            SafeDispatch(() =>
            {
                var vm = new MessageViewModel(msg, Settings, _chatService.BaseUrl);
                vm.ShowHeader = ShouldShowHeader(vm, Messages.LastOrDefault());
                Messages.Add(vm);
            });
    }

    private void OnUserTyping(int channelId, string username)
    {
        if (channelId != _selectedTextChannel?.Id) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _typingUsers[username] = DateTime.UtcNow;
            UpdateTypingText();
        });
    }

    private void CleanupTypingUsers()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-3);
        var expired = _typingUsers.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList();
        if (expired.Count == 0) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            foreach (var key in expired)
                _typingUsers.Remove(key);
            UpdateTypingText();
        });
    }

    private void UpdateTypingText()
    {
        var users = _typingUsers.Keys.ToList();
        TypingText = users.Count switch
        {
            0 => string.Empty,
            1 => $"{users[0]} is typing...",
            2 => $"{users[0]} and {users[1]} are typing...",
            _ => "Several people are typing..."
        };
    }

    private void OnNewMessageIndicator(NewMessageIndicatorDto indicator)
    {
        if (indicator.Author == Username) return;

        SafeDispatchAsync(() =>
        {
            if (indicator.ChannelId != _selectedTextChannel?.Id)
            {
                var channel = Channels.FirstOrDefault(c => c.Id == indicator.ChannelId);
                if (channel is not null)
                {
                    channel.UnreadCount++;
                    if (indicator.MentionedUsers.Any(u => string.Equals(u, Username, StringComparison.OrdinalIgnoreCase)))
                        channel.MentionCount++;
                }
            }
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

    private void OnVoiceMembersReceived(int channelId, List<VoiceMemberDto> members)
    {
        SafeDispatch(() =>
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

    private void OnUserConnected(string username)
    {
        SafeDispatch(() =>
        {
            if (!ConnectedUsers.Contains(username))
            {
                // Insert in sorted order
                int i = 0;
                while (i < ConnectedUsers.Count && string.Compare(ConnectedUsers[i], username, StringComparison.OrdinalIgnoreCase) < 0)
                    i++;
                ConnectedUsers.Insert(i, username);
                OnPropertyChanged(nameof(ConnectedUsersHeader));
            }
        });
    }

    private void OnUserDisconnected(string username)
    {
        SafeDispatch(() =>
        {
            ConnectedUsers.Remove(username);
            OnPropertyChanged(nameof(ConnectedUsersHeader));

            foreach (var ch in Channels)
            {
                var member = ch.VoiceMembers.FirstOrDefault(m => m.Username == username);
                if (member is not null)
                    ch.VoiceMembers.Remove(member);
            }
            if (_typingUsers.Remove(username))
                UpdateTypingText();
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

    public void OpenImagePreviewModal(string imageUrl) =>
        ActiveModal = new ImagePreviewModalViewModel(imageUrl);

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
            ConnectedUsers.Clear();
            OnPropertyChanged(nameof(ConnectedUsersHeader));
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
        _settingsStore.Set("Username",       Username);
        _settingsStore.Set("WindowLeft",      _windowLeft);
        _settingsStore.Set("WindowTop",       _windowTop);
        _settingsStore.Set("WindowWidth",     _windowWidth);
        _settingsStore.Set("WindowHeight",    _windowHeight);
        _settingsStore.Set("WindowMaximized", _windowMaximized);
    }

    public async ValueTask DisposeAsync()
    {
        _typingCleanupTimer.Stop();
        _typingCleanupTimer.Dispose();
        _settingsSaveTimer.Stop();
        _remoteSpeakingTimer.Stop();
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
