using System.IO;
using Chaos.Client.Services;
using Chaos.Client.ViewModels;
using Xunit;

namespace Chaos.Client.Tests;

/// <summary>
/// Integration tests that verify the full settings round-trip:
/// store → MainViewModel.Settings (load path) and
/// MainViewModel.Settings → store via FlushSettings (save path).
/// </summary>
public class SettingsIntegrationTests : IDisposable
{
    private readonly string _path;

    public SettingsIntegrationTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"chaos-integration-{Guid.NewGuid()}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private LocalJsonKeyValueStore MakeStore() => new(_path);

    // ── load path: store → Settings ───────────────────────────────────────────

    [Fact]
    public void Constructor_LoadsFontSizeFromStore()
    {
        var store = MakeStore();
        store.Set("FontSize", 22.0);

        var vm = new MainViewModel(store);

        Assert.Equal(22.0, vm.Settings.FontSize);
    }

    [Fact]
    public void Constructor_LoadsMessageSpacingFromStore()
    {
        var store = MakeStore();
        store.Set("MessageSpacing", 8.0);

        var vm = new MainViewModel(store);

        Assert.Equal(8.0, vm.Settings.MessageSpacing);
    }

    [Fact]
    public void Constructor_LoadsUiScaleFromStore()
    {
        var store = MakeStore();
        store.Set("UiScale", 1.5);

        var vm = new MainViewModel(store);

        Assert.Equal(1.5, vm.Settings.UiScale);
    }

    [Fact]
    public void Constructor_LoadsGroupMessagesFromStore()
    {
        var store = MakeStore();
        store.Set("GroupMessages", true);

        var vm = new MainViewModel(store);

        Assert.True(vm.Settings.GroupMessages);
    }

    [Fact]
    public void Constructor_LoadsInputDeviceFromStore()
    {
        var store = MakeStore();
        store.Set("InputDevice", "USB Mic");

        var vm = new MainViewModel(store);

        Assert.Equal("USB Mic", vm.Settings.InputDevice);
    }

    [Fact]
    public void Constructor_LoadsOutputDeviceFromStore()
    {
        var store = MakeStore();
        store.Set("OutputDevice", "Speakers");

        var vm = new MainViewModel(store);

        Assert.Equal("Speakers", vm.Settings.OutputDevice);
    }

    [Fact]
    public void Constructor_LoadsInputVolumeFromStore()
    {
        var store = MakeStore();
        store.Set("InputVolume", 0.6f);

        var vm = new MainViewModel(store);

        Assert.Equal(0.6f, vm.Settings.InputVolume);
    }

    [Fact]
    public void Constructor_LoadsOutputVolumeFromStore()
    {
        var store = MakeStore();
        store.Set("OutputVolume", 0.4f);

        var vm = new MainViewModel(store);

        Assert.Equal(0.4f, vm.Settings.OutputVolume);
    }

    // ── load path: missing keys use AppSettings defaults ──────────────────────

    [Fact]
    public void Constructor_MissingFontSize_UsesDefault()
    {
        var vm = new MainViewModel(MakeStore());
        Assert.Equal(14.0, vm.Settings.FontSize);
    }

    [Fact]
    public void Constructor_MissingGroupMessages_DefaultsFalse()
    {
        var vm = new MainViewModel(MakeStore());
        Assert.False(vm.Settings.GroupMessages);
    }

    [Fact]
    public void Constructor_MissingInputDevice_DefaultsToDefault()
    {
        var vm = new MainViewModel(MakeStore());
        Assert.Equal("Default", vm.Settings.InputDevice);
    }

    [Fact]
    public void Constructor_MissingVolumes_DefaultToOne()
    {
        var vm = new MainViewModel(MakeStore());
        Assert.Equal(1.0f, vm.Settings.InputVolume);
        Assert.Equal(1.0f, vm.Settings.OutputVolume);
    }

    // ── save path: FlushSettings → store ──────────────────────────────────────

    [Fact]
    public void FlushSettings_SavesFontSizeToStore()
    {
        var store = MakeStore();
        var vm = new MainViewModel(store);
        vm.Settings.FontSize = 20.0;

        vm.FlushSettings();

        Assert.Equal(20.0, store.Get("FontSize", 14.0));
    }

