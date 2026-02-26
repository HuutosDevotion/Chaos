using System.Globalization;
using Chaos.Client.Converters;
using Chaos.Client.Services;
using Chaos.Client.ViewModels;
using Xunit;

namespace Chaos.Tests;

// ── VoiceMemberInfo.Volume ────────────────────────────────────────────────────

public class VoiceMemberVolumeTests
{
    [Fact]
    public void Volume_DefaultsToOne()
    {
        var member = new VoiceMemberInfo();
        Assert.Equal(1.0f, member.Volume);
    }

    [Fact]
    public void Volume_FiresPropertyChanged_WhenValueChanges()
    {
        var member = new VoiceMemberInfo();
        string? firedProperty = null;
        member.PropertyChanged += (_, e) => firedProperty = e.PropertyName;

        member.Volume = 0.5f;

        Assert.Equal(nameof(VoiceMemberInfo.Volume), firedProperty);
    }

    [Fact]
    public void Volume_PropertyChangedReflectsNewValue()
    {
        var member = new VoiceMemberInfo();
        float captured = -1f;
        member.PropertyChanged += (_, _) => captured = member.Volume;

        member.Volume = 0.75f;

        Assert.Equal(0.75f, captured);
    }

    [Fact]
    public void Volume_DoesNotFire_WhenDifferenceIsBelowThreshold()
    {
        var member = new VoiceMemberInfo(); // starts at 1.0
        var fired = false;
        member.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VoiceMemberInfo.Volume)) fired = true;
        };

        member.Volume = 1.0f + 0.0005f; // Δ = 0.0005 < 0.001 threshold

        Assert.False(fired);
    }

    [Fact]
    public void Volume_Fires_WhenDifferenceExceedsThreshold()
    {
        var member = new VoiceMemberInfo(); // starts at 1.0
        var fired = false;
        member.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VoiceMemberInfo.Volume)) fired = true;
        };

        member.Volume = 1.0f - 0.01f; // Δ = 0.01 > 0.001 threshold

        Assert.True(fired);
    }

    [Fact]
    public void Volume_Change_DoesNotFireIsSpeakingChanged()
    {
        var member = new VoiceMemberInfo();
        var fired = false;
        member.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VoiceMemberInfo.IsSpeaking)) fired = true;
        };

        member.Volume = 0.5f;

        Assert.False(fired);
    }

    [Fact]
    public void IsSpeaking_Change_DoesNotFireVolumeChanged()
    {
        var member = new VoiceMemberInfo();
        var fired = false;
        member.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VoiceMemberInfo.Volume)) fired = true;
        };

        member.IsSpeaking = true;

        Assert.False(fired);
    }
}

// ── VolumeToPercentConverter ──────────────────────────────────────────────────

public class VolumeToPercentConverterTests
{
    private readonly VolumeToPercentConverter _converter = new();
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(0f,     "0%")]
    [InlineData(0.5f,   "50%")]
    [InlineData(1.0f,   "100%")]
    [InlineData(0.75f,  "75%")]
    [InlineData(0.333f, "33%")]
    [InlineData(0.995f, "100%")] // rounds up
    [InlineData(0.004f, "0%")]   // rounds down
    public void Convert_Float_ReturnsCorrectPercentString(float input, string expected)
    {
        var result = _converter.Convert(input, typeof(string), null!, Culture);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0.0,  "0%")]
    [InlineData(0.5,  "50%")]
    [InlineData(1.0,  "100%")]
    public void Convert_Double_ReturnsCorrectPercentString(double input, string expected)
    {
        var result = _converter.Convert(input, typeof(string), null!, Culture);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Convert_UnrecognisedType_ReturnsFallback()
    {
        Assert.Equal("100%", _converter.Convert("not a number", typeof(string), null!, Culture));
    }

    [Fact]
    public void Convert_Null_ReturnsFallback()
    {
        Assert.Equal("100%", _converter.Convert(null!, typeof(string), null!, Culture));
    }

    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        Assert.Throws<NotImplementedException>(() =>
            _converter.ConvertBack("50%", typeof(float), null!, Culture));
    }
}

// ── VoiceService.SetUserVolume ────────────────────────────────────────────────

public class VoiceServiceVolumeTests
{
    // SetUserVolume before Start() — _userStreams is empty so no WaveOutEvent is
    // touched; all tests below exercise the clamping + dictionary path only.

    [Fact]
    public void SetUserVolume_DoesNotThrow_WhenNoStreamExists()
    {
        using var svc = new VoiceService();
        svc.SetUserVolume(42, 0.5f);
    }

    [Fact]
    public void SetUserVolume_DoesNotThrow_ForValueAboveOne()
    {
        using var svc = new VoiceService();
        svc.SetUserVolume(42, 2.0f); // clamped to 1.0 internally
    }

    [Fact]
    public void SetUserVolume_DoesNotThrow_ForNegativeValue()
    {
        using var svc = new VoiceService();
        svc.SetUserVolume(42, -1.0f); // clamped to 0.0 internally
    }

    [Fact]
    public void SetUserVolume_DoesNotThrow_ForMultipleUsers()
    {
        using var svc = new VoiceService();
        svc.SetUserVolume(1, 0.8f);
        svc.SetUserVolume(2, 0.3f);
        svc.SetUserVolume(3, 1.0f);
    }

    [Fact]
    public void SetUserVolume_DoesNotThrow_AfterStop()
    {
        using var svc = new VoiceService();
        svc.SetUserVolume(1, 0.5f);
        svc.Stop(); // clears _userStreams and _userVolumes
        svc.SetUserVolume(1, 0.9f); // must not throw on fresh empty state
    }
}
