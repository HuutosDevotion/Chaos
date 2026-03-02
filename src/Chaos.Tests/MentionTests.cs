using Chaos.Shared;
using Xunit;
using Chaos.Tests.Fixtures;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chaos.Tests;

[Collection("ChaosServer")]
public class MentionTests
{
    private readonly ChaosServerFixture _fixture;

    public MentionTests(ChaosServerFixture fixture)
    {
        _fixture = fixture;
    }

    // ── GetMentions ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMentions_BeforeAnyMessages_ReturnsEmpty()
    {
        var client = _fixture.CreateHubConnection();
        try
        {
            await client.StartAsync();
            await client.InvokeAsync("SetUsername", $"User_{Guid.NewGuid():N}");

            var mentions = await client.InvokeAsync<List<MentionDto>>("GetMentions");

            Assert.Empty(mentions);
        }
        finally
        {
            await client.StopAsync();
        }
    }

    // ── MentionReceived signal ────────────────────────────────────────────────

    [Fact]
    public async Task SendMessage_WithMention_DeliversMentionToMentionedUser()
    {
        var sender = _fixture.CreateHubConnection();
        var recipient = _fixture.CreateHubConnection();
        try
        {
            var recipientName = $"Recipient_{Guid.NewGuid():N}";
            var tcs = new TaskCompletionSource<MentionDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            recipient.On<MentionDto>("MentionReceived", m => tcs.TrySetResult(m));

            await sender.StartAsync();
            await recipient.StartAsync();

            var senderName = $"Sender_{Guid.NewGuid():N}";
            await sender.InvokeAsync("SetUsername", senderName);
            await recipient.InvokeAsync("SetUsername", recipientName);

            await sender.InvokeAsync("JoinTextChannel", 1);
            await recipient.InvokeAsync("JoinTextChannel", 1);

            var content = $"Hey @{recipientName}, check this out!";
            await sender.InvokeAsync("SendMessage", 1, content, null);

            var mention = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, mention.ChannelId);
            Assert.Equal(senderName, mention.Author);
            Assert.Equal(content, mention.Content);
        }
        finally
        {
            await sender.StopAsync();
            await recipient.StopAsync();
        }
    }

    [Fact]
    public async Task SendMessage_WithMention_StoresMentionRetrievableViaGetMentions()
    {
        var sender = _fixture.CreateHubConnection();
        var recipient = _fixture.CreateHubConnection();
        try
        {
            var recipientName = $"Recipient_{Guid.NewGuid():N}";
            var mentionReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            recipient.On<MentionDto>("MentionReceived", _ => mentionReceived.TrySetResult(true));

            await sender.StartAsync();
            await recipient.StartAsync();

            var senderName = $"Sender_{Guid.NewGuid():N}";
            await sender.InvokeAsync("SetUsername", senderName);
            await recipient.InvokeAsync("SetUsername", recipientName);

            await sender.InvokeAsync("JoinTextChannel", 1);
            await recipient.InvokeAsync("JoinTextChannel", 2);

            await sender.InvokeAsync("SendMessage", 1, $"Hey @{recipientName}!", null);
            await mentionReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var mentions = await recipient.InvokeAsync<List<MentionDto>>("GetMentions");

            Assert.Contains(mentions, m => m.Author == senderName && m.ChannelId == 1);
        }
        finally
        {
            await sender.StopAsync();
            await recipient.StopAsync();
        }
    }

    [Fact]
    public async Task SendMessage_WithMention_DoesNotDeliverMentionToSender()
    {
        var sender = _fixture.CreateHubConnection();
        try
        {
            await sender.StartAsync();
            var senderName = $"Sender_{Guid.NewGuid():N}";
            await sender.InvokeAsync("SetUsername", senderName);
            await sender.InvokeAsync("JoinTextChannel", 1);

            var selfMentioned = false;
            sender.On<MentionDto>("MentionReceived", _ => selfMentioned = true);

            await sender.InvokeAsync("SendMessage", 1, $"Hello @{senderName}!", null);

            await Task.Delay(300);
            Assert.False(selfMentioned);
        }
        finally
        {
            await sender.StopAsync();
        }
    }

    [Fact]
    public async Task SendMessage_WithoutMention_DoesNotDeliverMentionToOtherUser()
    {
        var sender = _fixture.CreateHubConnection();
        var other = _fixture.CreateHubConnection();
        try
        {
            await sender.StartAsync();
            await other.StartAsync();

            await sender.InvokeAsync("SetUsername", $"Sender_{Guid.NewGuid():N}");
            await other.InvokeAsync("SetUsername", $"Other_{Guid.NewGuid():N}");

            await sender.InvokeAsync("JoinTextChannel", 1);
            await other.InvokeAsync("JoinTextChannel", 1);

            var mentionReceived = false;
            other.On<MentionDto>("MentionReceived", _ => mentionReceived = true);

            await sender.InvokeAsync("SendMessage", 1, "Just a normal message", null);

            await Task.Delay(300);
            Assert.False(mentionReceived);
        }
        finally
        {
            await sender.StopAsync();
            await other.StopAsync();
        }
    }

    // ── ClearMention ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ClearMention_RemovesSpecificMentionFromGetMentions()
    {
        var sender = _fixture.CreateHubConnection();
        var recipient = _fixture.CreateHubConnection();
        try
        {
            var recipientName = $"Recipient_{Guid.NewGuid():N}";
            var tcs = new TaskCompletionSource<MentionDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            recipient.On<MentionDto>("MentionReceived", m => tcs.TrySetResult(m));

            await sender.StartAsync();
            await recipient.StartAsync();

            await sender.InvokeAsync("SetUsername", $"Sender_{Guid.NewGuid():N}");
            await recipient.InvokeAsync("SetUsername", recipientName);

            await sender.InvokeAsync("JoinTextChannel", 1);
            await recipient.InvokeAsync("JoinTextChannel", 2);

            await sender.InvokeAsync("SendMessage", 1, $"Hey @{recipientName}!", null);
            var mention = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await recipient.InvokeAsync("ClearMention", mention.MessageId);

            var remaining = await recipient.InvokeAsync<List<MentionDto>>("GetMentions");
            Assert.DoesNotContain(remaining, m => m.MessageId == mention.MessageId);
        }
        finally
        {
            await sender.StopAsync();
            await recipient.StopAsync();
        }
    }

    [Fact]
    public async Task ClearMention_LeavesOtherMentionsIntact()
    {
        var sender = _fixture.CreateHubConnection();
        var recipient = _fixture.CreateHubConnection();
        try
        {
            var recipientName = $"Recipient_{Guid.NewGuid():N}";
            var received = new List<MentionDto>();
            var allReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            recipient.On<MentionDto>("MentionReceived", m =>
            {
                lock (received) { received.Add(m); }
                if (received.Count >= 2) allReceived.TrySetResult(true);
            });

            await sender.StartAsync();
            await recipient.StartAsync();

            await sender.InvokeAsync("SetUsername", $"Sender_{Guid.NewGuid():N}");
            await recipient.InvokeAsync("SetUsername", recipientName);

            await sender.InvokeAsync("JoinTextChannel", 1);
            await recipient.InvokeAsync("JoinTextChannel", 2);

            await sender.InvokeAsync("SendMessage", 1, $"First @{recipientName}!", null);
            await sender.InvokeAsync("SendMessage", 1, $"Second @{recipientName}!", null);
            await allReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var toRemove = received[0].MessageId;
            var toKeep = received[1].MessageId;

            await recipient.InvokeAsync("ClearMention", toRemove);

            var remaining = await recipient.InvokeAsync<List<MentionDto>>("GetMentions");
            Assert.DoesNotContain(remaining, m => m.MessageId == toRemove);
            Assert.Contains(remaining, m => m.MessageId == toKeep);
        }
        finally
        {
            await sender.StopAsync();
            await recipient.StopAsync();
        }
    }

    // ── ClearAllMentions ──────────────────────────────────────────────────────

    [Fact]
    public async Task ClearAllMentions_RemovesAllMentionsFromGetMentions()
    {
        var sender = _fixture.CreateHubConnection();
        var recipient = _fixture.CreateHubConnection();
        try
        {
            var recipientName = $"Recipient_{Guid.NewGuid():N}";
            int mentionCount = 0;
            var allReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            recipient.On<MentionDto>("MentionReceived", _ =>
            {
                if (Interlocked.Increment(ref mentionCount) >= 2)
                    allReceived.TrySetResult(true);
            });

            await sender.StartAsync();
            await recipient.StartAsync();

            await sender.InvokeAsync("SetUsername", $"Sender_{Guid.NewGuid():N}");
            await recipient.InvokeAsync("SetUsername", recipientName);

            await sender.InvokeAsync("JoinTextChannel", 1);
            await recipient.InvokeAsync("JoinTextChannel", 2);

            await sender.InvokeAsync("SendMessage", 1, $"First @{recipientName}!", null);
            await sender.InvokeAsync("SendMessage", 1, $"Second @{recipientName}!", null);
            await allReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await recipient.InvokeAsync("ClearAllMentions");

            var remaining = await recipient.InvokeAsync<List<MentionDto>>("GetMentions");
            Assert.Empty(remaining);
        }
        finally
        {
            await sender.StopAsync();
            await recipient.StopAsync();
        }
    }

    // ── UnreadCountChanged signal ─────────────────────────────────────────────

    [Fact]
    public async Task SendMessage_UserInDifferentChannel_ReceivesUnreadCountChanged()
    {
        var sender = _fixture.CreateHubConnection();
        var bystander = _fixture.CreateHubConnection();
        try
        {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            bystander.On<int>("UnreadCountChanged", channelId => tcs.TrySetResult(channelId));

            await sender.StartAsync();
            await bystander.StartAsync();

            await sender.InvokeAsync("SetUsername", $"Sender_{Guid.NewGuid():N}");
            await bystander.InvokeAsync("SetUsername", $"Bystander_{Guid.NewGuid():N}");

            await sender.InvokeAsync("JoinTextChannel", 1);
            await bystander.InvokeAsync("JoinTextChannel", 2); // different channel

            await sender.InvokeAsync("SendMessage", 1, "Hello world", null);

            var channelId = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, channelId);
        }
        finally
        {
            await sender.StopAsync();
            await bystander.StopAsync();
        }
    }

    [Fact]
    public async Task SendMessage_UserInSameChannel_DoesNotReceiveUnreadCountChanged()
    {
        var sender = _fixture.CreateHubConnection();
        var viewer = _fixture.CreateHubConnection();
        try
        {
            await sender.StartAsync();
            await viewer.StartAsync();

            await sender.InvokeAsync("SetUsername", $"Sender_{Guid.NewGuid():N}");
            await viewer.InvokeAsync("SetUsername", $"Viewer_{Guid.NewGuid():N}");

            await sender.InvokeAsync("JoinTextChannel", 1);
            await viewer.InvokeAsync("JoinTextChannel", 1); // same channel

            var gotUnread = false;
            viewer.On<int>("UnreadCountChanged", _ => gotUnread = true);

            await sender.InvokeAsync("SendMessage", 1, "Hello world", null);

            await Task.Delay(300);
            Assert.False(gotUnread);
        }
        finally
        {
            await sender.StopAsync();
            await viewer.StopAsync();
        }
    }

    [Fact]
    public async Task SendMessage_Sender_DoesNotReceiveOwnUnreadCountChanged()
    {
        var sender = _fixture.CreateHubConnection();
        try
        {
            await sender.StartAsync();
            await sender.InvokeAsync("SetUsername", $"Sender_{Guid.NewGuid():N}");
            await sender.InvokeAsync("JoinTextChannel", 1);

            var gotUnread = false;
            sender.On<int>("UnreadCountChanged", _ => gotUnread = true);

            await sender.InvokeAsync("SendMessage", 1, "Hello world", null);

            await Task.Delay(300);
            Assert.False(gotUnread);
        }
        finally
        {
            await sender.StopAsync();
        }
    }
}
