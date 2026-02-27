using System.Net;
using System.Net.Sockets;
using Chaos.Client.Audio;
using NAudio.Wave;

namespace Chaos.Client.Services;

public class VoiceService : IDisposable
{
    private WaveInEvent? _waveIn;
    private readonly Dictionary<int, (WaveOutEvent WaveOut, BufferedWaveProvider Provider)> _userStreams = new();
    private readonly Dictionary<int, float> _userVolumes = new();
    private readonly Dictionary<int, OpusCodec> _decoders = new();
    private readonly Dictionary<int, JitterBuffer> _jitterBuffers = new();
    private UdpClient? _udpClient;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private Timer? _playbackTimer;

    private OpusCodec? _encoder;
    private short[] _pcmAccumulator = new short[OpusCodec.FrameSize];
    private int _pcmAccumulatorPos;
    private byte[] _opusBuffer = new byte[4000]; // max opus output
    private ushort _seqNum;

    private int _userId;
    private int _channelId;
    private IPEndPoint? _serverEndpoint;

    private bool _isMuted;
    private bool _isDeafened;
    private bool _isActive;

    // Noise gate / PTT integration points
    private Func<short[], int, bool>? _transmitGate; // returns true if should transmit
    private Func<bool>? _pttCheck; // returns true if PTT key is held

    public string InputDeviceName { get; set; } = "Default";
    public string OutputDeviceName { get; set; } = "Default";
    public float InputVolume { get; set; } = 1.0f;

    private float _outputVolume = 1.0f;
    public float OutputVolume
    {
        get => _outputVolume;
        set
        {
            _outputVolume = value;
            foreach (var (userId, (waveOut, _)) in _userStreams)
            {
                float userVol = _userVolumes.TryGetValue(userId, out var v) ? v : 1.0f;
                waveOut.Volume = Math.Clamp(_outputVolume * userVol, 0f, 1f);
            }
        }
    }

    private static readonly byte[] RegistrationMagic = "RGST"u8.ToArray();
    private static readonly WaveFormat VoiceFormat = new(OpusCodec.SampleRate, 16, OpusCodec.Channels);