    [Fact]
    public void FlushSettings_SavesGroupMessagesToStore()
    {
        var store = MakeStore();
        var vm = new MainViewModel(store);
        vm.Settings.GroupMessages = true;

        vm.FlushSettings();

        Assert.True(store.Get("GroupMessages", false));
    }

    [Fact]
    public void FlushSettings_SavesInputDeviceToStore()
    {
        var store = MakeStore();
        var vm = new MainViewModel(store);
        vm.Settings.InputDevice = "Headset";

        vm.FlushSettings();

        Assert.Equal("Headset", store.Get("InputDevice", "Default"));
    }

    [Fact]
    public void FlushSettings_SaveesOutputVolumeToStore()
    {
        var store = MakeStore();
        var vm = new MainViewModel(store);
        vm.Settings.OutputVolume = 0.3f;

        vm.FlushSettings();

        Assert.Equal(0.3f, store.Get("OutputVolume", 1.0f));
    }

    [Fact]
    public void FlushSettings_SavesAllSettingsAtOnce()
    {
        var store = MakeStore();
        var vm = new MainViewModel(store);
        vm.Settings.FontSize       = 18.0;
        vm.Settings.MessageSpacing = 6.0;
        vm.Settings.UiScale        = 1.25;
        vm.Settings.GroupMessages  = true;
        vm.Settings.InputDevice    = "USB Mic";
        vm.Settings.OutputDevice   = "Headphones";
        vm.Settings.InputVolume    = 0.8f;
        vm.Settings.OutputVolume   = 0.5f;

        vm.FlushSettings();

        Assert.Equal(18.0,        store.Get("FontSize",       14.0));
        Assert.Equal(6.0,         store.Get("MessageSpacing", 4.0));
        Assert.Equal(1.25,        store.Get("UiScale",        1.0));
        Assert.True(              store.Get("GroupMessages",  false));
        Assert.Equal("USB Mic",   store.Get("InputDevice",   "Default"));
        Assert.Equal("Headphones",store.Get("OutputDevice",  "Default"));
        Assert.Equal(0.8f,        store.Get("InputVolume",   1.0f));
        Assert.Equal(0.5f,        store.Get("OutputVolume",  1.0f));
    }

    // ── username persistence ──────────────────────────────────────────────────

    [Fact]
    public void Constructor_LoadsUsernameFromStore()
    {
        var store = MakeStore();
        store.Set("Username", "Alice");

        var vm = new MainViewModel(store);

        Assert.Equal("Alice", vm.Username);
    }

    [Fact]
    public void Constructor_MissingUsername_DefaultsEmpty()
    {
        var vm = new MainViewModel(MakeStore());
        Assert.Equal(string.Empty, vm.Username);
    }

    [Fact]
    public void FlushSettings_SavesUsernameToStore()
    {
        var store = MakeStore();
        var vm = new MainViewModel(store);
        vm.Username = "Bob";

        vm.FlushSettings();

        Assert.Equal("Bob", store.Get("Username", string.Empty));
    }

    [Fact]
    public void RoundTrip_Username_PersistedAndReloaded()
    {
        var vm1 = new MainViewModel(MakeStore());
        vm1.Username = "Charlie";
        vm1.FlushSettings();

        var vm2 = new MainViewModel(MakeStore());

        Assert.Equal("Charlie", vm2.Username);
    }

    // ── window bounds persistence ─────────────────────────────────────────────

    [Fact]
    public void UpdateWindowBounds_GetWindowBounds_ReturnsStoredValues()
    {
        var vm = new MainViewModel(MakeStore());
        vm.UpdateWindowBounds(100, 200, 1000, 700, false);

        var (left, top, width, height, maximized) = vm.GetWindowBounds();

        Assert.Equal(100,   left);
        Assert.Equal(200,   top);
        Assert.Equal(1000,  width);
        Assert.Equal(700,   height);
        Assert.False(maximized);
    }

    [Fact]
    public void UpdateWindowBounds_Maximized_GetWindowBoundsReturnsTrue()
    {
        var vm = new MainViewModel(MakeStore());
        vm.UpdateWindowBounds(100, 200, 1000, 700, true);

        var (_, _, _, _, maximized) = vm.GetWindowBounds();

        Assert.True(maximized);
    }

