using Chaos.Client.ViewModels;
using Xunit;

namespace Chaos.Client.Tests;

/// <summary>
/// Regression tests for the mute/deafen tooltip text properties.
///
/// The tooltip system uses Tag="{Binding MuteTooltipText}" on the button and
/// TooltipHelper reads element.Tag at ToolTipOpening time to set tooltip content.
/// These tests guard the ViewModel side of that contract:
///   - correct text is returned for each state
///   - text updates when state toggles
///   - PropertyChanged fires so the Tag binding re-evaluates
///   - button text stays icon-only (tooltip is the accessible label)
/// </summary>
public class TooltipTextTests
{
    // ── MuteTooltipText ───────────────────────────────────────────────────────

    [Fact]
    public void MuteTooltipText_Default_ReturnsMute()
    {
        var vm = new MainViewModel();

        Assert.Equal("Mute", vm.MuteTooltipText);
    }

    [Fact]
    public void MuteTooltipText_WhenMuted_ReturnsUnmute()
    {
        var vm = new MainViewModel();
        vm.IsMuted = true;

        Assert.Equal("Unmute", vm.MuteTooltipText);
    }

    [Fact]
    public void MuteTooltipText_TogglesWithIsMuted()
    {
        var vm = new MainViewModel();

        vm.IsMuted = true;
        Assert.Equal("Unmute", vm.MuteTooltipText);

        vm.IsMuted = false;
        Assert.Equal("Mute", vm.MuteTooltipText);
    }

    [Fact]
    public void IsMuted_SetTrue_RaisesPropertyChangedForMuteTooltipText()
    {
        var vm = new MainViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsMuted = true;

        Assert.Contains(nameof(MainViewModel.MuteTooltipText), raised);
    }

    [Fact]
    public void IsMuted_SetFalse_RaisesPropertyChangedForMuteTooltipText()
    {
        var vm = new MainViewModel();
        vm.IsMuted = true;
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsMuted = false;

        Assert.Contains(nameof(MainViewModel.MuteTooltipText), raised);
    }

    // ── DeafenTooltipText ─────────────────────────────────────────────────────

    [Fact]
    public void DeafenTooltipText_Default_ReturnsDeafen()
    {
        var vm = new MainViewModel();

        Assert.Equal("Deafen", vm.DeafenTooltipText);
    }

    [Fact]
    public void DeafenTooltipText_WhenDeafened_ReturnsUndeafen()
    {
        var vm = new MainViewModel();
        vm.IsDeafened = true;

        Assert.Equal("Undeafen", vm.DeafenTooltipText);
    }

    [Fact]
    public void DeafenTooltipText_TogglesWithIsDeafened()
    {
        var vm = new MainViewModel();

        vm.IsDeafened = true;
        Assert.Equal("Undeafen", vm.DeafenTooltipText);

        vm.IsDeafened = false;
        Assert.Equal("Deafen", vm.DeafenTooltipText);
    }

    [Fact]
    public void IsDeafened_SetTrue_RaisesPropertyChangedForDeafenTooltipText()
    {
        var vm = new MainViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsDeafened = true;

        Assert.Contains(nameof(MainViewModel.DeafenTooltipText), raised);
    }

    [Fact]
    public void IsDeafened_SetFalse_RaisesPropertyChangedForDeafenTooltipText()
    {
        var vm = new MainViewModel();
        vm.IsDeafened = true;
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsDeafened = false;

        Assert.Contains(nameof(MainViewModel.DeafenTooltipText), raised);
    }

    // ── Button text is icon-only ──────────────────────────────────────────────
    // The tooltip carries the accessible label; if text is added back to the
    // button the tooltip becomes redundant and this breaks the design contract.

    [Fact]
    public void MuteButtonText_IsIconOnly_ContainsNoText()
    {
        var vm = new MainViewModel();

        Assert.DoesNotContain("mute", vm.MuteButtonText, StringComparison.OrdinalIgnoreCase);

        vm.IsMuted = true;
        Assert.DoesNotContain("mute", vm.MuteButtonText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeafenButtonText_IsIconOnly_ContainsNoText()
    {
        var vm = new MainViewModel();

        Assert.DoesNotContain("deafen", vm.DeafenButtonText, StringComparison.OrdinalIgnoreCase);

        vm.IsDeafened = true;
        Assert.DoesNotContain("deafen", vm.DeafenButtonText, StringComparison.OrdinalIgnoreCase);
    }
}
