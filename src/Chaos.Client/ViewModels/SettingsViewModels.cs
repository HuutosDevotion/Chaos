using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using NAudio.Wave;

namespace Chaos.Client.ViewModels;

public abstract class SettingsPageViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Name { get; }
    public ICommand Select { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }

    protected SettingsPageViewModel(string name, Action<SettingsPageViewModel> select)
    {
        Name = name;
        Select = new RelayCommand(_ => select(this));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class AppearanceSettingsViewModel : SettingsPageViewModel
{
    public AppSettings Settings { get; }

    public AppearanceSettingsViewModel(AppSettings settings, Action<SettingsPageViewModel> select)
        : base("Appearance", select)
    {
        Settings = settings;
    }
}

public class VoiceSettingsViewModel : SettingsPageViewModel
{
    private static readonly WaveFormat MicTestFormat = new(16000, 16, 1);

    private WaveInEvent? _micTestWaveIn;
    private WaveOutEvent? _micTestWaveOut;
    private BufferedWaveProvider? _micTestProvider;
    private bool _isMicTesting;
    private double _micTestLevel;

    public AppSettings Settings { get; }
    public ObservableCollection<string> InputDevices { get; } = new();
    public ObservableCollection<string> OutputDevices { get; } = new();

    public bool IsMicTesting
    {
        get => _isMicTesting;
        private set
        {
            if (_isMicTesting == value) return;
            _isMicTesting = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MicTestButtonText));
        }
    }

    public double MicTestLevel
    {
        get => _micTestLevel;
        private set { if (Math.Abs(_micTestLevel - value) < 0.001) return; _micTestLevel = value; OnPropertyChanged(); }
    }

    public string MicTestButtonText => IsMicTesting ? "Stop Test" : "Test Microphone";

    public ICommand TestMicCommand { get; }

    public VoiceSettingsViewModel(AppSettings settings, Action<SettingsPageViewModel> select)
        : base("Voice", select)
    {
        Settings = settings;
        TestMicCommand = new RelayCommand(_ => ToggleMicTest());

        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.OutputVolume) && _micTestWaveOut is not null)
                _micTestWaveOut.Volume = Math.Clamp(Settings.OutputVolume, 0f, 1f);
        };

        InputDevices.Add("Default");
        try
        {
            for (int i = 0; i < WaveInEvent.DeviceCount; i++)
                InputDevices.Add(WaveInEvent.GetCapabilities(i).ProductName);
        }
        catch { /* no capture devices available */ }

        OutputDevices.Add("Default");
        try
        {
            for (int i = 0; i < WaveOut.DeviceCount; i++)
                OutputDevices.Add(WaveOut.GetCapabilities(i).ProductName);
        }
        catch { /* no playback devices available */ }
    }

    private void ToggleMicTest()
    {
        if (IsMicTesting) StopMicTest();
        else StartMicTest();
    }

    private void StartMicTest()
    {
        StopMicTest();
        try
        {
            _micTestProvider = new BufferedWaveProvider(MicTestFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromMilliseconds(200)
            };
            _micTestWaveOut = new WaveOutEvent { DeviceNumber = ResolveOutputDevice(Settings.OutputDevice) };
            _micTestWaveOut.Init(_micTestProvider);
            _micTestWaveOut.Volume = Math.Clamp(Settings.OutputVolume, 0f, 1f);
            _micTestWaveOut.Play();

            _micTestWaveIn = new WaveInEvent
            {
                WaveFormat = MicTestFormat,
                BufferMilliseconds = 40,
                DeviceNumber = ResolveInputDevice(Settings.InputDevice)
            };
            _micTestWaveIn.DataAvailable += OnMicTestDataAvailable;
            _micTestWaveIn.StartRecording();
            IsMicTesting = true;
        }
        catch
        {
            StopMicTest();
        }
    }

    public void StopMicTest()
    {
        if (_micTestWaveIn is not null)
        {
            try { _micTestWaveIn.StopRecording(); } catch { }
            _micTestWaveIn.Dispose();
            _micTestWaveIn = null;
        }
        if (_micTestWaveOut is not null)
        {
            try { _micTestWaveOut.Stop(); } catch { }
            _micTestWaveOut.Dispose();
            _micTestWaveOut = null;
        }
        _micTestProvider = null;
        IsMicTesting = false;
        MicTestLevel = 0;
    }

    private void OnMicTestDataAvailable(object? sender, WaveInEventArgs e)
    {
        float maxSample = 0;
        float inputGain = Settings.InputVolume;
        for (int i = 0; i < e.BytesRecorded - 1; i += 2)
        {
            short raw = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
            short sample = raw;
            if (inputGain != 1.0f)
            {
                sample = (short)Math.Clamp(raw * inputGain, short.MinValue, short.MaxValue);
                e.Buffer[i] = (byte)(sample & 0xFF);
                e.Buffer[i + 1] = (byte)((sample >> 8) & 0xFF);
            }
            float abs = Math.Abs(sample / 32768f);
            if (abs > maxSample) maxSample = abs;
        }

        _micTestProvider?.AddSamples(e.Buffer, 0, e.BytesRecorded);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(() => MicTestLevel = maxSample);
    }

    private static int ResolveInputDevice(string deviceName)
    {
        if (deviceName == "Default") return 0;
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            try { if (WaveInEvent.GetCapabilities(i).ProductName == deviceName) return i; } catch { }
        }
        return 0;
    }

    private static int ResolveOutputDevice(string deviceName)
    {
        if (deviceName == "Default") return -1;
        for (int i = 0; i < WaveOut.DeviceCount; i++)
        {
            try { if (WaveOut.GetCapabilities(i).ProductName == deviceName) return i; } catch { }
        }
        return -1;
    }
}

public class SettingsCategoryViewModel
{
    public string Name { get; }
    public IReadOnlyList<SettingsPageViewModel> Pages { get; }

    public SettingsCategoryViewModel(string name, IEnumerable<SettingsPageViewModel> pages)
    {
        Name = name;
        Pages = pages.ToList();
    }
}

public class SettingsModalViewModel : INotifyPropertyChanged
{
    private SettingsPageViewModel? _selectedPage;

    public IReadOnlyList<SettingsCategoryViewModel> Categories { get; }

    public SettingsPageViewModel? SelectedPage
    {
        get => _selectedPage;
        private set
        {
            if (_selectedPage is not null) _selectedPage.IsSelected = false;
            _selectedPage = value;
            if (_selectedPage is not null) _selectedPage.IsSelected = true;
            OnPropertyChanged();
        }
    }

    public ICommand Close { get; }

    public SettingsModalViewModel(AppSettings settings, Action close)
    {
        Action<SettingsPageViewModel> select = p => SelectedPage = p;

        var appearance = new AppearanceSettingsViewModel(settings, select);
        var voice = new VoiceSettingsViewModel(settings, select);

        Categories = new List<SettingsCategoryViewModel>
        {
            new("App Settings", new SettingsPageViewModel[] { appearance, voice })
        };

        Close = new RelayCommand(_ => { voice.StopMicTest(); close(); });
        SelectedPage = appearance;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
