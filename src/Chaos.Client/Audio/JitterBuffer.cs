namespace Chaos.Client.Audio;

public class JitterBuffer
{
    private readonly SortedDictionary<ushort, byte[]> _buffer = new();
    private readonly OpusCodec _codec;
    private ushort _nextExpectedSeq;
    private bool _started;
    private int _targetDepth = 3; // packets (60ms at 20ms/frame)
    private int _minDepth = 1;
    private int _maxDepth = 10;

    // Jitter tracking for adaptive depth
    private readonly Queue<double> _jitterSamples = new();
    private DateTime _lastArrival = DateTime.MinValue;
    private const int JitterWindowSize = 50;

    public JitterBuffer(OpusCodec codec)
    {
        _codec = codec;
    }

    public int TargetDepth => _targetDepth;
    public int BufferCount => _buffer.Count;

    public void Push(ushort seq, byte[] opusData)
    {
        if (!_started)
        {
            _nextExpectedSeq = seq;
            _started = true;
        }

        // Track jitter
        var now = DateTime.UtcNow;
        if (_lastArrival != DateTime.MinValue)
        {
            double interval = (now - _lastArrival).TotalMilliseconds;
            double jitter = Math.Abs(interval - 20.0); // deviation from expected 20ms
            _jitterSamples.Enqueue(jitter);
            while (_jitterSamples.Count > JitterWindowSize)
                _jitterSamples.Dequeue();

            // Adapt target depth based on jitter variance
            if (_jitterSamples.Count >= 20)
            {
                double avgJitter = _jitterSamples.Average();
                if (avgJitter > 30 && _targetDepth < _maxDepth)
                    _targetDepth++;
                else if (avgJitter < 10 && _targetDepth > _minDepth)
                    _targetDepth--;
            }
        }
        _lastArrival = now;

        // Don't insert very old packets
        int age = SeqDiff(seq, _nextExpectedSeq);
        if (age < -50) return;

        _buffer[seq] = opusData;

        // Prevent unbounded growth
        while (_buffer.Count > _maxDepth * 3)
        {
            _buffer.Remove(_buffer.Keys.First());
        }
    }

    public short[]? Pop()
    {
        if (!_started)
            return null;

        // Wait until we have enough buffered packets before starting playback
        if (_buffer.Count < _targetDepth && _buffer.Count > 0)
        {
            // Check if oldest packet is getting stale
            int oldest = SeqDiff(_buffer.Keys.First(), _nextExpectedSeq);
            if (oldest > _maxDepth * 2)
            {
                // Buffer has only future packets far ahead — reset
                _nextExpectedSeq = _buffer.Keys.First();
            }
            else
            {
                return null; // still buffering
            }
        }

        if (_buffer.Count == 0)
            return null; // silence

        var pcm = new short[OpusCodec.FrameSize];

        if (_buffer.ContainsKey(_nextExpectedSeq))
        {
            // Normal case: expected packet available
            var opusData = _buffer[_nextExpectedSeq];
            _buffer.Remove(_nextExpectedSeq);
            _codec.Decode(opusData, opusData.Length, pcm);
        }
        else
        {
            // Missing packet — check if we have later packets
            ushort firstAvailable = _buffer.Keys.First();
            int gap = SeqDiff(firstAvailable, _nextExpectedSeq);

            if (gap > 0 && gap <= 3)
            {
                // Small gap: use PLC for the missing packet
                _codec.DecodePLC(pcm);
            }
            else if (gap > 3)
            {
                // Large gap: skip ahead to available packet
                _nextExpectedSeq = firstAvailable;
                var opusData = _buffer[_nextExpectedSeq];
                _buffer.Remove(_nextExpectedSeq);
                _codec.Decode(opusData, opusData.Length, pcm);
            }
            else
            {
                return null;
            }
        }

        _nextExpectedSeq++;
        return pcm;
    }

    public void Reset()
    {
        _buffer.Clear();
        _started = false;
        _jitterSamples.Clear();
        _lastArrival = DateTime.MinValue;
        _targetDepth = 3;
    }

    // Returns signed difference handling ushort wraparound
    private static int SeqDiff(ushort a, ushort b)
    {
        return (short)(a - b);
    }
}
