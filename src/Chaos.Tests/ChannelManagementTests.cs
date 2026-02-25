using Chaos.Shared;
using Xunit;
using Chaos.Tests.Fixtures;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chaos.Tests;

[Collection("ChaosServer")]
public class ChannelManagementTests
{
    private readonly ChaosServerFixture _fixture;

    public ChannelManagementTests(ChaosServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateChannel_BroadcastsChannelCreatedToAllClients()
    {
        var clientA = _fixture.CreateHubConnection();
        var clientB = _fixture.CreateHubConnection();
        int createdChannelId = 0;

        try
        {
            var channelName = $"TestCh_{Guid.NewGuid():N}";
            var tcs = new TaskCompletionSource<ChannelDto>(TaskCreationOptions.RunContinuationsAsynchronously);

            await clientA.StartAsync();
            await clientB.StartAsync();

            clientB.On<ChannelDto>("ChannelCreated", dto =>
            {
                if (dto.Name == channelName) tcs.TrySetResult(dto);
            });

            var created = await clientA.InvokeAsync<ChannelDto>("CreateChannel", channelName, ChannelType.Text);
            createdChannelId = created.Id;

            var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(channelName, received.Name);
            Assert.Equal(ChannelType.Text, received.Type);
        }
        finally
        {
            if (createdChannelId != 0)
                await clientA.InvokeAsync("DeleteChannel", createdChannelId);
            await clientA.StopAsync();
            await clientB.StopAsync();
        }
    }

    [Fact]
    public async Task CreateChannel_VoiceType_ReturnsCorrectType()
    {
        var clientA = _fixture.CreateHubConnection();
        int createdChannelId = 0;

        try
        {
            await clientA.StartAsync();
            var channelName = $"TestCh_{Guid.NewGuid():N}";
            var created = await clientA.InvokeAsync<ChannelDto>("CreateChannel", channelName, ChannelType.Voice);
            createdChannelId = created.Id;

            Assert.Equal(channelName, created.Name);
            Assert.Equal(ChannelType.Voice, created.Type);
            Assert.True(created.Id > 0);
        }
        finally
        {
            if (createdChannelId != 0)
                await clientA.InvokeAsync("DeleteChannel", createdChannelId);
            await clientA.StopAsync();
        }
    }

    [Fact]
    public async Task CreateChannel_AppearsInGetChannels()
    {
        var client = _fixture.CreateHubConnection();
        int createdChannelId = 0;

        try
        {
            await client.StartAsync();
            var channelName = $"TestCh_{Guid.NewGuid():N}";
            var created = await client.InvokeAsync<ChannelDto>("CreateChannel", channelName, ChannelType.Text);
            createdChannelId = created.Id;

            var channels = await client.InvokeAsync<List<ChannelDto>>("GetChannels");
            Assert.Contains(channels, c => c.Id == createdChannelId && c.Name == channelName);
        }
        finally
        {
            if (createdChannelId != 0)
                await client.InvokeAsync("DeleteChannel", createdChannelId);
            await client.StopAsync();
        }
    }

    [Fact]
    public async Task DeleteChannel_BroadcastsChannelDeletedToAllClients()
    {
        var clientA = _fixture.CreateHubConnection();
        var clientB = _fixture.CreateHubConnection();
        int createdChannelId = 0;

        try
        {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            int? targetId = null;

            // Register handler before StartAsync so no messages are missed
            clientB.On<int>("ChannelDeleted", id =>
            {
                if (targetId.HasValue && id == targetId.Value) tcs.TrySetResult(id);
            });

            await clientA.StartAsync();
            await clientB.StartAsync();

            var channelName = $"TestCh_{Guid.NewGuid():N}";
            var created = await clientA.InvokeAsync<ChannelDto>("CreateChannel", channelName, ChannelType.Text);
            createdChannelId = created.Id;
            targetId = createdChannelId;

            await clientA.InvokeAsync("DeleteChannel", createdChannelId);
            createdChannelId = 0;

            var receivedId = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(created.Id, receivedId);
        }
        finally
        {
            if (createdChannelId != 0)
                await clientA.InvokeAsync("DeleteChannel", createdChannelId);
            await clientA.StopAsync();
            await clientB.StopAsync();
        }
    }

    [Fact]
    public async Task DeleteChannel_NoLongerInGetChannels()
    {
        var client = _fixture.CreateHubConnection();
        int createdChannelId = 0;

        try
        {
            await client.StartAsync();
            var channelName = $"TestCh_{Guid.NewGuid():N}";
            var created = await client.InvokeAsync<ChannelDto>("CreateChannel", channelName, ChannelType.Text);
            createdChannelId = created.Id;

            await client.InvokeAsync("DeleteChannel", createdChannelId);
            createdChannelId = 0;

            var channels = await client.InvokeAsync<List<ChannelDto>>("GetChannels");
            Assert.DoesNotContain(channels, c => c.Id == created.Id);
        }
        finally
        {
            if (createdChannelId != 0)
                await client.InvokeAsync("DeleteChannel", createdChannelId);
            await client.StopAsync();
        }
    }

    [Fact]
    public async Task RenameChannel_BroadcastsChannelRenamedToAllClients()
    {
        var clientA = _fixture.CreateHubConnection();
        var clientB = _fixture.CreateHubConnection();
        int createdChannelId = 0;

        try
        {
            var tcs = new TaskCompletionSource<ChannelDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            int? targetId = null;

            // Register handler before StartAsync so no messages are missed
            clientB.On<ChannelDto>("ChannelRenamed", dto =>
            {
                if (targetId.HasValue && dto.Id == targetId.Value) tcs.TrySetResult(dto);
            });

            await clientA.StartAsync();
            await clientB.StartAsync();

            var channelName = $"TestCh_{Guid.NewGuid():N}";
            var created = await clientA.InvokeAsync<ChannelDto>("CreateChannel", channelName, ChannelType.Text);
            createdChannelId = created.Id;
            targetId = createdChannelId;

            var newName = $"Renamed_{Guid.NewGuid():N}";
            await clientA.InvokeAsync("RenameChannel", createdChannelId, newName);

            var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(createdChannelId, received.Id);
            Assert.Equal(newName, received.Name);
        }
        finally
        {
            if (createdChannelId != 0)
                await clientA.InvokeAsync("DeleteChannel", createdChannelId);
            await clientA.StopAsync();
            await clientB.StopAsync();
        }
    }

    [Fact]
    public async Task RenameChannel_PersistedToDatabase()
    {
        var client = _fixture.CreateHubConnection();
        int createdChannelId = 0;

        try
        {
            await client.StartAsync();
            var channelName = $"TestCh_{Guid.NewGuid():N}";
            var created = await client.InvokeAsync<ChannelDto>("CreateChannel", channelName, ChannelType.Text);
            createdChannelId = created.Id;

            var newName = $"Renamed_{Guid.NewGuid():N}";
            await client.InvokeAsync("RenameChannel", createdChannelId, newName);

            var channels = await client.InvokeAsync<List<ChannelDto>>("GetChannels");
            var renamed = channels.FirstOrDefault(c => c.Id == createdChannelId);
            Assert.NotNull(renamed);
            Assert.Equal(newName, renamed.Name);
        }
        finally
        {
            if (createdChannelId != 0)
                await client.InvokeAsync("DeleteChannel", createdChannelId);
            await client.StopAsync();
        }
    }

    [Fact]
    public async Task CreateChannel_EmptyName_ThrowsHubException()
    {
        var client = _fixture.CreateHubConnection();

        try
        {
            await client.StartAsync();
            await Assert.ThrowsAsync<HubException>(() =>
                client.InvokeAsync<ChannelDto>("CreateChannel", "   ", ChannelType.Text));
        }
        finally
        {
            await client.StopAsync();
        }
    }

    [Fact]
    public async Task DeleteChannel_NonExistentId_ThrowsHubException()
    {
        var client = _fixture.CreateHubConnection();

        try
        {
            await client.StartAsync();
            await Assert.ThrowsAsync<HubException>(() =>
                client.InvokeAsync("DeleteChannel", 999999));
        }
        finally
        {
            await client.StopAsync();
        }
    }

    [Fact]
    public async Task RenameChannel_EmptyName_ThrowsHubException()
    {
        var client = _fixture.CreateHubConnection();
        int createdChannelId = 0;

        try
        {
            await client.StartAsync();
            var channelName = $"TestCh_{Guid.NewGuid():N}";
            var created = await client.InvokeAsync<ChannelDto>("CreateChannel", channelName, ChannelType.Text);
            createdChannelId = created.Id;

            await Assert.ThrowsAsync<HubException>(() =>
                client.InvokeAsync("RenameChannel", createdChannelId, ""));
        }
        finally
        {
            if (createdChannelId != 0)
                await client.InvokeAsync("DeleteChannel", createdChannelId);
            await client.StopAsync();
        }
    }
}
