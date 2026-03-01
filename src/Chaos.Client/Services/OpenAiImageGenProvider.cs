using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Chaos.Client.Services;

public class OpenAiImageGenProvider : IImageGenProvider
{
    public string Name => "OpenAI";

    public async Task<ImageGenResult> GenerateAsync(string apiKey, string prompt, List<byte[]> images)
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(120);
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var contentParts = new List<object>
            {
                new { type = "input_text", text = prompt }
            };

            foreach (var img in images)
            {
                var b64 = Convert.ToBase64String(img);
                contentParts.Add(new
                {
                    type = "input_image",
                    image_url = $"data:image/png;base64,{b64}"
                });
            }

            var payload = new
            {
                model = "gpt-4.1",
                input = new[]
                {
                    new { role = "user", content = contentParts.ToArray() }
                },
                tools = new[] { new { type = "image_generation" } }
            };

            var json = JsonSerializer.Serialize(payload);
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            var response = await http.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return new ImageGenResult(null, null, $"API error {(int)response.StatusCode}: {responseBody}");

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            string? text = null;
            byte[]? imageData = null;

            if (root.TryGetProperty("output", out var output))
            {
                foreach (var item in output.EnumerateArray())
                {
                    var type = item.GetProperty("type").GetString();

                    if (type == "message" && item.TryGetProperty("content", out var content))
                    {
                        foreach (var part in content.EnumerateArray())
                        {
                            if (part.GetProperty("type").GetString() == "output_text")
                                text = part.GetProperty("text").GetString();
                        }
                    }
                    else if (type == "image_generation_call" && item.TryGetProperty("result", out var result))
                    {
                        var b64 = result.GetString();
                        if (!string.IsNullOrEmpty(b64))
                            imageData = Convert.FromBase64String(b64);
                    }
                }
            }

            if (text is null && imageData is null)
                return new ImageGenResult(null, null, "No output returned from API");

            return new ImageGenResult(text, imageData, null);
        }
        catch (TaskCanceledException)
        {
            return new ImageGenResult(null, null, "Request timed out (120s)");
        }
        catch (Exception ex)
        {
            return new ImageGenResult(null, null, $"Error: {ex.Message}");
        }
    }
}
