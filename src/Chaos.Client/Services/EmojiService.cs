using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Chaos.Shared;

namespace Chaos.Client.Services;

public class EmojiService
{
    private List<EmojiDto> _allEmojis = new();
    private Dictionary<string, EmojiDto> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BitmapImage?> _imageCache = new();
    private readonly ConcurrentDictionary<string, Task<BitmapImage?>> _loadingTasks = new();
    private string _baseUrl = string.Empty;
    private string _username = string.Empty;
    private IKeyValueStore? _store;

    private static readonly HttpClient _http = new();

    private static readonly string _cacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Chaos", "emoji-cache");

    // Ordered category list for picker display
    private static readonly string[] CategoryOrder =
    {
        "Smileys & Emotion", "People & Body", "Animals & Nature",
        "Food & Drink", "Travel & Places", "Activities",
        "Objects", "Symbols", "Flags"
    };

    // Event raised when emoji data changes (for eager grid rebuild)
    public event Action? EmojisLoaded;

    public bool IsLoaded => _allEmojis.Count > 0;

    public async Task LoadAsync(ChatService chatService, string username, IKeyValueStore store)
    {
        _baseUrl = chatService.BaseUrl;
        _username = username;
        _store = store;

        // Ensure disk cache directory exists
        Directory.CreateDirectory(_cacheDir);

        // 1. Load embedded metadata first (instant, no network)
        LoadEmbedded();

        // Notify that base emoji set is ready
        if (_allEmojis.Count > 0)
            EmojisLoaded?.Invoke();

        // 2. Fetch server emojis in background and merge any new/custom ones
        try
        {
            var serverEmojis = await chatService.GetEmojisAsync();
            int added = 0;
            foreach (var emoji in serverEmojis)
            {
                if (!_byName.ContainsKey(emoji.Name))
                {
                    _allEmojis.Add(emoji);
                    _byName[emoji.Name] = emoji;
                    added++;
                }
            }
            if (added > 0)
                EmojisLoaded?.Invoke();
        }
        catch
        {
            // Server fetch failed — embedded set is still usable
        }
    }

    private void LoadEmbedded()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("twemoji-metadata.json", StringComparison.OrdinalIgnoreCase));

            if (resourceName is null) return;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) return;

            var emojis = JsonSerializer.Deserialize<List<EmojiDto>>(stream);
            if (emojis is null || emojis.Count == 0) return;

            _allEmojis = emojis;
            BuildNameLookup();
        }
        catch
        {
            // Embedded resource load failed — will fall back to server fetch
        }
    }

    private void BuildNameLookup()
    {
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

        // Start async load once — deduplicated via _loadingTasks
        _ = GetOrStartLoadAsync(emoji);
        return null;
    }

    public Task<BitmapImage?> GetImageAsync(EmojiDto emoji)
    {
        if (_imageCache.TryGetValue(emoji.FileName, out var cached))
            return Task.FromResult(cached);

        return GetOrStartLoadAsync(emoji);
    }

    private Task<BitmapImage?> GetOrStartLoadAsync(EmojiDto emoji)
    {
        return _loadingTasks.GetOrAdd(emoji.FileName, _ => LoadImageAsync(emoji));
    }

    private async Task<BitmapImage?> LoadImageAsync(EmojiDto emoji)
    {
        try
        {
            byte[] bytes;
            string cachePath = Path.Combine(_cacheDir, emoji.FileName);

            // Try disk cache first
            if (File.Exists(cachePath))
            {
                bytes = await File.ReadAllBytesAsync(cachePath);
            }
            else
            {
                // Download and save to disk cache
                string url = $"{_baseUrl}/emojis/72x72/{emoji.FileName}";
                bytes = await _http.GetByteArrayAsync(url);

                try
                {
                    await File.WriteAllBytesAsync(cachePath, bytes);
                }
                catch
                {
                    // Disk write failed — still use the downloaded bytes
                }
            }

            // Create and freeze on background thread — Freeze() makes it
            // cross-thread safe, so no need to marshal to the UI thread.
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 72;
            bmp.EndInit();
            bmp.Freeze();

            _imageCache[emoji.FileName] = bmp;

            _loadingTasks.TryRemove(emoji.FileName, out _);
            return bmp;
        }
        catch
        {
            _imageCache[emoji.FileName] = null;
            _loadingTasks.TryRemove(emoji.FileName, out _);
            return null;
        }
    }

    /// <summary>
    /// Load images for a collection of Image controls in batches to avoid flooding the UI thread.
    /// </summary>
    public async Task LoadImagesBatchedAsync(List<(EmojiDto Emoji, System.Windows.Controls.Image ImageControl)> items, int batchSize = 50)
    {
        for (int i = 0; i < items.Count; i += batchSize)
        {
            var batch = items.Skip(i).Take(batchSize);
            var tasks = batch.Select(async item =>
            {
                var bmp = await GetImageAsync(item.Emoji);
                if (bmp is not null)
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => item.ImageControl.Source = bmp);
            });
            await Task.WhenAll(tasks);

            // Small delay between batches so the UI thread can breathe
            if (i + batchSize < items.Count)
                await Task.Delay(10);
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
