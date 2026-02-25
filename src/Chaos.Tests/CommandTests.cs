using Chaos.Shared;
using Xunit;
using Chaos.Tests.Fixtures;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chaos.Tests;

[Collection("ChaosServer")]
public class CommandTests
{
    private readonly ChaosServerFixture _fixture;

    public CommandTests(ChaosServerFixture fixture)
    {
        _fixture = fixture;
    }

    // ── /roll ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RollCommand_BroadcastsSystemMessageToWholeChannel()
    {
        var clientA = _fixture.CreateHubConnection();
        var clientB = _fixture.CreateHubConnection();

        try
        {
            var tcsA = new TaskCompletionSource<MessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tcsB = new TaskCompletionSource<MessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);

            clientA.On<MessageDto>("ReceiveMessage", msg =>
            {
                if (msg.Author == "System" && msg.Content.Contains("rolled")) tcsA.TrySetResult(msg);
            });
            clientB.On<MessageDto>("ReceiveMessage", msg =>
            {
                if (msg.Author == "System" && msg.Content.Contains("rolled")) tcsB.TrySetResult(msg);
            });

            await clientA.StartAsync();
            await clientB.StartAsync();

            var username = $"Roller_{Guid.NewGuid():N}";
            await clientA.InvokeAsync("SetUsername", username);
            await clientB.InvokeAsync("SetUsername", $"Watcher_{Guid.NewGuid():N}");

            await clientA.InvokeAsync("JoinTextChannel", 1);
            await clientB.InvokeAsync("JoinTextChannel", 1);

            await clientA.InvokeAsync("SendMessage", 1, "/roll d20", null);

            var msgA = await tcsA.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var msgB = await tcsB.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("System", msgA.Author);
            Assert.Contains(username, msgA.Content);
            Assert.Contains("d20", msgA.Content);
            Assert.Equal(1, msgA.ChannelId);

            // Both clients receive identical content
            Assert.Equal(msgA.Content, msgB.Content);
        }
        finally
        {
            await clientA.StopAsync();
            await clientB.StopAsync();
        }
    }

    [Theory]
    [InlineData("d6", 6)]
    [InlineData("d20", 20)]
    [InlineData("D100", 100)]
    public async Task RollCommand_ResultIsWithinDieRange(string die, int sides)
    {
        var client = _fixture.CreateHubConnection();

        try
        {
            var tcs = new TaskCompletionSource<MessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            client.On<MessageDto>("ReceiveMessage", msg =>
            {
                if (msg.Author == "System" && msg.Content.Contains($"d{sides}")) tcs.TrySetResult(msg);
            });

            await client.StartAsync();
            await client.InvokeAsync("SetUsername", $"RangeTest_{Guid.NewGuid():N}");
            await client.InvokeAsync("JoinTextChannel", 1);

            await client.InvokeAsync("SendMessage", 1, $"/roll {die}", null);

            var msg = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Extract number from "rolled a d<sides> and got <result>!"
            var gotIndex = msg.Content.LastIndexOf("got ", StringComparison.Ordinal);
            var resultStr = msg.Content[(gotIndex + 4)..].TrimEnd('!');
            var result = int.Parse(resultStr);

            Assert.InRange(result, 1, sides);
        }
        finally
        {
            await client.StopAsync();
        }
    }

    [Fact]
    public async Task RollCommand_ResultIsPersistedToDatabase()
    {
        var client = _fixture.CreateHubConnection();

        try
        {
            var tcs = new TaskCompletionSource<MessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            client.On<MessageDto>("ReceiveMessage", msg =>
            {
                if (msg.Author == "System" && msg.Content.Contains("rolled")) tcs.TrySetResult(msg);
            });

            await client.StartAsync();
            await client.InvokeAsync("SetUsername", $"PersistRoll_{Guid.NewGuid():N}");
            await client.InvokeAsync("JoinTextChannel", 1);

            await client.InvokeAsync("SendMessage", 1, "/roll d10", null);

            var broadcast = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Should have a real DB-assigned Id (not 0)
            Assert.True(broadcast.Id > 0);

            var history = await client.InvokeAsync<List<MessageDto>>("GetMessages", 1);
            Assert.Contains(history, m => m.Id == broadcast.Id && m.Author == "System");
        }
        finally
        {
            await client.StopAsync();
        }
    }

    // ── ephemeral errors ─────────────────────────────────────────────────────

    [Fact]
    public async Task RollCommand_NoArg_SendsUsageErrorOnlyToCaller()
    {
        var clientA = _fixture.CreateHubConnection();
        var clientB = _fixture.CreateHubConnection();

        try
        {
            var callerReceived = new TaskCompletionSource<MessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            var bystanterReceived = false;

            clientA.On<MessageDto>("ReceiveMessage", msg =>
            {
                if (msg.Author == "System" && msg.Content.Contains("Usage:")) callerReceived.TrySetResult(msg);
            });
            clientB.On<MessageDto>("ReceiveMessage", _ => bystanterReceived = true);

            await clientA.StartAsync();
            await clientB.StartAsync();

            await clientA.InvokeAsync("SetUsername", $"CallerA_{Guid.NewGuid():N}");
            await clientB.InvokeAsync("SetUsername", $"WatcherB_{Guid.NewGuid():N}");

            await clientA.InvokeAsync("JoinTextChannel", 1);
            await clientB.InvokeAsync("JoinTextChannel", 1);

            await clientA.InvokeAsync("SendMessage", 1, "/roll", null);

            var error = await callerReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("System", error.Author);
            Assert.Contains("/roll", error.Content);
            // Ephemeral: Id is 0, not persisted
            Assert.Equal(0, error.Id);

            await Task.Delay(300);
            Assert.False(bystanterReceived);
        }
        finally
        {
            await clientA.StopAsync();
            await clientB.StopAsync();
        }
    }

    [Fact]
    public async Task UnknownCommand_SendsErrorOnlyToCaller()
    {
        var clientA = _fixture.CreateHubConnection();
        var clientB = _fixture.CreateHubConnection();

        try
        {
            var callerReceived = new TaskCompletionSource<MessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            var bystanderReceived = false;

            clientA.On<MessageDto>("ReceiveMessage", msg =>
            {
                if (msg.Author == "System" && msg.Content.Contains("Unknown command")) callerReceived.TrySetResult(msg);
            });
            clientB.On<MessageDto>("ReceiveMessage", _ => bystanderReceived = true);

            await clientA.StartAsync();
            await clientB.StartAsync();

            await clientA.InvokeAsync("SetUsername", $"CallerA_{Guid.NewGuid():N}");
            await clientB.InvokeAsync("SetUsername", $"WatcherB_{Guid.NewGuid():N}");

            await clientA.InvokeAsync("JoinTextChannel", 1);
            await clientB.InvokeAsync("JoinTextChannel", 1);

            await clientA.InvokeAsync("SendMessage", 1, "/notacommand", null);

            var error = await callerReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("System", error.Author);
            Assert.Contains("/notacommand", error.Content);
            Assert.Equal(0, error.Id);

            await Task.Delay(300);
            Assert.False(bystanderReceived);
        }
        finally
        {
            await clientA.StopAsync();
            await clientB.StopAsync();
        }
    }

    // ── non-commands are unaffected ───────────────────────────────────────────

    [Fact]
    public async Task NormalMessage_WithSlashInMiddle_IsNotIntercedpted()
    {
        var client = _fixture.CreateHubConnection();

        try
        {
            var tcs = new TaskCompletionSource<MessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            var content = $"hello/world_{Guid.NewGuid():N}";

            client.On<MessageDto>("ReceiveMessage", msg =>
            {
                if (msg.Content == content) tcs.TrySetResult(msg);
            });

            await client.StartAsync();
            var username = $"Normal_{Guid.NewGuid():N}";
            await client.InvokeAsync("SetUsername", username);
            await client.InvokeAsync("JoinTextChannel", 1);

            await client.InvokeAsync("SendMessage", 1, content, null);

            var msg = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(username, msg.Author);
            Assert.Equal(content, msg.Content);
        }
        finally
        {
            await client.StopAsync();
        }
    }
}
