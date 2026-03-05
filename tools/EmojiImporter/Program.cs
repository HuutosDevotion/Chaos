using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

// Paths relative to the repo root
string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string emojiDir = Path.Combine(repoRoot, "src", "Chaos.Server", "wwwroot", "emojis");
string pngDir = Path.Combine(emojiDir, "72x72");
string metadataPath = Path.Combine(emojiDir, "twemoji-metadata.json");

Directory.CreateDirectory(pngDir);

using var http = new HttpClient();
http.Timeout = TimeSpan.FromMinutes(5);

// 1. Download emojibase data
Console.WriteLine("Downloading emojibase data...");
string emojibaseUrl = "https://cdn.jsdelivr.net/npm/emojibase-data@15/en/data.json";
string emojibaseJson = await http.GetStringAsync(emojibaseUrl);
var emojibaseEntries = JsonSerializer.Deserialize<List<EmojibaseEntry>>(emojibaseJson) ?? [];
Console.WriteLine($"  Got {emojibaseEntries.Count} emojibase entries");

// 2. Download twemoji archive
string twemojiZipPath = Path.Combine(Path.GetTempPath(), "twemoji-assets.zip");
if (!File.Exists(twemojiZipPath))
{
    Console.WriteLine("Downloading twemoji assets...");
    string twemojiUrl = "https://github.com/jdecked/twemoji/archive/refs/heads/main.zip";
    var zipBytes = await http.GetByteArrayAsync(twemojiUrl);
    await File.WriteAllBytesAsync(twemojiZipPath, zipBytes);
    Console.WriteLine($"  Downloaded {zipBytes.Length / 1024 / 1024}MB");
}
else
{
    Console.WriteLine("Using cached twemoji archive...");
}

// 3. Extract 72x72 PNGs from the archive
Console.WriteLine("Extracting 72x72 PNGs...");
var availablePngs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
using (var zip = ZipFile.OpenRead(twemojiZipPath))
{
    string prefix = "twemoji-main/assets/72x72/";
    foreach (var entry in zip.Entries)
    {
        if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
        if (!entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;

        string destPath = Path.Combine(pngDir, entry.Name.ToLower());
        if (!File.Exists(destPath))
        {
            entry.ExtractToFile(destPath, overwrite: true);
        }
        availablePngs.Add(entry.Name.ToLower());
    }
}
Console.WriteLine($"  Extracted {availablePngs.Count} PNGs");

// 4. Cross-reference: build metadata for emojis that have matching PNGs
Console.WriteLine("Building metadata...");
var metadata = new List<EmojiMetadata>();

foreach (var entry in emojibaseEntries)
{
    if (string.IsNullOrEmpty(entry.Hexcode)) continue;

    // emojibase hexcode uses dashes for multi-codepoint sequences
    string fileName = entry.Hexcode.ToLower().Replace("-", "-") + ".png";
    if (!availablePngs.Contains(fileName))
    {
        // Try without variant selector (FE0F)
        fileName = entry.Hexcode.ToLower().Replace("-fe0f", "").Replace("-FE0F", "") + ".png";
        if (!availablePngs.Contains(fileName)) continue;
    }

    // Get the shortcode name (first shortcode, or derive from label)
    string name;
    if (entry.Shortcodes is { Count: > 0 })
    {
        name = entry.Shortcodes[0];
    }
    else
    {
        // Derive from label: "grinning face" -> "grinning_face"
        name = entry.Label?.ToLower().Replace(" ", "_").Replace("-", "_") ?? entry.Hexcode.ToLower();
    }

    // Get category from group
    string category = entry.Group switch
    {
        0 => "Smileys & Emotion",
        1 => "People & Body",
        2 => "Animals & Nature",
        3 => "Food & Drink",
        4 => "Travel & Places",
        5 => "Activities",
        6 => "Objects",
        7 => "Symbols",
        8 => "Flags",
        _ => "Other"
    };

    var tags = new List<string>();
    if (entry.Tags is { Count: > 0 }) tags.AddRange(entry.Tags);

    metadata.Add(new EmojiMetadata
    {
        Name = name,
        Category = category,
        FileName = fileName,
        Tags = tags
    });
}

Console.WriteLine($"  Matched {metadata.Count} emojis with PNGs");

// 5. Write metadata JSON
var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
string json = JsonSerializer.Serialize(metadata, jsonOptions);
await File.WriteAllTextAsync(metadataPath, json);
Console.WriteLine($"  Wrote metadata to {metadataPath}");

// 6. Clean up PNGs that aren't referenced
var referencedPngs = new HashSet<string>(metadata.Select(m => m.FileName), StringComparer.OrdinalIgnoreCase);
int removed = 0;
foreach (var file in Directory.GetFiles(pngDir, "*.png"))
{
    if (!referencedPngs.Contains(Path.GetFileName(file)))
    {
        File.Delete(file);
        removed++;
    }
}
Console.WriteLine($"  Cleaned up {removed} unreferenced PNGs");
Console.WriteLine("Done!");

// Models for emojibase JSON deserialization
class EmojibaseEntry
{
    [JsonPropertyName("hexcode")]
    public string? Hexcode { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("shortcodes")]
    public List<string>? Shortcodes { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("group")]
    public int Group { get; set; }

    [JsonPropertyName("type")]
    public int Type { get; set; }
}

class EmojiMetadata
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
}
