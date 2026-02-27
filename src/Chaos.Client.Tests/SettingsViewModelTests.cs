using Chaos.Client.ViewModels;
using Xunit;

namespace Chaos.Client.Tests;

/// <summary>
/// Unit tests for the settings modal ViewModel layer:
/// SettingsModalViewModel, AppearanceSettingsViewModel, VoiceSettingsViewModel.
/// </summary>
public class SettingsModalViewModelTests
{
    private static (SettingsModalViewModel modal, List<bool> closes) Make()
    {
        var closes = new List<bool>();
        var modal = new SettingsModalViewModel(new AppSettings(), () => closes.Add(true));
        return (modal, closes);
    }

    // ── initial state ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_SelectedPage_IsAppearancePage()
    {
        var (modal, _) = Make();
        Assert.IsType<AppearanceSettingsViewModel>(modal.SelectedPage);
    }

    [Fact]
    public void Constructor_AppearancePage_IsMarkedSelected()
    {
        var (modal, _) = Make();
        var page = modal.Categories[0].Pages[0];
        Assert.True(page.IsSelected);
    }

    [Fact]
    public void Constructor_VoicePage_IsNotMarkedSelected()
    {
        var (modal, _) = Make();
        var page = modal.Categories[0].Pages[1];
        Assert.False(page.IsSelected);
    }

    // ── categories and pages ───────────────────────────────────────────────────

    [Fact]
    public void Categories_HasOneCategory()
    {
        var (modal, _) = Make();
        Assert.Single(modal.Categories);
    }

    [Fact]
    public void Category_HasThreePages()
    {
        var (modal, _) = Make();
        Assert.Equal(3, modal.Categories[0].Pages.Count);
    }

    [Fact]
    public void FirstPage_IsAppearanceSettingsViewModel()
    {
        var (modal, _) = Make();
        Assert.IsType<AppearanceSettingsViewModel>(modal.Categories[0].Pages[0]);
    }

    [Fact]
    public void SecondPage_IsVoiceSettingsViewModel()
    {
        var (modal, _) = Make();
        Assert.IsType<VoiceSettingsViewModel>(modal.Categories[0].Pages[1]);
    }

    [Fact]
    public void AppearancePage_HasCorrectName()
    {
        var (modal, _) = Make();
        Assert.Equal("Appearance", modal.Categories[0].Pages[0].Name);
    }

    [Fact]
    public void VoicePage_HasCorrectName()
    {
        var (modal, _) = Make();
        Assert.Equal("Voice", modal.Categories[0].Pages[1].Name);
    }

    // ── page selection ─────────────────────────────────────────────────────────

    [Fact]
    public void SelectVoicePage_ChangesSelectedPageToVoice()
    {
        var (modal, _) = Make();
        var voicePage = modal.Categories[0].Pages[1];

        voicePage.Select.Execute(null);

        Assert.IsType<VoiceSettingsViewModel>(modal.SelectedPage);
    }

    [Fact]
    public void SelectVoicePage_DeselectedPreviousPage()
    {
        var (modal, _) = Make();
        var appearancePage = modal.Categories[0].Pages[0];
        var voicePage = modal.Categories[0].Pages[1];

        voicePage.Select.Execute(null);

        Assert.False(appearancePage.IsSelected);
    }

    [Fact]
    public void SelectVoicePage_MarksVoicePageSelected()
    {
        var (modal, _) = Make();
        var voicePage = modal.Categories[0].Pages[1];

        voicePage.Select.Execute(null);

        Assert.True(voicePage.IsSelected);
    }

    [Fact]
    public void SelectPage_RaisesSelectedPagePropertyChanged()
    {
        var (modal, _) = Make();
        var raised = new List<string?>();
        modal.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        var voicePage = modal.Categories[0].Pages[1];

        voicePage.Select.Execute(null);

        Assert.Contains(nameof(SettingsModalViewModel.SelectedPage), raised);
    }

    // ── close command ──────────────────────────────────────────────────────────

    [Fact]
    public void Close_InvokesCloseCallback()
    {
        var (modal, closes) = Make();

        modal.Close.Execute(null);

        Assert.Single(closes);
    }

    [Fact]
    public void Close_CanAlwaysExecute()
    {
        var (modal, _) = Make();
        Assert.True(modal.Close.CanExecute(null));
    }
}

public class AppearanceSettingsViewModelTests
{
    [Fact]
    public void Name_IsAppearance()
    {
        var vm = new AppearanceSettingsViewModel(new AppSettings(), _ => { });
        Assert.Equal("Appearance", vm.Name);
    }

    [Fact]
    public void Settings_IsThePassedInAppSettings()
    {
        var settings = new AppSettings();
        var vm = new AppearanceSettingsViewModel(settings, _ => { });
        Assert.Same(settings, vm.Settings);
    }

    [Fact]
    public void IsSelected_DefaultsFalse()
    {
        var vm = new AppearanceSettingsViewModel(new AppSettings(), _ => { });
        Assert.False(vm.IsSelected);
    }
}

public class VoiceSettingsViewModelTests
{
    [Fact]
    public void Name_IsVoice()
    {
        var vm = new VoiceSettingsViewModel(new AppSettings(), _ => { });
        Assert.Equal("Voice", vm.Name);
    }

    [Fact]
    public void IsMicTesting_DefaultsFalse()
    {
        var vm = new VoiceSettingsViewModel(new AppSettings(), _ => { });
        Assert.False(vm.IsMicTesting);
    }

    [Fact]
    public void MicTestButtonText_WhenNotTesting_ShowsTestLabel()
    {
        var vm = new VoiceSettingsViewModel(new AppSettings(), _ => { });
        Assert.Equal("Test Microphone", vm.MicTestButtonText);
    }

    [Fact]
    public void InputDevices_ContainsDefault()
    {
        var vm = new VoiceSettingsViewModel(new AppSettings(), _ => { });
        Assert.Contains("Default", vm.InputDevices);
    }

    [Fact]
    public void OutputDevices_ContainsDefault()
    {
        var vm = new VoiceSettingsViewModel(new AppSettings(), _ => { });
        Assert.Contains("Default", vm.OutputDevices);
    }

    [Fact]
    public void InputDevices_DefaultIsFirst()
    {
        var vm = new VoiceSettingsViewModel(new AppSettings(), _ => { });
        Assert.Equal("Default", vm.InputDevices[0]);
    }

    [Fact]
    public void OutputDevices_DefaultIsFirst()
    {
        var vm = new VoiceSettingsViewModel(new AppSettings(), _ => { });
        Assert.Equal("Default", vm.OutputDevices[0]);
    }

    [Fact]
    public void StopMicTest_WhenNotStarted_DoesNotThrow()
    {
        var vm = new VoiceSettingsViewModel(new AppSettings(), _ => { });
        vm.StopMicTest();
    }

    [Fact]
    public void Settings_IsThePassedInAppSettings()
    {
        var settings = new AppSettings();
        var vm = new VoiceSettingsViewModel(settings, _ => { });
        Assert.Same(settings, vm.Settings);
    }

    [Fact]
    public void IsSelected_DefaultsFalse()
    {
        var vm = new VoiceSettingsViewModel(new AppSettings(), _ => { });
        Assert.False(vm.IsSelected);
    }
}
