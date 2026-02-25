using Chaos.Shared;
using Xunit;
using Chaos.Tests.Fixtures;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chaos.Tests;

[Collection("ChaosServer")]
public class TextChatTests
{
    private readonly ChaosServerFixture _fixture;

    public TextChatTests(ChaosServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SetUsername_BroadcastsUserConnectedToOtherClients()
    {
        var clientA = _fixture.CreateHubConnection();
        var clientB = _fixture.CreateHubConnection();

        try
        {
            var username = $"TestUser_{Guid.NewGuid():N}";
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            clientB.On<string>("UserConnected", name =>
            {
                if (name == username) tcs.TrySetResult(name);
            });

            await clientB.StartAsync();
            await clientA.StartAsync();

            await clientA.InvokeAsync("SetUsername", username);

            var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(username, received);
        }
        finally
        {
            await clientA.StopAsync();
            await clientB.StopAsync();
        }
    }

    [Fact]
    public async Task SendMessage_ClientInSameChannel_ReceivesMessage()
    {
        var clientA = _fixture.CreateHubConnection();
        var clientB = _fixture.CreateHubConnection();

        try
        {
            var tcs = new TaskCompletionSource<MessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);

            clientB.On<MessageDto>("ReceiveMessage", msg => tcs.TrySetResult(msg));

            await clientA.StartAsync();
            await clientB.StartAsync();

            var senderName = $"Sender_{Guid.NewGuid():N}";
            await clientA.InvokeAsync("SetUsername", senderName);
            await clientB.InvokeAsync("SetUsername", $"Receiver_{Guid.NewGuid():N}");

            await clientA.InvokeAsync("JoinTextChannel", 1);
            await clientB.InvokeAsync("JoinTextChannel", 1);

            var content = $"Hello_{Guid.NewGuid():N}";
            await clientA.InvokeAsync("SendMessage", 1, content, null);

            var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, received.ChannelId);
            Assert.Equal(senderName, received.Author);
            Assert.Equal(content, received.Content);
            Assert.False(received.HasImage);
        }
        finally
        {
            await clientA.StopAsync();
            await clientB.StopAsync();
        }
    }

    [Fact]
    public async Task SendMessage_ClientInDifferentChannel_DoesNotReceiveMessage()
    {
        var clientA = _fixture.CreateHubConnection();
        var clientB = _fixture.CreateHubConnection();

        try
        {
            await clientA.StartAsync();
            await clientB.StartAsync();

            await clientA.InvokeAsync("SetUsername", $"Sender_{Guid.NewGuid():N}");
            await clientB.InvokeAsync("SetUsername", $"Bystander_{Guid.NewGuid():N}");

            await clientA.InvokeAsync("JoinTextChannel", 1);
            await clientB.InvokeAsync("JoinTextChannel", 2);

            var received = false;
            clientB.On<MessageDto>("ReceiveMessage", _ => received = true);

            await clientA.InvokeAsync("SendMessage", 1, "Should not reach B", null);

            await Task.Delay(500);
            Assert.False(received);
        }
        finally
        {
            await clientA.StopAsync();
            await clientB.StopAsync();
        }
    }

    [Fact]
    public async Task SendMessage_PersistedToDatabase()
    {
        var client = _fixture.CreateHubConnection();

        try
        {
            await client.StartAsync();
            await client.InvokeAsync("SetUsername", $"DBTestUser_{Guid.NewGuid():N}");
            await client.InvokeAsync("JoinTextChannel", 1);

            var content = $"PersistTest_{Guid.NewGuid():N}";
            await client.InvokeAsync("SendMessage", 1, content, null);

            await Task.Delay(100);

            var messages = await client.InvokeAsync<List<MessageDto>>("GetMessages", 1);
            Assert.Contains(messages, m => m.Content == content);
        }
        finally
        {
            await client.StopAsync();
        }
    }

    [Fact]
    public async Task GetMessages_ReturnsMessagesInChronologicalOrder()
    {
        var client = _fixture.CreateHubConnection();

        try
        {
            await client.StartAsync();
            await client.InvokeAsync("SetUsername", $"OrderTestUser_{Guid.NewGuid():N}");
            await client.InvokeAsync("JoinTextChannel", 1);

            var tag = Guid.NewGuid().ToString("N");
            var firstContent = $"First_{tag}";
            var secondContent = $"Second_{tag}";

            await client.InvokeAsync("SendMessage", 1, firstContent, null);
            await Task.Delay(50);
            await client.InvokeAsync("SendMessage", 1, secondContent, null);

            var messages = await client.InvokeAsync<List<MessageDto>>("GetMessages", 1);

            var testMessages = messages
                .Where(m => m.Content == firstContent || m.Content == secondContent)
                .OrderBy(m => m.Timestamp)
                .ToList();

            Assert.Equal(2, testMessages.Count);
            Assert.Equal(firstContent, testMessages[0].Content);
            Assert.Equal(secondContent, testMessages[1].Content);
        }
        finally
        {
            await client.StopAsync();
        }
    }
}
