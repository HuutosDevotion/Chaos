using Xunit;
using Chaos.Tests.Fixtures;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chaos.Tests;

[Collection("ChaosServer")]
public class ConnectedUsersTests
{
    private readonly ChaosServerFixture _fixture;

    public ConnectedUsersTests(ChaosServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetConnectedUsers_AfterSetUsername_IncludesUser()
    {
        var client = _fixture.CreateHubConnection();
        try
        {
            await client.StartAsync();
            var username = $"User_{Guid.NewGuid():N}";
            await client.InvokeAsync("SetUsername", username);

            var users = await client.InvokeAsync<List<string>>("GetConnectedUsers");

            Assert.Contains(username, users);
        }
        finally
        {
            await client.StopAsync();
        }
    }

    [Fact]
    public async Task GetConnectedUsers_WithoutSettingUsername_ExcludesUnnamedConnection()
    {
        var namedClient = _fixture.CreateHubConnection();
        var unnamedClient = _fixture.CreateHubConnection();
        try
        {
            await namedClient.StartAsync();
            await unnamedClient.StartAsync();

            var username = $"Named_{Guid.NewGuid():N}";
            await namedClient.InvokeAsync("SetUsername", username);
            // unnamedClient deliberately does not call SetUsername

            var users = await namedClient.InvokeAsync<List<string>>("GetConnectedUsers");

            Assert.All(users, u => Assert.False(string.IsNullOrEmpty(u)));
        }
        finally
        {
            await namedClient.StopAsync();
            await unnamedClient.StopAsync();
        }
    }

    [Fact]
    public async Task GetConnectedUsers_MultipleUsers_ReturnsAllUsernames()
    {
        var clientA = _fixture.CreateHubConnection();
        var clientB = _fixture.CreateHubConnection();
        try
        {
            await clientA.StartAsync();
            await clientB.StartAsync();

            var usernameA = $"UserA_{Guid.NewGuid():N}";
            var usernameB = $"UserB_{Guid.NewGuid():N}";
            await clientA.InvokeAsync("SetUsername", usernameA);
            await clientB.InvokeAsync("SetUsername", usernameB);

            var users = await clientA.InvokeAsync<List<string>>("GetConnectedUsers");

            Assert.Contains(usernameA, users);
            Assert.Contains(usernameB, users);
        }
        finally
        {
            await clientA.StopAsync();
            await clientB.StopAsync();
        }
    }

    [Fact]
    public async Task GetConnectedUsers_AfterDisconnect_RemovesUser()
    {
        var leavingClient = _fixture.CreateHubConnection();
        var stayingClient = _fixture.CreateHubConnection();
        try
        {
            await leavingClient.StartAsync();
            await stayingClient.StartAsync();

            var leavingName = $"Leaving_{Guid.NewGuid():N}";
            var stayingName = $"Staying_{Guid.NewGuid():N}";
            await leavingClient.InvokeAsync("SetUsername", leavingName);
            await stayingClient.InvokeAsync("SetUsername", stayingName);

            await leavingClient.StopAsync();
            await Task.Delay(300); // give server time to process the disconnect

            var users = await stayingClient.InvokeAsync<List<string>>("GetConnectedUsers");

            Assert.DoesNotContain(leavingName, users);
            Assert.Contains(stayingName, users);
        }
        finally
        {
            if (leavingClient.State != HubConnectionState.Disconnected)
                await leavingClient.StopAsync();
            await stayingClient.StopAsync();
        }
    }

    [Fact]
    public async Task GetConnectedUsers_ReturnsSortedAlphabetically()
    {
        var clientA = _fixture.CreateHubConnection();
        var clientB = _fixture.CreateHubConnection();
        try
        {
            await clientA.StartAsync();
            await clientB.StartAsync();

            // Deliberately reversed alphabetical order to confirm sorting
            var usernameA = $"ZZZ_{Guid.NewGuid():N}";
            var usernameB = $"AAA_{Guid.NewGuid():N}";
            await clientA.InvokeAsync("SetUsername", usernameA);
            await clientB.InvokeAsync("SetUsername", usernameB);

            var users = await clientA.InvokeAsync<List<string>>("GetConnectedUsers");

            var testUsers = users.Where(u => u == usernameA || u == usernameB).ToList();
            Assert.Equal(2, testUsers.Count);
            Assert.Equal(testUsers.OrderBy(u => u, StringComparer.OrdinalIgnoreCase).ToList(), testUsers);
        }
        finally
        {
            await clientA.StopAsync();
            await clientB.StopAsync();
        }
    }

    [Fact]
    public async Task UserDisconnected_BroadcastsToOtherClients()
    {
        var clientA = _fixture.CreateHubConnection();
        var clientB = _fixture.CreateHubConnection();
        try
        {
            var username = $"Disconnector_{Guid.NewGuid():N}";
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            await clientB.StartAsync();
            await clientA.StartAsync();

            clientB.On<string>("UserDisconnected", name =>
            {
                if (name == username) tcs.TrySetResult(name);
            });

            await clientA.InvokeAsync("SetUsername", username);
            await clientA.StopAsync();

            var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(username, received);
        }
        finally
        {
            if (clientA.State != HubConnectionState.Disconnected)
                await clientA.StopAsync();
            await clientB.StopAsync();
        }
    }
}
