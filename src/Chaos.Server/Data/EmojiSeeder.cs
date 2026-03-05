using System.Text.Json;
using Chaos.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Chaos.Server.Data;

public static class EmojiSeeder
{
    private class EmojiMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
    }

    public static async Task SeedAsync(ChaosDbContext db, string contentRootPath)
    {
        if (await db.Emojis.AnyAsync()) return;

        var metadataPath = Path.Combine(contentRootPath, "wwwroot", "emojis", "twemoji-metadata.json");
        if (!File.Exists(metadataPath)) return;

        var json = await File.ReadAllTextAsync(metadataPath);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var entries = JsonSerializer.Deserialize<List<EmojiMetadata>>(json, options);
        if (entries is null) return;

        var emojis = entries.Select(e => new Emoji
        {
            Name = e.Name,
            Category = e.Category,
            FileName = e.FileName
        }).ToList();

        db.Emojis.AddRange(emojis);
        await db.SaveChangesAsync();
    }
}
