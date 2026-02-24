using System.Net;
using System.Net.Sockets;
using NAudio.Wave;

namespace Chaos.Client.Services;

public class VoiceService : IDisposable
{
    private WaveInEvent? _waveIn;
    private BufferedWaveProvider? _bufferedProvider;
    private WaveOutEvent? _waveOut;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;

    private int _userId;
    private int _channelId;
    private IPEndPoint? _serverEndpoint;

    private bool _isMuted;
    private bool _isDeafened;
    private bool _isActive;

    private static readonly byte[] RegistrationMagic = "RGST"u8.ToArray();
    private static readonly WaveFormat VoiceFormat = new(16000, 16, 1);

    public event Action<float>? MicLevelChanged; // 0.0 to 1.0
    public event Action<string>? Error;
    public event Action<int, float>? RemoteAudioLevel; // userId, level (0.0-1.0)

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            if (_waveIn is not null)
            {
                if (_isMuted)
                    _waveIn.StopRecording();
                else if (_isActive)
                    _waveIn.StartRecording();
            }
        }
    }

    public bool IsDeafened
    {
        get => _isDeafened;
        set
        {
            _isDeafened = value;
            if (_waveOut is not null)
            {
                if (_isDeafened)
                    _waveOut.Stop();
                else if (_isActive)
                    _waveOut.Play();
            }
        }
    }

    public bool IsActive => _isActive;

    public void Start(string serverHost, int serverPort, int userId, int channelId)
    {
        Stop();

        _userId = userId;
        _channelId = channelId;

        // Resolve server address once upfront
        var ip = ResolveHost(serverHost);
        _serverEndpoint = new IPEndPoint(ip, serverPort);
        System.Diagnostics.Debug.WriteLine($"[Voice] Server endpoint: {_serverEndpoint}");

        // Check for recording devices
        int deviceCount = WaveInEvent.DeviceCount;
        if (deviceCount == 0)
        {
            Error?.Invoke("No microphone found. Check your audio input devices.");
            return;
        }

        // Log available devices
        var defaultDevice = WaveInEvent.GetCapabilities(0);
        System.Diagnostics.Debug.WriteLine($"[Voice] Found {deviceCount} input device(s). Using: {defaultDevice.ProductName}");

        _udpClient = new UdpClient(_serverEndpoint.AddressFamily);

        // Send registration packet
        var regPacket = new byte[12];
        BitConverter.GetBytes(userId).CopyTo(regPacket, 0);
        BitConverter.GetBytes(channelId).CopyTo(regPacket, 4);
        RegistrationMagic.CopyTo(regPacket, 8);
        _udpClient.Send(regPacket, regPacket.Length, _serverEndpoint);

        try
        {
            // Setup audio capture
            _waveIn = new WaveInEvent
            {
                WaveFormat = VoiceFormat,
                BufferMilliseconds = 40
            };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += (_, args) =>
            {
                if (args.Exception is not null)
                    Error?.Invoke($"Recording error: {args.Exception.Message}");
            };

            // Setup audio playback
            _bufferedProvider = new BufferedWaveProvider(VoiceFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(2)
            };
            _waveOut = new WaveOutEvent();
            _waveOut.Init(_bufferedProvider);

            // Start receiving
            _receiveCts = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ReceiveLoop(_receiveCts.Token));

            // Start capture and playback
            if (!_isMuted) _waveIn.StartRecording();
            if (!_isDeafened) _waveOut.Play();

            _isActive = true;
            System.Diagnostics.Debug.WriteLine("[Voice] Started successfully");
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Voice start failed: {ex.Message}");
            Stop();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        // Calculate mic level from PCM samples (16-bit signed)
        float maxSample = 0;
        for (int i = 0; i < e.BytesRecorded - 1; i += 2)
        {
            short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
            float abs = Math.Abs(sample / 32768f);
            if (abs > maxSample) maxSample = abs;
        }
        MicLevelChanged?.Invoke(maxSample);

        if (_isMuted || _udpClient is null || _serverEndpoint is null)
            return;

        // Build packet: 4 bytes userId + 4 bytes channelId + audio data
        var packet = new byte[8 + e.BytesRecorded];
        BitConverter.GetBytes(_userId).CopyTo(packet, 0);
        BitConverter.GetBytes(_channelId).CopyTo(packet, 4);
        Buffer.BlockCopy(e.Buffer, 0, packet, 8, e.BytesRecorded);

        try
        {
            _udpClient.Send(packet, packet.Length, _serverEndpoint);
        }
        catch { }
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _udpClient is not null)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(ct);
                if (result.Buffer.Length < 8)
                    continue;

                int senderId = BitConverter.ToInt32(result.Buffer, 0);
                var audioLength = result.Buffer.Length - 8;

                // Calculate remote user's audio level
                if (audioLength > 0)
                {
                    float maxSample = 0;
                    for (int i = 8; i < result.Buffer.Length - 1; i += 2)
                    {
                        short sample = (short)(result.Buffer[i] | (result.Buffer[i + 1] << 8));
                        float abs = Math.Abs(sample / 32768f);
                        if (abs > maxSample) maxSample = abs;
                    }
                    RemoteAudioLevel?.Invoke(senderId, maxSample);
                }

                if (_isDeafened)
                    continue;

                // Play audio
                if (audioLength > 0 && _bufferedProvider is not null)
                {
                    _bufferedProvider.AddSamples(result.Buffer, 8, audioLength);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch { }
        }
    }

    public void Stop()
    {
        _isActive = false;
        _receiveCts?.Cancel();

        try { _waveIn?.StopRecording(); } catch { }
        try { _waveOut?.Stop(); } catch { }

        _waveIn?.Dispose();
        _waveOut?.Dispose();
        _udpClient?.Close();
        _udpClient?.Dispose();

        _waveIn = null;
        _waveOut = null;
        _bufferedProvider = null;
        _udpClient = null;
        _receiveCts = null;
    }

    public void Dispose()
    {
        Stop();
    }

    private static IPAddress ResolveHost(string host)
    {
        if (IPAddress.TryParse(host, out var parsed))
            return parsed;
        var addresses = Dns.GetHostAddresses(host);
        // Prefer IPv4 to match server's UDP listener
        return addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            ?? addresses.First();
    }
}
