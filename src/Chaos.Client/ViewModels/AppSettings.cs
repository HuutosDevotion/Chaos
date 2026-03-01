using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows;

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
    private float _micThreshold = 0.02f;

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

    public float MicThreshold
    {
        get => _micThreshold;
        set { if (Math.Abs(_micThreshold - value) < 0.001f) return; _micThreshold = value; OnPropertyChanged(); }
    }

    // ── Integrations ─────────────────────────────────────────────────────────

    private string _openAiApiKey = string.Empty;

    public string OpenAiApiKey
    {
        get => _openAiApiKey;
        set { if (_openAiApiKey == value) return; _openAiApiKey = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
