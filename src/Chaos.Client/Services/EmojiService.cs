using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Chaos.Shared;

namespace Chaos.Client.Services;

public class EmojiService
{
    private List<EmojiDto> _allEmojis = new();
    private Dictionary<string, EmojiDto> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BitmapImage?> _imageCache = new();
    private string _baseUrl = string.Empty;
    private string _username = string.Empty;
    private IKeyValueStore? _store;

    // Ordered category list for picker display
    private static readonly string[] CategoryOrder =
    {
        "Smileys & Emotion", "People & Body", "Animals & Nature",
        "Food & Drink", "Travel & Places", "Activities",
        "Objects", "Symbols", "Flags"
    };

    public bool IsLoaded => _allEmojis.Count > 0;

    public async Task LoadAsync(ChatService chatService, string username, IKeyValueStore store)
    {
        _baseUrl = chatService.BaseUrl;
        _username = username;
        _store = store;

        var emojis = await chatService.GetEmojisAsync();
        _allEmojis = emojis;

        // Build name lookup, handling duplicates with ~N suffix
        _byName = new Dictionary<string, EmojiDto>(StringComparer.OrdinalIgnoreCase);
        var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var emoji in _allEmojis)
        {
            string name = emoji.Name;
            if (_byName.ContainsKey(name))
            {
                if (!nameCounts.ContainsKey(name)) nameCounts[name] = 1;
                nameCounts[name]++;
                name = $"{emoji.Name}~{nameCounts[name]}";
            }
            _byName[name] = emoji;
        }
    }

    public EmojiDto? Resolve(string shortcode)
    {
        // shortcode may or may not have colons
        string name = shortcode.Trim(':');
        return _byName.GetValueOrDefault(name);
    }

    public List<EmojiDto> Search(string query, int maxResults = 30)
    {
        if (string.IsNullOrWhiteSpace(query)) return new();
        query = query.Trim().ToLower();

        return _allEmojis
            .Where(e => e.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(maxResults)
            .ToList();
    }

    public List<EmojiDto> GetByCategory(string category)
    {
        return _allEmojis.Where(e => e.Category == category).ToList();
    }

    public string[] GetCategories() => CategoryOrder;

    public List<(string Category, List<EmojiDto> Emojis)> GetGroupedByCategory()
    {
        var grouped = _allEmojis.GroupBy(e => e.Category)
            .ToDictionary(g => g.Key, g => g.ToList());

        return CategoryOrder
            .Where(c => grouped.ContainsKey(c))
            .Select(c => (c, grouped[c]))
            .ToList();
    }

    public BitmapImage? GetCachedImage(EmojiDto emoji)
    {
        if (_imageCache.TryGetValue(emoji.FileName, out var cached))
            return cached;

        // Start async download
        _ = LoadImageAsync(emoji);
        return null;
    }

    public async Task<BitmapImage?> GetImageAsync(EmojiDto emoji)
    {
        if (_imageCache.TryGetValue(emoji.FileName, out var cached))
            return cached;

        return await LoadImageAsync(emoji);
    }

    private async Task<BitmapImage?> LoadImageAsync(EmojiDto emoji)
    {
        try
        {
            string url = $"{_baseUrl}/emojis/72x72/{emoji.FileName}";
            using var http = new HttpClient();
            var bytes = await http.GetByteArrayAsync(url);

            BitmapImage? bmp = null;
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 72;
                bmp.EndInit();
                bmp.Freeze();
            });

            if (bmp is not null)
                _imageCache[emoji.FileName] = bmp;

            return bmp;
        }
        catch
        {
            _imageCache[emoji.FileName] = null;
            return null;
        }
    }

    // Frequently used tracking
    public void TrackUsage(string shortcode)
    {
        if (_store is null || string.IsNullOrEmpty(_username)) return;
        string key = $"EmojiFreqUsed_{_username}";
        var usage = _store.Get<Dictionary<string, int>>(key, new());
        string name = shortcode.Trim(':');
        usage[name] = usage.GetValueOrDefault(name) + 1;
        _store.Set(key, usage);
    }

    public List<EmojiDto> GetFrequentlyUsed(int count = 45)
    {
        if (_store is null || string.IsNullOrEmpty(_username)) return new();
        string key = $"EmojiFreqUsed_{_username}";
        var usage = _store.Get<Dictionary<string, int>>(key, new());

        return usage
            .OrderByDescending(kv => kv.Value)
            .Take(count)
            .Select(kv => Resolve(kv.Key))
            .Where(e => e is not null)
            .Cast<EmojiDto>()
            .ToList();
    }
}
