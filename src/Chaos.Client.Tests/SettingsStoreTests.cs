using System.IO;
using Chaos.Client.Services;
using Xunit;

namespace Chaos.Client.Tests;

/// <summary>
/// Unit tests for LocalJsonKeyValueStore — the client-side settings persistence layer.
/// Each test gets an isolated temp file so tests never touch real user settings.
/// </summary>
public class LocalJsonKeyValueStoreTests : IDisposable
{
    private readonly string _path;
    private readonly LocalJsonKeyValueStore _store;

    public LocalJsonKeyValueStoreTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"chaos-test-{Guid.NewGuid()}.json");
        _store = new LocalJsonKeyValueStore(_path);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    // ── missing key returns default ────────────────────────────────────────────

    [Fact]
    public void Get_MissingKey_ReturnsDoubleDefault()
    {
        Assert.Equal(14.0, _store.Get("FontSize", 14.0));
    }

    [Fact]
    public void Get_MissingKey_ReturnsStringDefault()
    {
        Assert.Equal("Default", _store.Get("InputDevice", "Default"));
    }

    [Fact]
    public void Get_MissingKey_ReturnsBoolDefault()
    {
        Assert.False(_store.Get("GroupMessages", false));
    }

    [Fact]
    public void Get_MissingKey_ReturnsFloatDefault()
    {
        Assert.Equal(1.0f, _store.Get("InputVolume", 1.0f));
    }

    // ── round-trip in memory ───────────────────────────────────────────────────

    [Fact]
    public void Get_AfterSetDouble_ReturnsStoredValue()
    {
        _store.Set("FontSize", 20.0);
        Assert.Equal(20.0, _store.Get("FontSize", 14.0));
    }

    [Fact]
    public void Get_AfterSetString_ReturnsStoredValue()
    {
        _store.Set("InputDevice", "Headphones");
        Assert.Equal("Headphones", _store.Get("InputDevice", "Default"));
    }

    [Fact]
    public void Get_AfterSetBool_ReturnsStoredValue()
    {
        _store.Set("GroupMessages", true);
        Assert.True(_store.Get("GroupMessages", false));
    }

    [Fact]
    public void Get_AfterSetFloat_ReturnsStoredValue()
    {
        _store.Set("OutputVolume", 0.75f);
        Assert.Equal(0.75f, _store.Get("OutputVolume", 1.0f));
    }

    [Fact]
    public void Set_OverwritesPreviousValue()
    {
        _store.Set("FontSize", 16.0);
        _store.Set("FontSize", 22.0);
        Assert.Equal(22.0, _store.Get("FontSize", 14.0));
    }

    [Fact]
    public void MultipleKeys_AreStoredIndependently()
    {
        _store.Set("FontSize", 18.0);
        _store.Set("UiScale", 1.5);
        Assert.Equal(18.0, _store.Get("FontSize", 14.0));
        Assert.Equal(1.5, _store.Get("UiScale", 1.0));
    }

    // ── disk persistence ───────────────────────────────────────────────────────

    [Fact]
    public void Set_PersistsToDisk_NewInstanceReadsSameDouble()
    {
        _store.Set("FontSize", 18.0);

        var store2 = new LocalJsonKeyValueStore(_path);

        Assert.Equal(18.0, store2.Get("FontSize", 14.0));
    }

    [Fact]
    public void Set_PersistsToDisk_NewInstanceReadsSameString()
    {
        _store.Set("InputDevice", "USB Mic");

        var store2 = new LocalJsonKeyValueStore(_path);

        Assert.Equal("USB Mic", store2.Get("InputDevice", "Default"));
    }

    [Fact]
    public void Set_PersistsToDisk_NewInstanceReadsSameBool()
    {
        _store.Set("GroupMessages", true);

        var store2 = new LocalJsonKeyValueStore(_path);

        Assert.True(store2.Get("GroupMessages", false));
    }

    [Fact]
    public void Set_MultipleTimes_LastValuePersistedForEachKey()
    {
        _store.Set("FontSize", 16.0);
        _store.Set("UiScale", 1.25);
        _store.Set("FontSize", 24.0); // overwrite

        var store2 = new LocalJsonKeyValueStore(_path);

        Assert.Equal(24.0, store2.Get("FontSize", 14.0));
        Assert.Equal(1.25, store2.Get("UiScale", 1.0));
    }

    // ── error handling ─────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_MissingFile_DoesNotThrow_ReturnsDefaults()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"chaos-nonexistent-{Guid.NewGuid()}.json");
        var store = new LocalJsonKeyValueStore(missingPath);

        Assert.Equal(14.0, store.Get("FontSize", 14.0));
    }

    [Fact]
    public void Constructor_CorruptedFile_DoesNotThrow_ReturnsDefaults()
    {
        var corruptPath = Path.Combine(Path.GetTempPath(), $"chaos-corrupt-{Guid.NewGuid()}.json");
        File.WriteAllText(corruptPath, "{ this is not valid json !!!!");
        try
        {
            var store = new LocalJsonKeyValueStore(corruptPath);
            Assert.Equal(14.0, store.Get("FontSize", 14.0));
        }
        finally
        {
            File.Delete(corruptPath);
        }
    }
}
