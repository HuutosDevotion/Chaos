using System.Windows;
using Chaos.Client.ViewModels;
using Xunit;

namespace Chaos.Client.Tests;

/// <summary>
/// Unit tests for AppSettings — verifies default values, INotifyPropertyChanged
/// behaviour (including no-op guard and threshold guards), and computed properties.
/// </summary>
public class AppSettingsTests
{
    // ── default values ─────────────────────────────────────────────────────────

    [Fact]
    public void FontSize_DefaultsTo14() => Assert.Equal(14.0, new AppSettings().FontSize);

    [Fact]
    public void MessageSpacing_DefaultsTo4() => Assert.Equal(4.0, new AppSettings().MessageSpacing);

    [Fact]
    public void UiScale_DefaultsTo1() => Assert.Equal(1.0, new AppSettings().UiScale);

    [Fact]
    public void GroupMessages_DefaultsFalse() => Assert.False(new AppSettings().GroupMessages);

    [Fact]
    public void InputDevice_DefaultsToDefault() => Assert.Equal("Default", new AppSettings().InputDevice);

    [Fact]
    public void OutputDevice_DefaultsToDefault() => Assert.Equal("Default", new AppSettings().OutputDevice);

    [Fact]
    public void InputVolume_DefaultsTo1() => Assert.Equal(1.0f, new AppSettings().InputVolume);

    [Fact]
    public void OutputVolume_DefaultsTo1() => Assert.Equal(1.0f, new AppSettings().OutputVolume);

    // ── MessagePadding computed property ───────────────────────────────────────

    [Fact]
    public void MessagePadding_DefaultsToSpacing4()
    {
        Assert.Equal(new Thickness(16, 4, 16, 4), new AppSettings().MessagePadding);
    }

    [Fact]
    public void MessagePadding_ReflectsChangedMessageSpacing()
    {
        var s = new AppSettings { MessageSpacing = 8.0 };
        Assert.Equal(new Thickness(16, 8, 16, 8), s.MessagePadding);
    }

    [Fact]
    public void MessagePadding_ZeroSpacing_HasZeroTopBottom()
    {
        var s = new AppSettings { MessageSpacing = 0.0 };
        Assert.Equal(new Thickness(16, 0, 16, 0), s.MessagePadding);
    }

    // ── INotifyPropertyChanged — FontSize ──────────────────────────────────────

    [Fact]
    public void FontSize_SetNewValue_RaisesPropertyChanged()
    {
        var s = new AppSettings();
        var raised = new List<string?>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        s.FontSize = 20.0;

        Assert.Contains(nameof(AppSettings.FontSize), raised);
    }

    [Fact]
    public void FontSize_SetSameValue_DoesNotRaise()
    {
        var s = new AppSettings();
        var raised = false;
        s.PropertyChanged += (_, _) => raised = true;

        s.FontSize = 14.0; // same as default

        Assert.False(raised);
    }

    // ── INotifyPropertyChanged — MessageSpacing ────────────────────────────────

    [Fact]
    public void MessageSpacing_SetNewValue_RaisesBothSpacingAndPadding()
    {
        var s = new AppSettings();
        var raised = new List<string?>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        s.MessageSpacing = 8.0;

        Assert.Contains(nameof(AppSettings.MessageSpacing), raised);
        Assert.Contains(nameof(AppSettings.MessagePadding), raised);
    }

    [Fact]
    public void MessageSpacing_SetSameValue_DoesNotRaise()
    {
        var s = new AppSettings();
        var raised = false;
        s.PropertyChanged += (_, _) => raised = true;

        s.MessageSpacing = 4.0;

        Assert.False(raised);
    }

    // ── INotifyPropertyChanged — UiScale (threshold guard) ────────────────────

    [Fact]
    public void UiScale_SetNewValue_RaisesPropertyChanged()
    {
        var s = new AppSettings();
        var raised = new List<string?>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        s.UiScale = 1.5;

        Assert.Contains(nameof(AppSettings.UiScale), raised);
    }

    [Fact]
    public void UiScale_SetValueBelowThreshold_DoesNotRaise()
    {
        var s = new AppSettings(); // starts at 1.0
        var raised = false;
        s.PropertyChanged += (_, _) => raised = true;

        s.UiScale = 1.0 + 0.0005; // Δ = 0.0005 < 0.001 threshold

        Assert.False(raised);
    }

    [Fact]
    public void UiScale_SetValueAboveThreshold_Raises()
    {
        var s = new AppSettings();
        var raised = false;
        s.PropertyChanged += (_, _) => raised = true;

        s.UiScale = 1.0 + 0.01; // Δ = 0.01 > 0.001 threshold

        Assert.True(raised);
    }

    // ── INotifyPropertyChanged — GroupMessages ─────────────────────────────────

    [Fact]
    public void GroupMessages_SetTrue_RaisesPropertyChanged()
    {
        var s = new AppSettings();
        var raised = new List<string?>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        s.GroupMessages = true;

        Assert.Contains(nameof(AppSettings.GroupMessages), raised);
    }

    [Fact]
    public void GroupMessages_SetSameValue_DoesNotRaise()
    {
        var s = new AppSettings();
        var raised = false;
        s.PropertyChanged += (_, _) => raised = true;

        s.GroupMessages = false;

        Assert.False(raised);
    }

    // ── INotifyPropertyChanged — device and volume properties ─────────────────

    [Fact]
    public void InputDevice_SetNewValue_RaisesPropertyChanged()
    {
        var s = new AppSettings();
        var raised = new List<string?>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        s.InputDevice = "USB Mic";

        Assert.Contains(nameof(AppSettings.InputDevice), raised);
    }

    [Fact]
    public void InputDevice_SetSameValue_DoesNotRaise()
    {
        var s = new AppSettings();
        var raised = false;
        s.PropertyChanged += (_, _) => raised = true;

        s.InputDevice = "Default";

        Assert.False(raised);
    }

    [Fact]
    public void OutputDevice_SetNewValue_RaisesPropertyChanged()
    {
        var s = new AppSettings();
        var raised = new List<string?>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        s.OutputDevice = "Speakers";

        Assert.Contains(nameof(AppSettings.OutputDevice), raised);
    }

    [Fact]
    public void InputVolume_SetNewValue_RaisesPropertyChanged()
    {
        var s = new AppSettings();
        var raised = new List<string?>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        s.InputVolume = 0.5f;

        Assert.Contains(nameof(AppSettings.InputVolume), raised);
    }

    [Fact]
    public void InputVolume_SetNearSameValue_DoesNotRaise()
    {
        var s = new AppSettings(); // starts at 1.0f
        var raised = false;
        s.PropertyChanged += (_, _) => raised = true;

        s.InputVolume = 1.0f + 0.0005f; // below 0.001f threshold

        Assert.False(raised);
    }

    [Fact]
    public void OutputVolume_SetNewValue_RaisesPropertyChanged()
    {
        var s = new AppSettings();
        var raised = new List<string?>();
        s.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        s.OutputVolume = 0.75f;

        Assert.Contains(nameof(AppSettings.OutputVolume), raised);
    }

    [Fact]
    public void OutputVolume_SetNearSameValue_DoesNotRaise()
    {
        var s = new AppSettings();
        var raised = false;
        s.PropertyChanged += (_, _) => raised = true;

        s.OutputVolume = 1.0f + 0.0005f;

        Assert.False(raised);
    }
}