    public event Action<float>? MicLevelChanged; // 0.0 to 1.0
    public event Action<string>? Error;
    public event Action<int, float>? RemoteAudioLevel; // userId, level (0.0-1.0)
    public event Action<int, ushort>? PacketReceived; // userId, seqNum (for quality tracking)

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
            foreach (var (waveOut, _) in _userStreams.Values)
            {
                if (_isDeafened) waveOut.Stop();
                else if (_isActive) waveOut.Play();
            }
        }
    }

    public bool IsActive => _isActive;

    public void SetTransmitGate(Func<short[], int, bool>? gate) => _transmitGate = gate;
    public void SetPttCheck(Func<bool>? check) => _pttCheck = check;

    public void Start(string serverHost, int serverPort, int userId, int channelId)
    {
        Stop();

        _userId = userId;
        _channelId = channelId;
        _seqNum = 0;
        _pcmAccumulatorPos = 0;

        var ip = ResolveHost(serverHost);
        _serverEndpoint = new IPEndPoint(ip, serverPort);
        System.Diagnostics.Debug.WriteLine($"[Voice] Server endpoint: {_serverEndpoint}");

        int deviceCount = WaveInEvent.DeviceCount;
        if (deviceCount == 0)
        {
            Error?.Invoke("No microphone found. Check your audio input devices.");
            return;
        }

        var defaultDevice = WaveInEvent.GetCapabilities(0);
        System.Diagnostics.Debug.WriteLine($"[Voice] Found {deviceCount} input device(s). Using: {defaultDevice.ProductName}");

        _udpClient = new UdpClient(_serverEndpoint.AddressFamily);

        // Send registration packet: [4B userId][4B channelId][4B magic "RGST"]
        var regPacket = new byte[12];
        BitConverter.GetBytes(userId).CopyTo(regPacket, 0);
        BitConverter.GetBytes(channelId).CopyTo(regPacket, 4);
        RegistrationMagic.CopyTo(regPacket, 8);
        _udpClient.Send(regPacket, regPacket.Length, _serverEndpoint);

        try
        {
            _encoder = new OpusCodec();

            _waveIn = new WaveInEvent
            {
                WaveFormat = VoiceFormat,
                BufferMilliseconds = 20,
                DeviceNumber = ResolveInputDevice(InputDeviceName)
            };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += (_, args) =>
            {
                if (args.Exception is not null)
                    Error?.Invoke($"Recording error: {args.Exception.Message}");
            };

            _receiveCts = new CancellationTokenSource();
            _receiveTask = Task.Run(() => ReceiveLoop(_receiveCts.Token));

            // 20ms playback timer to drain jitter buffers
            _playbackTimer = new Timer(PlaybackTimerCallback, null, 0, 20);

            if (!_isMuted) _waveIn.StartRecording();

            _isActive = true;
            System.Diagnostics.Debug.WriteLine("[Voice] Started successfully (Opus 48kHz)");
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Voice start failed: {ex.Message}");
            Stop();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        // Convert bytes to shorts, apply input gain, calculate mic level
        int sampleCount = e.BytesRecorded / 2;
        float maxSample = 0;

        for (int i = 0; i < e.BytesRecorded - 1; i += 2)
        {
            short raw = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
            short sample = raw;
            if (InputVolume != 1.0f)
            {
                sample = (short)Math.Clamp(raw * InputVolume, short.MinValue, short.MaxValue);
                e.Buffer[i] = (byte)(sample & 0xFF);
                e.Buffer[i + 1] = (byte)((sample >> 8) & 0xFF);
            }
            float abs = Math.Abs(sample / 32768f);
            if (abs > maxSample) maxSample = abs;

            // Accumulate into frame buffer
            _pcmAccumulator[_pcmAccumulatorPos++] = sample;

            if (_pcmAccumulatorPos >= OpusCodec.FrameSize)
            {
                ProcessFrame(_pcmAccumulator, OpusCodec.FrameSize);
                _pcmAccumulatorPos = 0;
            }
        }

        MicLevelChanged?.Invoke(maxSample);
    }

    private void ProcessFrame(short[] pcm, int count)
    {
        if (_isMuted || _udpClient is null || _serverEndpoint is null || _encoder is null)
            return;

        // Check PTT if set
        if (_pttCheck is not null && !_pttCheck())
            return;

        // Check noise gate if set
        if (_transmitGate is not null && !_transmitGate(pcm, count))
            return;

        // Encode with Opus
        int encodedLen = _encoder.Encode(pcm, _opusBuffer);
        if (encodedLen <= 0)
            return;

        // New packet format: [4B userId][4B channelId][2B seqNum][2B opusLen][opusData...]
        var packet = new byte[12 + encodedLen];
        BitConverter.GetBytes(_userId).CopyTo(packet, 0);
        BitConverter.GetBytes(_channelId).CopyTo(packet, 4);
        BitConverter.GetBytes(_seqNum).CopyTo(packet, 8);
        BitConverter.GetBytes((ushort)encodedLen).CopyTo(packet, 10);
        Buffer.BlockCopy(_opusBuffer, 0, packet, 12, encodedLen);

        _seqNum++;

        try
        {
            _udpClient.Send(packet, packet.Length, _serverEndpoint);
        }
        catch { }
    }

    private (WaveOutEvent WaveOut, BufferedWaveProvider Provider) GetOrCreateUserStream(int userId)
    {
        if (_userStreams.TryGetValue(userId, out var existing))
            return existing;

        var provider = new BufferedWaveProvider(VoiceFormat) { DiscardOnBufferOverflow = true, BufferDuration = TimeSpan.FromSeconds(2) };
        var waveOut = new WaveOutEvent { DeviceNumber = ResolveOutputDevice(OutputDeviceName) };
        waveOut.Init(provider);
        if (!_isDeafened) waveOut.Play();
        _userStreams[userId] = (waveOut, provider);
        return (waveOut, provider);
    }

    private OpusCodec GetOrCreateDecoder(int userId)
    {
        if (_decoders.TryGetValue(userId, out var decoder))
            return decoder;
        decoder = new OpusCodec();
        _decoders[userId] = decoder;
        return decoder;
    }

    private JitterBuffer GetOrCreateJitterBuffer(int userId)
    {
        if (_jitterBuffers.TryGetValue(userId, out var jb))
            return jb;
        var decoder = GetOrCreateDecoder(userId);
        jb = new JitterBuffer(decoder);
        _jitterBuffers[userId] = jb;
        return jb;
    }

    public void SetUserVolume(int userId, float volume)
    {
        volume = Math.Clamp(volume, 0f, 1f);
        _userVolumes[userId] = volume;
    }

    private static void ApplyVolumeInPlace(short[] pcm, int count, float volume)
    {
        for (int i = 0; i < count; i++)
        {
            float scaled = pcm[i] * volume;
            pcm[i] = (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
        }
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _udpClient is not null)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(ct);
                if (result.Buffer.Length < 12)
                    continue;

                int senderId = BitConverter.ToInt32(result.Buffer, 0);
                ushort seqNum = BitConverter.ToUInt16(result.Buffer, 8);
                ushort opusLen = BitConverter.ToUInt16(result.Buffer, 10);

                if (result.Buffer.Length < 12 + opusLen)
                    continue;

                // Notify for quality tracking
                PacketReceived?.Invoke(senderId, seqNum);

                // Extract opus payload and push to jitter buffer
                var opusData = new byte[opusLen];
                Buffer.BlockCopy(result.Buffer, 12, opusData, 0, opusLen);

                var jb = GetOrCreateJitterBuffer(senderId);
                jb.Push(seqNum, opusData);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch { }
        }
    }

    private void PlaybackTimerCallback(object? state)
    {
        if (!_isActive) return;

        foreach (var (userId, jb) in _jitterBuffers)
        {
            var pcm = jb.Pop();
            if (pcm is null)
                continue;

            // Calculate remote audio level
            float maxSample = 0;
            for (int i = 0; i < pcm.Length; i++)
            {
                float abs = Math.Abs(pcm[i] / 32768f);
                if (abs > maxSample) maxSample = abs;
            }
            RemoteAudioLevel?.Invoke(userId, maxSample);

            if (_isDeafened) continue;

            // Apply user volume
            float volume = _userVolumes.TryGetValue(userId, out var vol) ? vol : 1.0f;
            if (volume != 1.0f)
                ApplyVolumeInPlace(pcm, pcm.Length, volume);

            var stream = GetOrCreateUserStream(userId);
            var pcmBytes = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, pcmBytes, 0, pcmBytes.Length);
            stream.Provider.AddSamples(pcmBytes, 0, pcmBytes.Length);
        }
    }

    public void Stop()
    {
        _isActive = false;
        _receiveCts?.Cancel();
        _playbackTimer?.Dispose();
        _playbackTimer = null;

        try { _waveIn?.StopRecording(); } catch { }

        foreach (var (waveOut, _) in _userStreams.Values)
        {
            try { waveOut.Stop(); } catch { }
            waveOut.Dispose();
        }
        _userStreams.Clear();
        _userVolumes.Clear();

        foreach (var decoder in _decoders.Values)
            decoder.Dispose();
        _decoders.Clear();
        _jitterBuffers.Clear();

        _waveIn?.Dispose();
        _udpClient?.Close();
        _udpClient?.Dispose();
        _encoder?.Dispose();

        _waveIn = null;
        _udpClient = null;
        _receiveCts = null;
        _encoder = null;
        _pcmAccumulatorPos = 0;
    }

    public void Dispose()
    {
        Stop();
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
        if (deviceName == "Default") return -1; // wave mapper
        for (int i = 0; i < WaveOut.DeviceCount; i++)
        {
            try { if (WaveOut.GetCapabilities(i).ProductName == deviceName) return i; } catch { }
        }
        return -1;
    }

    private static IPAddress ResolveHost(string host)
    {
        if (IPAddress.TryParse(host, out var parsed))
            return parsed;
        var addresses = Dns.GetHostAddresses(host);
        return addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            ?? addresses.First();
    }
}
