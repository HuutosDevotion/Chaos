using Chaos.Shared;
using Xunit;
using Chaos.Tests.Fixtures;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chaos.Tests;

/// <summary>
/// Integration tests that verify the server-side contracts the client UX
/// features depend on — channel ordering, seed data, and return values.
/// </summary>
[Collection("ChaosServer")]
public class UxSupportTests
{
    private readonly ChaosServerFixture _fixture;

    public UxSupportTests(ChaosServerFixture fixture)
    {
        _fixture = fixture;
    }

    // ── auto-select first text channel on login ───────────────────────────────

    /// <summary>
    /// The client picks Channels.FirstOrDefault(c => c.Type == Text) right after
    /// connecting.  For this to reliably land on a text channel, text channels
    /// must appear before voice channels in GetChannels.
    /// </summary>
    [Fact]
    public async Task GetChannels_TextChannelsComeBeforeVoiceChannels()
    {
        var client = _fixture.CreateHubConnection();

        try
        {
            await client.StartAsync();
            var channels = await client.InvokeAsync<List<ChannelDto>>("GetChannels");

            Assert.NotEmpty(channels);

            // Find the index of the last text channel and the first voice channel.
            var lastTextIndex = channels.Select((c, i) => (c, i))
                                        .Where(x => x.c.Type == ChannelType.Text)
                                        .Select(x => x.i)
                                        .DefaultIfEmpty(-1)
                                        .Last();

            var firstVoiceIndex = channels.Select((c, i) => (c, i))
                                          .Where(x => x.c.Type == ChannelType.Voice)
                                          .Select(x => x.i)
                                          .DefaultIfEmpty(int.MaxValue)
                                          .First();

            Assert.True(lastTextIndex < firstVoiceIndex,
                "All text channels must appear before any voice channel so the " +
                "client's FirstOrDefault(Text) resolves correctly.");
        }
        finally
        {
            await client.StopAsync();
        }
    }

    /// <summary>
    /// There must be at least one text channel in the seed data so the client
    /// can auto-select it immediately after login.
    /// </summary>
    [Fact]
    public async Task GetChannels_AlwaysIncludesAtLeastOneTextChannel()
    {
        var client = _fixture.CreateHubConnection();

        try
        {
            await client.StartAsync();
            var channels = await client.InvokeAsync<List<ChannelDto>>("GetChannels");

            Assert.Contains(channels, c => c.Type == ChannelType.Text);
        }
        finally
        {
            await client.StopAsync();
        }
    }

    // ── auto-select newly created text channel ────────────────────────────────

    /// <summary>
    /// After CreateChannel the client uses the returned ChannelDto to look up
    /// the new channel in its local list and select it.  The return value must
    /// carry the server-assigned Id so that lookup succeeds.
    /// </summary>
    [Fact]
    public async Task CreateTextChannel_ReturnsDtoWithServerAssignedId()
    {
        var client = _fixture.CreateHubConnection();
        int createdId = 0;

        try
        {
            await client.StartAsync();
            var name = $"AutoSelect_{Guid.NewGuid():N}";

            var dto = await client.InvokeAsync<ChannelDto>("CreateChannel", name, ChannelType.Text);
            createdId = dto.Id;

            Assert.True(dto.Id > 0, "Id must be server-assigned (> 0) for client lookup to work.");
            Assert.Equal(name, dto.Name);
            Assert.Equal(ChannelType.Text, dto.Type);
        }
        finally
        {
            if (createdId != 0)
                await client.InvokeAsync("DeleteChannel", createdId);
            await client.StopAsync();
        }
    }

    /// <summary>
    /// The ChannelCreated broadcast arrives before (or by the time) CreateChannel
    /// returns, so the new channel is already in the client's Channels list
    /// when the auto-select code runs.
    /// </summary>
    [Fact]
    public async Task CreateTextChannel_ChannelCreatedBroadcastArrivesBeforeInvokeReturns()
    {
        var client = _fixture.CreateHubConnection();
        int createdId = 0;

        try
        {
            var broadcastedId = 0;
            client.On<ChannelDto>("ChannelCreated", dto => broadcastedId = dto.Id);

            await client.StartAsync();
            var name = $"Timing_{Guid.NewGuid():N}";

            var returned = await client.InvokeAsync<ChannelDto>("CreateChannel", name, ChannelType.Text);
            createdId = returned.Id;

            // Give the broadcast a moment if it arrives slightly after (LongPolling)
            await Task.Delay(200);

            Assert.Equal(returned.Id, broadcastedId);
        }
        finally
        {
            if (createdId != 0)
                await client.InvokeAsync("DeleteChannel", createdId);
            await client.StopAsync();
        }
    }

    // ── default channel set includes voice channel ────────────────────────────

    /// <summary>
    /// There must be at least one voice channel in the seed data so the
    /// active-voice highlight feature has a target to test against in the UI.
    /// </summary>
    [Fact]
    public async Task GetChannels_AlwaysIncludesAtLeastOneVoiceChannel()
    {
        var client = _fixture.CreateHubConnection();

        try
        {
            await client.StartAsync();
            var channels = await client.InvokeAsync<List<ChannelDto>>("GetChannels");

            Assert.Contains(channels, c => c.Type == ChannelType.Voice);
        }
        finally
        {
            await client.StopAsync();
        }
    }
}
