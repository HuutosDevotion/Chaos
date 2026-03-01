using Concentus.Enums;
using Concentus.Structs;

namespace Chaos.Client.Audio;

public class OpusCodec : IDisposable
{
    public const int SampleRate = 48000;
    public const int Channels = 1;
    public const int FrameSize = 960; // 20ms at 48kHz
    public const int FrameBytes = FrameSize * 2; // 16-bit = 2 bytes per sample
    public const int DefaultBitrate = 48000; // 48 kbps

    private readonly OpusEncoder _encoder;
    private readonly OpusDecoder _decoder;
    private bool _disposed;

    public OpusCodec(int bitrate = DefaultBitrate)
    {
#pragma warning disable CS0618 // Obsolete constructor still functional in Concentus 2.x
        _encoder = new OpusEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_VOIP);
        _decoder = new OpusDecoder(SampleRate, Channels);
#pragma warning restore CS0618

        _encoder.Bitrate = bitrate;
        _encoder.Complexity = 5;
        _encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
    }

    public int Bitrate
    {
        get => _encoder.Bitrate;
        set => _encoder.Bitrate = value;
    }

    public int Encode(short[] pcm, byte[] output)
    {
        return _encoder.Encode(pcm.AsSpan(), FrameSize, output.AsSpan(), output.Length);
    }

    public int Decode(byte[] opus, int opusLen, short[] pcm)
    {
        return _decoder.Decode(opus.AsSpan(0, opusLen), pcm.AsSpan(), FrameSize, false);
    }

    public int DecodePLC(short[] pcm)
    {
        return _decoder.Decode(ReadOnlySpan<byte>.Empty, pcm.AsSpan(), FrameSize, false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _encoder.Dispose();
        _decoder.Dispose();
    }
}
