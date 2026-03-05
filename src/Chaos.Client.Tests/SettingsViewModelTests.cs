using Chaos.Client.ViewModels;
using Xunit;

namespace Chaos.Client.Tests;

/// <summary>
/// Unit tests for the settings modal ViewModel layer:
/// SettingsModalViewModel, AppearanceSettingsViewModel, VoiceSettingsViewModel.
/// </summary>
public class SettingsModalViewModelTests
{
    private static SettingsModalViewModel Make()
    {
        return new SettingsModalViewModel(new AppSettings());
    }

    // ── initial state ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_SelectedPage_IsAppearancePage()
    {
        var modal = Make();
        Assert.IsType<AppearanceSettingsViewModel>(modal.SelectedPage);
    }

    [Fact]
    public void Constructor_AppearancePage_IsMarkedSelected()
    {
        var modal = Make();
        var page = modal.Categories[0].Pages[0];
        Assert.True(page.IsSelected);
    }

    [Fact]
    public void Constructor_VoicePage_IsNotMarkedSelected()
    {
        var modal = Make();
        var page = modal.Categories[0].Pages[1];
        Assert.False(page.IsSelected);
    }

    // ── categories and pages ───────────────────────────────────────────────────

    [Fact]
    public void Categories_HasOneCategory()
    {
        var modal = Make();
        Assert.Single(modal.Categories);
    }

    [Fact]
    public void Category_HasTwoPages()
    {
        var modal = Make();
        Assert.Equal(2, modal.Categories[0].Pages.Count);
    }

    [Fact]
    public void FirstPage_IsAppearanceSettingsViewModel()
    {
        var modal = Make();
        Assert.IsType<AppearanceSettingsViewModel>(modal.Categories[0].Pages[0]);
    }

    [Fact]
    public void SecondPage_IsVoiceSettingsViewModel()
    {
        var modal = Make();
        Assert.IsType<VoiceSettingsViewModel>(modal.Categories[0].Pages[1]);
    }

    [Fact]
    public void AppearancePage_HasCorrectName()
    {
        var modal = Make();
        Assert.Equal("Appearance", modal.Categories[0].Pages[0].Name);
    }

    [Fact]
    public void VoicePage_HasCorrectName()
    {
        var modal = Make();
        Assert.Equal("Voice", modal.Categories[0].Pages[1].Name);
    }

    // ── page selection ─────────────────────────────────────────────────────────

    [Fact]
    public void SelectVoicePage_ChangesSelectedPageToVoice()
    {
        var modal = Make();
        var voicePage = modal.Categories[0].Pages[1];

        voicePage.Select.Execute(null);

        Assert.IsType<VoiceSettingsViewModel>(modal.SelectedPage);
    }

    [Fact]
    public void SelectVoicePage_DeselectedPreviousPage()
    {
        var modal = Make();
        var appearancePage = modal.Categories[0].Pages[0];
        var voicePage = modal.Categories[0].Pages[1];

        voicePage.Select.Execute(null);

        Assert.False(appearancePage.IsSelected);
    }

    [Fact]
    public void SelectVoicePage_MarksVoicePageSelected()
    {
        var modal = Make();
        var voicePage = modal.Categories[0].Pages[1];

        voicePage.Select.Execute(null);

        Assert.True(voicePage.IsSelected);
    }

    [Fact]
    public void SelectPage_RaisesSelectedPagePropertyChanged()
    {
        var modal = Make();
        var raised = new List<string?>();
        modal.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        var voicePage = modal.Categories[0].Pages[1];

        voicePage.Select.Execute(null);

        Assert.Contains(nameof(SettingsModalViewModel.SelectedPage), raised);
    }

    // ── close command ──────────────────────────────────────────────────────────

    [Fact]
    public void CloseCommand_SetsIsOpenFalse()
    {
        var modal = Make();
        modal.Open();

        modal.CloseCommand.Execute(null);

        Assert.False(modal.IsOpen);
    }

    [Fact]
    public void CloseCommand_CanAlwaysExecute()
    {
        var modal = Make();
        Assert.True(modal.CloseCommand.CanExecute(null));
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
