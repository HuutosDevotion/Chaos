using System.Reflection;
using System.Text.Json;
using Chaos.Client.Services;
using Chaos.Shared;
using Xunit;

namespace Chaos.Client.Tests;

public class EmojiServiceTests : IDisposable
{
    private readonly EmojiService _service;
    private readonly InMemoryKeyValueStore _store;

    public EmojiServiceTests()
    {
        _store = new InMemoryKeyValueStore();
        _service = new EmojiService();
        _service.Initialize("testuser", _store);
    }

    public void Dispose() { }

    // ── Search ───────────────────────────────────────────────────────────────

    [Fact]
    public void Search_KnownQuery_ReturnsMatches()
    {
        var results = _service.Search("grinning");
        Assert.NotEmpty(results);
        Assert.All(results, e => Assert.Contains("grinning", e.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Search_CaseInsensitive_ReturnsMatches()
    {
        var lower = _service.Search("grinning");
        var upper = _service.Search("GRINNING");
        Assert.Equal(lower.Count, upper.Count);
    }

    [Fact]
    public void Search_RespectsMaxResults()
    {
        var results = _service.Search("face", maxResults: 3);
        Assert.True(results.Count <= 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Search_EmptyOrWhitespace_ReturnsEmpty(string? query)
    {
        var results = _service.Search(query!);
        Assert.Empty(results);
    }

    [Fact]
    public void Search_NoMatches_ReturnsEmpty()
    {
        var results = _service.Search("zzzzzznotanemoji");
        Assert.Empty(results);
    }

    // ── Resolve ──────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_KnownShortcode_ReturnsEmoji()
    {
        var emoji = _service.Resolve("star");
        Assert.NotNull(emoji);
        Assert.Equal("star", emoji.Name);
    }

    [Fact]
    public void Resolve_WithAndWithoutColons_BothWork()
    {
        var withColons = _service.Resolve(":star:");
        var withoutColons = _service.Resolve("star");
        Assert.NotNull(withColons);
        Assert.NotNull(withoutColons);
        Assert.Equal(withColons.Name, withoutColons.Name);
    }

    [Fact]
    public void Resolve_UnknownShortcode_ReturnsNull()
    {
        Assert.Null(_service.Resolve("zzzznotreal"));
    }

    [Fact]
    public void Resolve_CaseInsensitive()
    {
        var lower = _service.Resolve("star");
        var upper = _service.Resolve("STAR");
        Assert.NotNull(lower);
        Assert.NotNull(upper);
        Assert.Equal(lower.Name, upper.Name);
    }

    // ── BuildNameLookup ─────────────────────────────────────────────────────

    [Fact]
    public void BuildNameLookup_DuplicateNames_GetTildeSuffix()
    {
        var service = new EmojiService();

        var allField = typeof(EmojiService).GetField("_allEmojis",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        allField.SetValue(service, new List<EmojiDto>
        {
            new() { Name = "star", FileName = "star1.png", Category = "Symbols" },
            new() { Name = "star", FileName = "star2.png", Category = "Symbols" },
            new() { Name = "star", FileName = "star3.png", Category = "Symbols" },
        });

        var method = typeof(EmojiService).GetMethod("BuildNameLookup",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(service, null);

        Assert.NotNull(service.Resolve("star"));
        Assert.NotNull(service.Resolve("star~2"));
        Assert.NotNull(service.Resolve("star~3"));
    }

    // ── GetByCategory ────────────────────────────────────────────────────────

    [Fact]
    public void GetByCategory_KnownCategory_ReturnsOnlyThatCategory()
    {
        var results = _service.GetByCategory("Smileys & Emotion");
        Assert.NotEmpty(results);
        Assert.All(results, e => Assert.Equal("Smileys & Emotion", e.Category));
    }

    [Fact]
    public void GetByCategory_UnknownCategory_ReturnsEmpty()
    {
        Assert.Empty(_service.GetByCategory("Not A Real Category"));
    }

    // ── GetCategories ────────────────────────────────────────────────────────

    [Fact]
    public void GetCategories_Returns9Categories()
    {
        var categories = _service.GetCategories();
        Assert.Equal(9, categories.Length);
        Assert.Contains("Smileys & Emotion", categories);
        Assert.Contains("Flags", categories);
    }

    // ── GetGroupedByCategory ─────────────────────────────────────────────────

    [Fact]
    public void GetGroupedByCategory_FollowsCategoryOrder()
    {
        var grouped = _service.GetGroupedByCategory();
        var expectedOrder = _service.GetCategories();

        int prevIndex = -1;
        foreach (var (category, _) in grouped)
        {
            int index = Array.IndexOf(expectedOrder, category);
            Assert.True(index > prevIndex, $"Category '{category}' is out of order");
            prevIndex = index;
        }
    }

    [Fact]
    public void GetGroupedByCategory_EachGroupContainsCorrectEmojis()
    {
        var grouped = _service.GetGroupedByCategory();
        foreach (var (category, emojis) in grouped)
        {
            Assert.NotEmpty(emojis);
            Assert.All(emojis, e => Assert.Equal(category, e.Category));
        }
    }

    // ── TrackUsage ───────────────────────────────────────────────────────────

    [Fact]
    public void TrackUsage_IncrementsCount()
    {
        _service.TrackUsage("star");
        _service.TrackUsage("star");
        _service.TrackUsage("star");

        var freq = _service.GetFrequentlyUsed();
        Assert.Contains(freq, e => e.Name == "star");
    }

    [Fact]
    public void TrackUsage_NullStore_NoOp()
    {
        var service = new EmojiService();
        // Not initialized — _store is null
        service.TrackUsage("smile"); // Should not throw
    }

    // ── GetFrequentlyUsed ────────────────────────────────────────────────────

    [Fact]
    public void GetFrequentlyUsed_ReturnsOrderedByCount()
    {
        var results = _service.Search("grinning", 2);
        Assert.True(results.Count >= 2);
        string more = results[0].Name;
        string less = results[1].Name;

        _service.TrackUsage(more);
        _service.TrackUsage(more);
        _service.TrackUsage(more);
        _service.TrackUsage(less);

        var freq = _service.GetFrequentlyUsed();
        int moreIdx = freq.FindIndex(e => e.Name == more);
        int lessIdx = freq.FindIndex(e => e.Name == less);
        Assert.True(moreIdx >= 0 && lessIdx >= 0);
        Assert.True(moreIdx < lessIdx);
    }

    [Fact]
    public void GetFrequentlyUsed_RespectsCountLimit()
    {
        var results = _service.Search("face", 3);
        Assert.True(results.Count >= 3);
        foreach (var e in results.Take(3)) _service.TrackUsage(e.Name);

        var freq = _service.GetFrequentlyUsed(count: 2);
        Assert.True(freq.Count <= 2);
    }

    [Fact]
    public void GetFrequentlyUsed_EmptyWhenNoUsage()
    {
        Assert.Empty(_service.GetFrequentlyUsed());
    }

    // ── IsLoaded ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsLoaded_FalseBeforeInit_TrueAfter()
    {
        var service = new EmojiService();
        Assert.False(service.IsLoaded);

        service.Initialize("testuser", _store);
        Assert.True(service.IsLoaded);
    }
}

/// <summary>
/// In-memory implementation of IKeyValueStore for testing.
/// Serializes/deserializes via JsonSerializer to match LocalJsonKeyValueStore behavior.
/// </summary>
internal class InMemoryKeyValueStore : IKeyValueStore
{
    private readonly Dictionary<string, JsonElement> _data = new();

    public T Get<T>(string key, T defaultValue)
    {
        if (!_data.TryGetValue(key, out var element)) return defaultValue;
        try { return element.Deserialize<T>() ?? defaultValue; }
        catch { return defaultValue; }
    }

    public void Set<T>(string key, T value)
    {
        _data[key] = JsonSerializer.SerializeToElement(value);
    }
}
