using Chaos.Client.Services;

namespace Chaos.Client.Services;

public class ConnectionQualityService : IDisposable
{
    private readonly ChatService _chatService;
    private Timer? _pingTimer;
    private DateTime _pingSentAt;
    private bool _pongRegistered;

    // Packet loss tracking per user
    private readonly Dictionary<int, ushort> _lastSeqPerUser = new();
    private int _packetsReceived;
    private int _packetsExpected;
    private DateTime _lastLossReset = DateTime.UtcNow;

    public int PingMs { get; private set; }
    public double PacketLossPercent { get; private set; }
    public int QualityBars { get; private set; } = 4;
    public event Action? Updated;

    public ConnectionQualityService(ChatService chatService)
    {
        _chatService = chatService;
    }

    public void Start()
    {
        if (!_pongRegistered)
        {
            _chatService.PongReceived += OnPong;
            _pongRegistered = true;
        }

        _pingTimer = new Timer(async _ =>
        {
            try
            {
                _pingSentAt = DateTime.UtcNow;
                await _chatService.SendPing();
            }
            catch { }
        }, null, 0, 5000);
    }

    public void Stop()
    {
        _pingTimer?.Dispose();
        _pingTimer = null;
        PingMs = 0;
        PacketLossPercent = 0;
        QualityBars = 4;
        _lastSeqPerUser.Clear();
        _packetsReceived = 0;
        _packetsExpected = 0;
    }

    private void OnPong()
    {
        PingMs = (int)(DateTime.UtcNow - _pingSentAt).TotalMilliseconds;
        RecalculateQuality();
        Updated?.Invoke();
    }

    public void OnVoicePacketReceived(int userId, ushort seqNum)
    {
        _packetsReceived++;

        if (_lastSeqPerUser.TryGetValue(userId, out var lastSeq))
        {
            int gap = (ushort)(seqNum - lastSeq);
            if (gap > 0 && gap < 100) // reasonable gap
                _packetsExpected += gap;
            else
                _packetsExpected++;
        }
        else
        {
            _packetsExpected++;
        }

        _lastSeqPerUser[userId] = seqNum;

        // Recalculate loss every 5 seconds
        if ((DateTime.UtcNow - _lastLossReset).TotalSeconds >= 5)
        {
            if (_packetsExpected > 0)
                PacketLossPercent = Math.Max(0, 100.0 * (1.0 - (double)_packetsReceived / _packetsExpected));
            else
                PacketLossPercent = 0;

            _packetsReceived = 0;
            _packetsExpected = 0;
            _lastLossReset = DateTime.UtcNow;

            RecalculateQuality();
            Updated?.Invoke();
        }
    }

    private void RecalculateQuality()
    {
        // QualityBars: 4 = <80ms+<2% loss, 3 = <150ms+<5%, 2 = <250ms+<15%, 1 = rest
        if (PingMs < 80 && PacketLossPercent < 2)
            QualityBars = 4;
        else if (PingMs < 150 && PacketLossPercent < 5)
            QualityBars = 3;
        else if (PingMs < 250 && PacketLossPercent < 15)
            QualityBars = 2;
        else
            QualityBars = 1;
    }

    public void Dispose()
    {
        Stop();
        if (_pongRegistered)
        {
            _chatService.PongReceived -= OnPong;
            _pongRegistered = false;
        }
    }
}
