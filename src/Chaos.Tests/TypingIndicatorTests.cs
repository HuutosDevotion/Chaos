using Chaos.Tests.Fixtures;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Chaos.Tests;

[Collection("ChaosServer")]
public class TypingIndicatorTests
{
    private readonly ChaosServerFixture _fixture;

    public TypingIndicatorTests(ChaosServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task StartTyping_OtherClientInSameChannel_ReceivesEventWithCorrectPayload()
    {
        var clientA = _fixture.CreateHubConnection();
        var clientB = _fixture.CreateHubConnection();

        try
        {
            var tcs = new TaskCompletionSource<(int channelId, string username)>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            clientB.On<int, string>("UserTyping", (channelId, username) =>
                tcs.TrySetResult((channelId, username)));

            await clientA.StartAsync();
            await clientB.StartAsync();

            var typistName = $"Typist_{Guid.NewGuid():N}";
            await clientA.InvokeAsync("SetUsername", typistName);
            await clientB.InvokeAsync("SetUsername", $"Watcher_{Guid.NewGuid():N}");

            await clientA.InvokeAsync("JoinTextChannel", 1);
            await clientB.InvokeAsync("JoinTextChannel", 1);

            await clientA.InvokeAsync("StartTyping", 1);

            var (receivedChannelId, receivedUsername) = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, receivedChannelId);
            Assert.Equal(typistName, receivedUsername);
        }
        finally
        {
            await clientA.StopAsync();
            await clientB.StopAsync();
        }
    }

    [Fact]
    public async Task StartTyping_SenderDoesNotReceiveOwnEvent()
    {
        var client = _fixture.CreateHubConnection();

        try
        {
            var received = false;
            client.On<int, string>("UserTyping", (_, _) => received = true);

            await client.StartAsync();
            await client.InvokeAsync("SetUsername", $"Solo_{Guid.NewGuid():N}");
            await client.InvokeAsync("JoinTextChannel", 1);

            await client.InvokeAsync("StartTyping", 1);

            await Task.Delay(500);
            Assert.False(received);
        }
        finally
        {
            await client.StopAsync();
        }
    }

    [Fact]
    public async Task StartTyping_ClientInDifferentChannel_DoesNotReceiveEvent()
    {
        var clientA = _fixture.CreateHubConnection();
        var clientB = _fixture.CreateHubConnection();

        try
        {
            var received = false;
            clientB.On<int, string>("UserTyping", (_, _) => received = true);

            await clientA.StartAsync();
            await clientB.StartAsync();

            await clientA.InvokeAsync("SetUsername", $"Typist_{Guid.NewGuid():N}");
            await clientB.InvokeAsync("SetUsername", $"Other_{Guid.NewGuid():N}");

            await clientA.InvokeAsync("JoinTextChannel", 1);
            await clientB.InvokeAsync("JoinTextChannel", 2); // different channel

            await clientA.InvokeAsync("StartTyping", 1);

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
    public async Task StartTyping_WithoutSettingUsername_NoBroadcast()
    {
        var clientA = _fixture.CreateHubConnection(); // intentionally no SetUsername
        var clientB = _fixture.CreateHubConnection();

        try
        {
            var received = false;
            clientB.On<int, string>("UserTyping", (_, _) => received = true);

            await clientA.StartAsync();
            await clientB.StartAsync();

            await clientB.InvokeAsync("SetUsername", $"Watcher_{Guid.NewGuid():N}");
            await clientB.InvokeAsync("JoinTextChannel", 1);

            await clientA.InvokeAsync("StartTyping", 1);

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
    public async Task StartTyping_MultipleTypists_WatcherReceivesBothEvents()
    {
        var clientA = _fixture.CreateHubConnection();
        var clientB = _fixture.CreateHubConnection();
        var watcher = _fixture.CreateHubConnection();

        try
        {
            var received = new List<string>();
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            watcher.On<int, string>("UserTyping", (_, username) =>
            {
                lock (received)
                {
                    received.Add(username);
                    if (received.Count == 2) tcs.TrySetResult();
                }
            });

            await clientA.StartAsync();
            await clientB.StartAsync();
            await watcher.StartAsync();

            var nameA = $"TypistA_{Guid.NewGuid():N}";
            var nameB = $"TypistB_{Guid.NewGuid():N}";

            await clientA.InvokeAsync("SetUsername", nameA);
            await clientB.InvokeAsync("SetUsername", nameB);
            await watcher.InvokeAsync("SetUsername", $"Watcher_{Guid.NewGuid():N}");

            await clientA.InvokeAsync("JoinTextChannel", 1);
            await clientB.InvokeAsync("JoinTextChannel", 1);
            await watcher.InvokeAsync("JoinTextChannel", 1);

            await clientA.InvokeAsync("StartTyping", 1);
            await clientB.InvokeAsync("StartTyping", 1);

            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Contains(nameA, received);
            Assert.Contains(nameB, received);
        }
        finally
        {
            await clientA.StopAsync();
            await clientB.StopAsync();
            await watcher.StopAsync();
        }
    }
}
