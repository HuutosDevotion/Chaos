using Chaos.Shared;
using Xunit;
using Chaos.Tests.Fixtures;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chaos.Tests;

[Collection("ChaosServer")]
public class MentionIndicatorTests
{
    private readonly ChaosServerFixture _fixture;

    public MentionIndicatorTests(ChaosServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SendMessage_BroadcastsNewMessageIndicatorToAllClients()
    {
        var clientA = _fixture.CreateHubConnection();
        var clientB = _fixture.CreateHubConnection();

        try
        {
            var tcs = new TaskCompletionSource<NewMessageIndicatorDto>(TaskCreationOptions.RunContinuationsAsynchronously);

            var senderName = $"Sender_{Guid.NewGuid():N}";

            clientB.On<NewMessageIndicatorDto>("NewMessageIndicator", dto =>
            {
                if (dto.Author == senderName) tcs.TrySetResult(dto);
            });

            await clientA.StartAsync();
            await clientB.StartAsync();

            await clientA.InvokeAsync("SetUsername", senderName);
            await clientB.InvokeAsync("SetUsername", $"Receiver_{Guid.NewGuid():N}");

            // B is in a different channel — should still receive the indicator
            await clientA.InvokeAsync("JoinTextChannel", 1);
            await clientB.InvokeAsync("JoinTextChannel", 2);

            var content = $"Hello_{Guid.NewGuid():N}";
            await clientA.InvokeAsync("SendMessage", 1, content, null);

            var indicator = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, indicator.ChannelId);
            Assert.Equal(senderName, indicator.Author);
            Assert.Contains(content[..20], indicator.ContentPreview);
        }
        finally
        {
            await clientA.StopAsync();
            await clientB.StopAsync();
        }
    }

    [Fact]
    public async Task SendMessage_WithMention_IndicatorContainsMentionedUser()
    {
        var clientA = _fixture.CreateHubConnection();
        var clientB = _fixture.CreateHubConnection();

        try
        {
            var senderName = $"Sender_{Guid.NewGuid():N}";
            var receiverName = $"Receiver_{Guid.NewGuid():N}";

            var tcs = new TaskCompletionSource<NewMessageIndicatorDto>(TaskCreationOptions.RunContinuationsAsynchronously);

            clientB.On<NewMessageIndicatorDto>("NewMessageIndicator", dto =>
            {
                if (dto.Author == senderName) tcs.TrySetResult(dto);
            });

            await clientA.StartAsync();
            await clientB.StartAsync();

            await clientA.InvokeAsync("SetUsername", senderName);
            await clientB.InvokeAsync("SetUsername", receiverName);

            await clientA.InvokeAsync("JoinTextChannel", 1);
            await clientB.InvokeAsync("JoinTextChannel", 2);

            var content = $"Hey @{receiverName} check this out!";
            await clientA.InvokeAsync("SendMessage", 1, content, null);

            var indicator = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Contains(receiverName, indicator.MentionedUsers);
        }
        finally
        {
            await clientA.StopAsync();
            await clientB.StopAsync();
        }
    }

    [Fact]
    public async Task SendMessage_NoMention_IndicatorHasEmptyMentionedUsers()
    {
        var clientA = _fixture.CreateHubConnection();
        var clientB = _fixture.CreateHubConnection();

        try
        {
            var senderName = $"Sender_{Guid.NewGuid():N}";

            var tcs = new TaskCompletionSource<NewMessageIndicatorDto>(TaskCreationOptions.RunContinuationsAsynchronously);

            clientB.On<NewMessageIndicatorDto>("NewMessageIndicator", dto =>
            {
                if (dto.Author == senderName) tcs.TrySetResult(dto);
            });

            await clientA.StartAsync();
            await clientB.StartAsync();

            await clientA.InvokeAsync("SetUsername", senderName);
            await clientB.InvokeAsync("SetUsername", $"Other_{Guid.NewGuid():N}");

            await clientA.InvokeAsync("JoinTextChannel", 1);

            var content = $"Just a normal message {Guid.NewGuid():N}";
            await clientA.InvokeAsync("SendMessage", 1, content, null);

            var indicator = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Empty(indicator.MentionedUsers);
        }
        finally
        {
            await clientA.StopAsync();
            await clientB.StopAsync();
        }
    }

