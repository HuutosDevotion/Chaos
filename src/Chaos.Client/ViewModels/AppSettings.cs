using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows;
using Chaos.Shared;

namespace Chaos.Client.ViewModels;

public class AppSettings : INotifyPropertyChanged
{
    // ── Appearance ────────────────────────────────────────────────────────────

    private double _fontSize = 14;
    private double _messageSpacing = 4;
    private double _uiScale = 1.0;
    private bool _groupMessages;

    public double FontSize
    {
        get => _fontSize;
        set { if (_fontSize == value) return; _fontSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(HeaderFontSize)); }
    }

    [JsonIgnore]
    public double HeaderFontSize => _fontSize + 2;

    public double MessageSpacing
    {
        get => _messageSpacing;
        set
        {
            if (_messageSpacing == value) return;
            _messageSpacing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MessagePadding));
        }
    }

    [JsonIgnore]
    public Thickness MessagePadding => new(16, _messageSpacing, 16, _messageSpacing);

    public double UiScale
    {
        get => _uiScale;
        set { if (Math.Abs(_uiScale - value) < 0.001) return; _uiScale = value; OnPropertyChanged(); }
    }

    public bool GroupMessages
    {
        get => _groupMessages;
        set { if (_groupMessages == value) return; _groupMessages = value; OnPropertyChanged(); }
    }

    // ── Voice ─────────────────────────────────────────────────────────────────

    private string _inputDevice = "Default";
    private string _outputDevice = "Default";
    private float _inputVolume = 1.0f;
    private float _outputVolume = 1.0f;
    private VoiceMode _voiceMode = VoiceMode.VoiceActivity;
    private string _pttKey = "OemTilde";
    private int _pttReleaseDelay = 200;
    private float _vadSensitivity = 0.5f;
    private bool _noiseSuppression = true;
    private int _opusBitrate = 48000;

    public string InputDevice
    {
        get => _inputDevice;
        set { if (_inputDevice == value) return; _inputDevice = value; OnPropertyChanged(); }
    }

    public string OutputDevice
    {
        get => _outputDevice;
        set { if (_outputDevice == value) return; _outputDevice = value; OnPropertyChanged(); }
    }

    public float InputVolume
    {
        get => _inputVolume;
        set { if (Math.Abs(_inputVolume - value) < 0.001f) return; _inputVolume = value; OnPropertyChanged(); }
    }

    public float OutputVolume
    {
        get => _outputVolume;
        set { if (Math.Abs(_outputVolume - value) < 0.001f) return; _outputVolume = value; OnPropertyChanged(); }
    }

    public VoiceMode VoiceMode
    {
        get => _voiceMode;
        set { if (_voiceMode == value) return; _voiceMode = value; OnPropertyChanged(); }
    }

    public string PttKey
    {
        get => _pttKey;
        set { if (_pttKey == value) return; _pttKey = value; OnPropertyChanged(); }
    }

    public int PttReleaseDelay
    {
        get => _pttReleaseDelay;
        set { if (_pttReleaseDelay == value) return; _pttReleaseDelay = value; OnPropertyChanged(); }
    }

    public float VadSensitivity
    {
        get => _vadSensitivity;
        set { if (Math.Abs(_vadSensitivity - value) < 0.001f) return; _vadSensitivity = value; OnPropertyChanged(); }
    }

    public bool NoiseSuppression
    {
        get => _noiseSuppression;
        set { if (_noiseSuppression == value) return; _noiseSuppression = value; OnPropertyChanged(); }
    }

    public int OpusBitrate
    {
        get => _opusBitrate;
        set { if (_opusBitrate == value) return; _opusBitrate = value; OnPropertyChanged(); }
    }

    // ── Screen Share ──────────────────────────────────────────────────────────

    private StreamQuality _defaultStreamQuality = StreamQuality.Medium;
    private int _maxStreamFps = 30;

    public StreamQuality DefaultStreamQuality
    {
        get => _defaultStreamQuality;
        set { if (_defaultStreamQuality == value) return; _defaultStreamQuality = value; OnPropertyChanged(); }
    }

    public int MaxStreamFps
    {
        get => _maxStreamFps;
        set { if (_maxStreamFps == value) return; _maxStreamFps = Math.Clamp(value, 5, 60); OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