    [Fact]
    public void Constructor_MissingWindowBounds_UseSentinelDefaults()
    {
        var vm = new MainViewModel(MakeStore());
        var (left, top, width, height, maximized) = vm.GetWindowBounds();

        Assert.Equal(-999999.0, left);
        Assert.Equal(-999999.0, top);
        Assert.Equal(0.0,       width);
        Assert.Equal(0.0,       height);
        Assert.False(maximized);
    }

    [Fact]
    public void FlushSettings_SavesWindowBoundsToStore()
    {
        var store = MakeStore();
        var vm = new MainViewModel(store);
        vm.UpdateWindowBounds(50, 75, 800, 600, false);

        vm.FlushSettings();

        Assert.Equal(50.0,  store.Get("WindowLeft",   0.0));
        Assert.Equal(75.0,  store.Get("WindowTop",    0.0));
        Assert.Equal(800.0, store.Get("WindowWidth",  0.0));
        Assert.Equal(600.0, store.Get("WindowHeight", 0.0));
        Assert.False(       store.Get("WindowMaximized", true));
    }

    [Fact]
    public void FlushSettings_SavesWindowMaximizedTrue()
    {
        var store = MakeStore();
        var vm = new MainViewModel(store);
        vm.UpdateWindowBounds(0, 0, 1920, 1080, true);

        vm.FlushSettings();

        Assert.True(store.Get("WindowMaximized", false));
    }

    [Fact]
    public void RoundTrip_WindowBounds_PersistedAndReloaded()
    {
        var vm1 = new MainViewModel(MakeStore());
        vm1.UpdateWindowBounds(120, 80, 1280, 800, false);
        vm1.FlushSettings();

        var vm2 = new MainViewModel(MakeStore());
        var (left, top, width, height, maximized) = vm2.GetWindowBounds();

        Assert.Equal(120.0,  left);
        Assert.Equal(80.0,   top);
        Assert.Equal(1280.0, width);
        Assert.Equal(800.0,  height);
        Assert.False(maximized);
    }

    [Fact]
    public void RoundTrip_WindowMaximized_PersistedAndReloaded()
    {
        var vm1 = new MainViewModel(MakeStore());
        vm1.UpdateWindowBounds(0, 0, 1920, 1080, true);
        vm1.FlushSettings();

        var vm2 = new MainViewModel(MakeStore());
        var (_, _, _, _, maximized) = vm2.GetWindowBounds();

        Assert.True(maximized);
    }

    // ── full round-trip ───────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_FontSize_PersistedAndReloadedByNewInstance()
    {
        var vm1 = new MainViewModel(MakeStore());
        vm1.Settings.FontSize = 20.0;
        vm1.FlushSettings();

        var vm2 = new MainViewModel(MakeStore()); // same file, fresh instance

        Assert.Equal(20.0, vm2.Settings.FontSize);
    }

    [Fact]
    public void RoundTrip_AllSettings_PersistedAndReloaded()
    {
        var vm1 = new MainViewModel(MakeStore());
        vm1.Settings.FontSize       = 18.0;
        vm1.Settings.MessageSpacing = 6.0;
        vm1.Settings.UiScale        = 1.25;
        vm1.Settings.GroupMessages  = true;
        vm1.Settings.InputDevice    = "USB Mic";
        vm1.Settings.OutputDevice   = "Headphones";
        vm1.Settings.InputVolume    = 0.8f;
        vm1.Settings.OutputVolume   = 0.5f;
        vm1.FlushSettings();

        var vm2 = new MainViewModel(MakeStore());

        Assert.Equal(18.0,         vm2.Settings.FontSize);
        Assert.Equal(6.0,          vm2.Settings.MessageSpacing);
        Assert.Equal(1.25,         vm2.Settings.UiScale);
        Assert.True(               vm2.Settings.GroupMessages);
        Assert.Equal("USB Mic",    vm2.Settings.InputDevice);
        Assert.Equal("Headphones", vm2.Settings.OutputDevice);
        Assert.Equal(0.8f,         vm2.Settings.InputVolume);
        Assert.Equal(0.5f,         vm2.Settings.OutputVolume);
    }
}