    [Fact]
    public async Task SendMessage_ContentPreview_TruncatesLongMessages()
    {
        var clientA = _fixture.CreateHubConnection();

        try
        {
            var senderName = $"Sender_{Guid.NewGuid():N}";

            var tcs = new TaskCompletionSource<NewMessageIndicatorDto>(TaskCreationOptions.RunContinuationsAsynchronously);

            clientA.On<NewMessageIndicatorDto>("NewMessageIndicator", dto =>
            {
                if (dto.Author == senderName) tcs.TrySetResult(dto);
            });

            await clientA.StartAsync();
            await clientA.InvokeAsync("SetUsername", senderName);
            await clientA.InvokeAsync("JoinTextChannel", 1);

            // Send a message longer than 100 chars
            var content = new string('A', 150);
            await clientA.InvokeAsync("SendMessage", 1, content, null);

            var indicator = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(103, indicator.ContentPreview.Length); // 100 chars + "..."
            Assert.EndsWith("...", indicator.ContentPreview);
        }
        finally
        {
            await clientA.StopAsync();
        }
    }

    [Fact]
    public async Task SendMessage_IndicatorIncludesChannelName()
    {
        var clientA = _fixture.CreateHubConnection();

        try
        {
            var senderName = $"Sender_{Guid.NewGuid():N}";

            var tcs = new TaskCompletionSource<NewMessageIndicatorDto>(TaskCreationOptions.RunContinuationsAsynchronously);

            clientA.On<NewMessageIndicatorDto>("NewMessageIndicator", dto =>
            {
                if (dto.Author == senderName) tcs.TrySetResult(dto);
            });

            await clientA.StartAsync();
            await clientA.InvokeAsync("SetUsername", senderName);
            await clientA.InvokeAsync("JoinTextChannel", 1); // channel 1 is "general" seed data

            await clientA.InvokeAsync("SendMessage", 1, "test", null);

            var indicator = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(string.IsNullOrEmpty(indicator.ChannelName));
        }
        finally
        {
            await clientA.StopAsync();
        }
    }

    [Fact]
    public async Task SendMessage_MultipleMentions_AllDetected()
    {
        var clientA = _fixture.CreateHubConnection();
        var clientB = _fixture.CreateHubConnection();
        var clientC = _fixture.CreateHubConnection();

        try
        {
            var senderName = $"Sender_{Guid.NewGuid():N}";
            var user1 = $"User1_{Guid.NewGuid():N}";
            var user2 = $"User2_{Guid.NewGuid():N}";

            var tcs = new TaskCompletionSource<NewMessageIndicatorDto>(TaskCreationOptions.RunContinuationsAsynchronously);

            clientA.On<NewMessageIndicatorDto>("NewMessageIndicator", dto =>
            {
                if (dto.Author == senderName) tcs.TrySetResult(dto);
            });

            await clientA.StartAsync();
            await clientB.StartAsync();
            await clientC.StartAsync();

            await clientA.InvokeAsync("SetUsername", senderName);
            await clientB.InvokeAsync("SetUsername", user1);
            await clientC.InvokeAsync("SetUsername", user2);

            await clientA.InvokeAsync("JoinTextChannel", 1);

            var content = $"Hey @{user1} and @{user2} look at this!";
            await clientA.InvokeAsync("SendMessage", 1, content, null);

            var indicator = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Contains(user1, indicator.MentionedUsers);
            Assert.Contains(user2, indicator.MentionedUsers);
            Assert.Equal(2, indicator.MentionedUsers.Count);
        }
        finally
        {
            await clientA.StopAsync();
            await clientB.StopAsync();
            await clientC.StopAsync();
        }
    }
}
