using System.Net;
using Xunit;
using System.Net.Http.Headers;
using System.Text.Json;
using Chaos.Shared;
using Chaos.Tests.Fixtures;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chaos.Tests;

[Collection("ChaosServer")]
public class ImageChatTests
{
    private readonly ChaosServerFixture _fixture;

    // Minimal valid 1x1 PNG (grayscale, confirmed valid)
    private static readonly byte[] MinimalPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA6fptVAAAACklEQVQI12NgAAAAAgAB4iG8MwAAAABJRU5ErkJggg==");

    public ImageChatTests(ChaosServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UploadImage_ValidPng_ReturnsUrlInJson()
    {
        var httpClient = _fixture.CreateClient();
        using var content = new MultipartFormDataContent();
        var imageContent = new ByteArrayContent(MinimalPng);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(imageContent, "file", "test.png");

        var response = await httpClient.PostAsync("/api/upload", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var url = doc.RootElement.GetProperty("url").GetString();
        Assert.NotNull(url);
        Assert.StartsWith("/uploads/", url);
        Assert.EndsWith(".png", url);
    }

    [Fact]
    public async Task UploadImage_NonImageExtension_ReturnsBadRequest()
    {
        var httpClient = _fixture.CreateClient();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0, 1, 2, 3 }), "file", "malicious.txt");

        var response = await httpClient.PostAsync("/api/upload", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".gif")]
    [InlineData(".bmp")]
    [InlineData(".webp")]
    public async Task UploadImage_AllowedExtensions_ReturnOk(string ext)
    {
        var httpClient = _fixture.CreateClient();
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(MinimalPng), "file", $"test{ext}");

        var response = await httpClient.PostAsync("/api/upload", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendMessage_WithImageUrl_ReceivedWithCorrectImageData()
    {
        var clientA = _fixture.CreateHubConnection();
        var clientB = _fixture.CreateHubConnection();

        try
        {
            var tcs = new TaskCompletionSource<MessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);

            clientB.On<MessageDto>("ReceiveMessage", msg => tcs.TrySetResult(msg));

            await clientA.StartAsync();
            await clientB.StartAsync();

            await clientA.InvokeAsync("SetUsername", $"ImgSender_{Guid.NewGuid():N}");
            await clientB.InvokeAsync("SetUsername", $"ImgReceiver_{Guid.NewGuid():N}");

            await clientA.InvokeAsync("JoinTextChannel", 1);
            await clientB.InvokeAsync("JoinTextChannel", 1);

            var imageUrl = $"/uploads/test-{Guid.NewGuid():N}.png";
            await clientA.InvokeAsync("SendMessage", 1, string.Empty, imageUrl);

            var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(imageUrl, received.ImageUrl);
            Assert.True(received.HasImage);
        }
        finally
        {
            await clientA.StopAsync();
            await clientB.StopAsync();
        }
    }

    [Fact]
    public async Task SendMessage_WithImageUrl_PersistedToDatabase()
    {
        var client = _fixture.CreateHubConnection();

        try
        {
            await client.StartAsync();
            await client.InvokeAsync("SetUsername", $"ImgDBUser_{Guid.NewGuid():N}");
            await client.InvokeAsync("JoinTextChannel", 1);

            var imageUrl = $"/uploads/persist-{Guid.NewGuid():N}.png";
            await client.InvokeAsync("SendMessage", 1, string.Empty, imageUrl);

            await Task.Delay(100);

            var messages = await client.InvokeAsync<List<MessageDto>>("GetMessages", 1);
            var found = messages.FirstOrDefault(m => m.ImageUrl == imageUrl);

            Assert.NotNull(found);
            Assert.Equal(string.Empty, found.Content);
            Assert.True(found.HasImage);
        }
        finally
        {
            await client.StopAsync();
        }
    }

    [Fact]
    public void MessageDto_HasImage_TrueWhenImageUrlNotEmpty()
    {
        var dto = new MessageDto { ImageUrl = "/uploads/test.png" };
        Assert.True(dto.HasImage);
    }

    [Fact]
    public void MessageDto_HasImage_FalseWhenImageUrlNull()
    {
        var dto = new MessageDto { ImageUrl = null };
        Assert.False(dto.HasImage);
    }

    [Fact]
    public void MessageDto_HasImage_FalseWhenImageUrlEmpty()
    {
        var dto = new MessageDto { ImageUrl = "" };
        Assert.False(dto.HasImage);
    }
}
