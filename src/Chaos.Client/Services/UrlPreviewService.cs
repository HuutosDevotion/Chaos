using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;


namespace Chaos.Client.Services;

public record UrlPreviewData(string Title, string? Description, string? ImageUrl, string Domain, string Url);

public class UrlPreviewService
{
    // Default HttpClient uses SocketsHttpHandler which auto-decompresses (gzip/br)
    // and follows redirects out of the box
    internal static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    static UrlPreviewService()
    {
        Http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        Http.DefaultRequestHeaders.Add("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        Http.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
    }

    // Task-cached: same URL only fires one HTTP request even if called concurrently
    private static readonly ConcurrentDictionary<string, Task<UrlPreviewData?>> _cache
        = new(StringComparer.OrdinalIgnoreCase);

    public Task<UrlPreviewData?> FetchAsync(string url) =>
        _cache.GetOrAdd(url, static u => FetchInternalAsync(u));

    private static Task<UrlPreviewData?> FetchInternalAsync(string url)
    {
        if (IsRedditPostUrl(url)) return FetchRedditAsync(url);
        return FetchHtmlAsync(url);
    }

    // ── Reddit JSON API ───────────────────────────────────────────────────────

    private static readonly Regex RedditPostPattern =
        new(@"reddit\.com/r/[^/]+/comments/", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool IsRedditPostUrl(string url) => RedditPostPattern.IsMatch(url);

    private static async Task<UrlPreviewData?> FetchRedditAsync(string url)
    {
        try
        {
            // Strip query string and fragment, ensure .json suffix
            var clean = Regex.Replace(url, @"[?#].*$", "").TrimEnd('/');
            // Replace old.reddit.com with www so the API works consistently
            clean = Regex.Replace(clean, @"old\.reddit\.com", "www.reddit.com", RegexOptions.IgnoreCase);
            var jsonUrl = clean + ".json";

            // Reddit's API requires a different Accept header than our browser default
            using var req = new HttpRequestMessage(HttpMethod.Get, jsonUrl);
            req.Headers.Add("Accept", "application/json");
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var post = doc.RootElement[0]
                          .GetProperty("data")
                          .GetProperty("children")[0]
                          .GetProperty("data");

            var title     = post.GetProperty("title").GetString() ?? "";
            var subreddit = post.TryGetProperty("subreddit", out var sub) ? sub.GetString() : null;
            var selftext  = post.TryGetProperty("selftext",  out var st)  ? st.GetString()  : null;

            string? imageUrl = null;

            // Prefer thumbnail — it's always JPEG and sized well for our 72px card.
            // The high-quality preview source uses auto=webp which WPF can't decode.
            if (post.TryGetProperty("thumbnail", out var thumb) &&
                thumb.GetString() is string tn &&
                tn.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                imageUrl = tn;
            }

            // Fall back to preview source, stripping the WebP conversion hint
            if (imageUrl is null &&
                post.TryGetProperty("preview", out var preview) &&
                preview.TryGetProperty("images", out var images) &&
                images.GetArrayLength() > 0 &&
                images[0].TryGetProperty("source", out var source) &&
                source.TryGetProperty("url", out var srcUrl))
            {
                var raw = System.Net.WebUtility.HtmlDecode(srcUrl.GetString() ?? "");
                imageUrl = Regex.Replace(raw, @"[&?]auto=webp", "");
            }

            var desc = string.IsNullOrWhiteSpace(selftext) ? null
                     : selftext.Length > 300 ? selftext[..300] + "…" : selftext;

            var domain = subreddit is not null ? $"r/{subreddit}" : "Reddit";
            return new UrlPreviewData(title, desc, imageUrl, domain, url);
        }
        catch { return null; }
    }

    // ── Generic HTML scraper ─────────────────────────────────────────────────

    private static async Task<UrlPreviewData?> FetchHtmlAsync(string url)
    {
        try
        {
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return null;
            var ct = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (!ct.Contains("html", StringComparison.OrdinalIgnoreCase)) return null;

            // Detect charset from Content-Type; fall back to UTF-8
            var charsetStr = resp.Content.Headers.ContentType?.CharSet ?? "utf-8";
            Encoding encoding;
            try { encoding = Encoding.GetEncoding(charsetStr); }
            catch { encoding = Encoding.UTF8; }

            // Read up to 256 KB — handles sites with large inline scripts before meta tags
            using var stream = await resp.Content.ReadAsStreamAsync();
            var buffer = new byte[262144];
            int read = 0, chunk;
            while (read < buffer.Length &&
                   (chunk = await stream.ReadAsync(buffer, read, buffer.Length - read)) > 0)
                read += chunk;

            var html     = encoding.GetString(buffer, 0, read);
            var finalUrl = resp.RequestMessage?.RequestUri?.AbsoluteUri ?? url;
            return ParseHtml(html, url, finalUrl);
        }
        catch { return null; }
    }

    private static readonly Regex MetaTag = new(
        @"<meta\s([^>]+?)\/?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Separate patterns for double- and single-quoted values so apostrophes in
    // content (e.g. "world's") don't truncate the captured text.
    private static readonly Regex AttrProp = new(
        @"(?:property|name)=""([^""]*)""|(?:property|name)='([^']*)'",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AttrCont = new(
        @"content=""([^""]*)""|content='([^']*)'",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TitleTag = new(
        @"<title[^>]*>([^<]+)</title>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Returns the value of the first successfully captured group.
    private static string? Capture(Match m) =>
        m.Groups[1].Success ? m.Groups[1].Value :
        m.Groups[2].Success ? m.Groups[2].Value : null;

    private static UrlPreviewData? ParseHtml(string html, string originalUrl, string finalUrl)
    {
        string? title = null, desc = null, image = null, siteName = null;

        foreach (Match m in MetaTag.Matches(html))
        {
            var attrs    = m.Groups[1].Value;
            var propName = Capture(AttrProp.Match(attrs))?.ToLowerInvariant();
            var contVal  = Capture(AttrCont.Match(attrs));
            if (propName is null || contVal is null) continue;

            var content = System.Net.WebUtility.HtmlDecode(contVal);
            switch (propName)
            {
                case "og:title":                               title    ??= content; break;
                case "og:description":                         desc     ??= content; break;
                case "og:image":
                case "og:image:secure_url":                    image    ??= content; break;
                case "og:site_name":                           siteName ??= content; break;
                case "twitter:title"        when title is null: title   = content; break;
                case "twitter:description"  when desc  is null: desc    = content; break;
                case "twitter:image"        when image is null: image   = content; break;
                case "description"          when desc  is null: desc    = content; break;
            }
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            var tm = TitleTag.Match(html);
            if (tm.Success) title = System.Net.WebUtility.HtmlDecode(tm.Groups[1].Value.Trim());
        }

        if (string.IsNullOrWhiteSpace(title)) return null;

        // Resolve relative image URLs against the final (post-redirect) URL
        if (!string.IsNullOrEmpty(image) &&
            !image.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            try { image = new Uri(new Uri(finalUrl), image).AbsoluteUri; }
            catch { image = null; }
        }

        var domain = siteName ?? new Uri(originalUrl).Host;
        return new UrlPreviewData(title!, desc, image, domain, originalUrl);
    }
}
