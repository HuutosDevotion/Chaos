namespace Chaos.Client.Services;

public record ImageGenResult(string? Text, byte[]? ImageData, string? Error);

public interface IImageGenProvider
{
    string Name { get; }
    Task<ImageGenResult> GenerateAsync(string apiKey, string prompt, List<byte[]> images);
}
