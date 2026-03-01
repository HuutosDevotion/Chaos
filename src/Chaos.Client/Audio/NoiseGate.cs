namespace Chaos.Client.Audio;

public class NoiseGate
{
    private float _openThreshold = 0.015f;
    private float _closeThreshold = 0.010f;
    private int _holdFrames = 10; // ~200ms hold at 20ms frames
    private int _holdCounter;
    private bool _isOpen;

    // Fade ramp state for click-free transitions (10ms = 480 samples at 48kHz)
    private const int RampSamples = 480;
    private int _fadePosition; // 0 = fully closed, RampSamples = fully open

    private float _sensitivity = 0.5f;
    public float Sensitivity
    {
        get => _sensitivity;
        set
        {
            _sensitivity = Math.Clamp(value, 0f, 1f);
            // Map 0.0-1.0 sensitivity to threshold range
            // High sensitivity (1.0) = low threshold (more sensitive)
            // Low sensitivity (0.0) = high threshold (less sensitive)
            _openThreshold = 0.005f + (1f - _sensitivity) * 0.045f;
            _closeThreshold = _openThreshold * 0.67f;
        }
    }

    public bool IsOpen => _isOpen;
    public float LastRms { get; private set; }

    public bool Process(short[] pcm, int count)
    {
        float rms = CalculateRMS(pcm, count);
        LastRms = rms;

        if (rms > _openThreshold)
        {
            _isOpen = true;
            _holdCounter = _holdFrames;
        }
        else if (_isOpen)
        {
            _holdCounter--;
            if (_holdCounter <= 0)
                _isOpen = false;
        }

        // Apply fade ramp to avoid clicks at gate transitions
        if (_isOpen && _fadePosition < RampSamples)
            ApplyFadeIn(pcm, count);
        else if (!_isOpen && _fadePosition > 0)
            ApplyFadeOut(pcm, count);

        if (!_isOpen && _fadePosition <= 0)
            return false;

        return true;
    }

    private void ApplyFadeIn(short[] pcm, int count)
    {
        int samplesToProcess = Math.Min(count, RampSamples - _fadePosition);
        for (int i = 0; i < samplesToProcess; i++)
        {
            float gain = (float)(_fadePosition + i) / RampSamples;
            pcm[i] = (short)(pcm[i] * gain);
        }
        _fadePosition = Math.Min(_fadePosition + samplesToProcess, RampSamples);
    }

    private void ApplyFadeOut(short[] pcm, int count)
    {
        int samplesToProcess = Math.Min(count, _fadePosition);
        for (int i = 0; i < samplesToProcess; i++)
        {
            float gain = (float)(_fadePosition - i) / RampSamples;
            pcm[i] = (short)(pcm[i] * gain);
        }
        _fadePosition = Math.Max(_fadePosition - samplesToProcess, 0);

        // Zero out remaining samples if gate just closed
        if (_fadePosition <= 0)
        {
            for (int i = samplesToProcess; i < count; i++)
                pcm[i] = 0;
        }
    }

    private static float CalculateRMS(short[] pcm, int count)
    {
        if (count == 0) return 0;
        double sum = 0;
        for (int i = 0; i < count; i++)
        {
            float normalized = pcm[i] / 32768f;
            sum += normalized * normalized;
        }
        return (float)Math.Sqrt(sum / count);
    }
}
