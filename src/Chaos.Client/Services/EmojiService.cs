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
        "Custom", "Smileys & Emotion", "People & Body", "Animals & Nature",
        "Food & Drink", "Travel & Places", "Activities",
        "Objects", "Symbols", "Flags"
    };

    // Fires when emoji metadata is ready (names/categories available)
    public event Action? EmojisLoaded;

    // Fires when all images are preloaded into RAM (grid can be built with all images)
    public event Action? ImagesReady;

    public bool IsLoaded => _allEmojis.Count > 0;
    public bool ImagesPreloaded { get; private set; }

    /// <summary>
    /// Phase 1: called at app start. Loads embedded metadata and preloads images from disk cache.
    /// No network required.
    /// </summary>
    public void Initialize(string username, IKeyValueStore store)
    {
        _username = username;
        _store = store;

        Directory.CreateDirectory(_cacheDir);

        LoadEmbedded();

        if (_allEmojis.Count > 0)
            EmojisLoaded?.Invoke();

        // Preload from disk cache in background (no network calls)
        _ = Task.Run(async () =>
        {
            await PreloadFromDiskAsync();
            ImagesPreloaded = true;
            ImagesReady?.Invoke();
        });
    }

    /// <summary>
    /// Phase 2: called after connecting. Fetches server emojis, merges any new/custom ones,
    /// and downloads missing images.
    /// </summary>
    public async Task SyncWithServerAsync(ChatService chatService)
    {
        _baseUrl = chatService.BaseUrl;

        await Task.Run(async () =>
        {
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
                        await GetOrStartLoadAsync(emoji);
                    }
                }

                if (added > 0)
                    ImagesReady?.Invoke();
            }
            catch
            {
                // Server fetch failed — local set is still usable
            }
        });
    }

    /// <summary>
    /// Preload images from disk cache only (no network). Skips emojis without a cached file.
    /// </summary>
    private async Task PreloadFromDiskAsync()
    {
        var emojis = _allEmojis.ToList();

        const int batchSize = 100;
        for (int i = 0; i < emojis.Count; i += batchSize)
        {
            var batch = emojis.Skip(i).Take(batchSize);
            await Task.WhenAll(batch.Select(e => LoadFromDiskAsync(e)));
        }
    }

    private Task LoadFromDiskAsync(EmojiDto emoji)
    {
        return _loadingTasks.GetOrAdd(emoji.FileName, _ => LoadDiskOnlyAsync(emoji));
    }

    private async Task<BitmapImage?> LoadDiskOnlyAsync(EmojiDto emoji)
    {
        try
        {
            string safePath = emoji.FileName.Replace('/', Path.DirectorySeparatorChar);
            string cachePath = Path.Combine(_cacheDir, safePath);
            if (!File.Exists(cachePath))
            {
                _loadingTasks.TryRemove(emoji.FileName, out _);
                return null;
            }

            byte[] bytes = await File.ReadAllBytesAsync(cachePath);

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
            _loadingTasks.TryRemove(emoji.FileName, out _);
            return null;
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

            var emojis = JsonSerializer.Deserialize<List<EmojiDto>>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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
        string name = shortcode.Trim(':');
        return _byName.GetValueOrDefault(name);
    }

    public List<EmojiDto> Search(string query, int maxResults = 30)
    {
        if (string.IsNullOrWhiteSpace(query)) return new();
        query = query.Trim().ToLower();

        var snapshot = _allEmojis.ToList();
        return snapshot
            .Where(e => e.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(maxResults)
            .ToList();
    }

    public List<EmojiDto> GetByCategory(string category)
    {
        return _allEmojis.ToList().Where(e => e.Category == category).ToList();
    }

    public string[] GetCategories() => CategoryOrder;

    public List<(string Category, List<EmojiDto> Emojis)> GetGroupedByCategory()
    {
        var snapshot = _allEmojis.ToList();
        var grouped = snapshot.GroupBy(e => e.Category)
            .ToDictionary(g => g.Key, g => g.ToList());

        return CategoryOrder
            .Where(c => grouped.ContainsKey(c))
            .Select(c => (c, grouped[c]))
            .ToList();
    }

    public BitmapImage? GetCachedImage(EmojiDto emoji)
    {
        _imageCache.TryGetValue(emoji.FileName, out var cached);
        return cached;
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
            string safePath = emoji.FileName.Replace('/', Path.DirectorySeparatorChar);
            string cachePath = Path.Combine(_cacheDir, safePath);

            if (File.Exists(cachePath))
            {
                bytes = await File.ReadAllBytesAsync(cachePath);
            }
            else
            {
                string url = emoji.FileName.StartsWith("custom/")
                    ? $"{_baseUrl}/emojis/{emoji.FileName}"
                    : $"{_baseUrl}/emojis/72x72/{emoji.FileName}";
                using var resp = await _http.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    _imageCache[emoji.FileName] = null;
                    _loadingTasks.TryRemove(emoji.FileName, out _);
                    return null;
                }
                bytes = await resp.Content.ReadAsByteArrayAsync();

                try
                {
                    var cacheParent = Path.GetDirectoryName(cachePath);
                    if (cacheParent is not null) Directory.CreateDirectory(cacheParent);
                    await File.WriteAllBytesAsync(cachePath, bytes);
                }
                catch { }
            }

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

    public async Task AddEmojiAsync(EmojiDto emoji)
    {
        if (_byName.ContainsKey(emoji.Name)) return;

        _allEmojis.Add(emoji);
        _byName[emoji.Name] = emoji;

        await GetOrStartLoadAsync(emoji);
        ImagesReady?.Invoke();
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
