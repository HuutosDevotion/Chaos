using System.Collections.Concurrent;
using Chaos.Server.Commands;
using Chaos.Server.Data;
using Chaos.Server.Models;
using Chaos.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Chaos.Server.Hubs;

public class ConnectedUser
{
    public string ConnectionId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public int? TextChannelId { get; set; }
    public int? VoiceChannelId { get; set; }
    public int VoiceUserId { get; set; } // matches the userId in UDP packets
    public bool IsMuted { get; set; }
    public bool IsDeafened { get; set; }
    public bool IsScreenSharing { get; set; }
    public int ScreenShareVideoUserId { get; set; }
    public StreamQuality ScreenShareQuality { get; set; }
}

public class ChatHub : Hub
{
    private static readonly ConcurrentDictionary<string, ConnectedUser> _users = new();
    private readonly ChaosDbContext _db;
    private readonly CommandDispatcher _commandDispatcher;

    public ChatHub(ChaosDbContext db, CommandDispatcher commandDispatcher)
    {
        _db = db;
        _commandDispatcher = commandDispatcher;
    }

    public async Task SetUsername(string username)
    {
        var user = new ConnectedUser
        {
            ConnectionId = Context.ConnectionId,
            Username = username
        };
        _users.AddOrUpdate(Context.ConnectionId, user, (_, _) => user);
        await Clients.All.SendAsync("UserConnected", username);
        await Clients.Caller.SendAsync("UsernameSet", username);
    }

    public List<SlashCommandDto> GetAvailableCommands() => _commandDispatcher.GetCommandInfos();

    public async Task<List<ChannelDto>> GetChannels()
    {
        return await _db.Channels.Select(c => new ChannelDto
        {
            Id = c.Id,
            Name = c.Name,
            Type = c.Type
        }).ToListAsync();
    }

    public async Task<List<MessageDto>> GetMessages(int channelId)
    {
        return await _db.Messages
            .Where(m => m.ChannelId == channelId)
            .OrderByDescending(m => m.Timestamp)
            .Take(100)
            .OrderBy(m => m.Timestamp)
            .Select(m => new MessageDto
            {
                Id = m.Id,
                ChannelId = m.ChannelId,
                Author = m.Author,
                Content = m.Content,
                Timestamp = m.Timestamp,
                ImageUrl = m.ImageUrl
            })
            .ToListAsync();
    }

    // Text channel: just subscribes to message group
    public async Task JoinTextChannel(int channelId)
    {
        if (_users.TryGetValue(Context.ConnectionId, out var user))
        {
            if (user.TextChannelId.HasValue)
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"text_{user.TextChannelId}");

            user.TextChannelId = channelId;
            await Groups.AddToGroupAsync(Context.ConnectionId, $"text_{channelId}");
        }
    }

    // Voice channel: join/leave with presence broadcast to everyone
    public async Task JoinVoiceChannel(int channelId, int voiceUserId)
    {
        if (_users.TryGetValue(Context.ConnectionId, out var user))
        {
            // Leave previous voice channel if any
            if (user.VoiceChannelId.HasValue)
            {
                int oldId = user.VoiceChannelId.Value;
                user.VoiceChannelId = null;
                await Clients.All.SendAsync("UserLeftVoice", user.Username, oldId);
            }

            user.VoiceChannelId = channelId;
            user.VoiceUserId = voiceUserId;
            await Clients.All.SendAsync("UserJoinedVoice", user.Username, channelId, voiceUserId);

            // Send current voice members for this channel (username + voiceUserId pairs)
            var members = _users.Values
                .Where(u => u.VoiceChannelId == channelId)
                .Select(u => new { u.Username, u.VoiceUserId })
                .ToList();
            await Clients.Caller.SendAsync("VoiceMembers", channelId, members);
        }
    }

    public async Task LeaveVoiceChannel()
    {
        if (_users.TryGetValue(Context.ConnectionId, out var user) && user.VoiceChannelId.HasValue)
        {
            // Stop screen share if active
            if (user.IsScreenSharing)
            {
                int chId = user.VoiceChannelId.Value;
                user.IsScreenSharing = false;
                user.ScreenShareVideoUserId = 0;
                await Clients.All.SendAsync("UserStoppedScreenShare", user.Username, chId);
            }

            int oldId = user.VoiceChannelId.Value;
            user.VoiceChannelId = null;
            await Clients.All.SendAsync("UserLeftVoice", user.Username, oldId);
        }
    }

    public async Task SendMessage(int channelId, string content, string? imageUrl = null)
    {
        if (!_users.TryGetValue(Context.ConnectionId, out var user))
            return;

        if (imageUrl == null)
        {
            var handled = await _commandDispatcher.TryDispatchAsync(
                content, user.Username, channelId,
                Clients.Group($"text_{channelId}"), Clients.Caller);
            if (handled) return;
        }

        var message = new Message
        {
            ChannelId = channelId,
            Author = user.Username,
            Content = content,
            Timestamp = DateTime.UtcNow,
            ImageUrl = imageUrl
        };

        _db.Messages.Add(message);
        await _db.SaveChangesAsync();

        var dto = new MessageDto
        {
            Id = message.Id,
            ChannelId = message.ChannelId,
            Author = message.Author,
            Content = message.Content,
            Timestamp = message.Timestamp,
            ImageUrl = message.ImageUrl
        };

        await Clients.Group($"text_{channelId}").SendAsync("ReceiveMessage", dto);
    }

    // Screen sharing: must be in a voice channel to share
    public async Task StartScreenShare(int videoUserId, StreamQuality quality)
    {
        if (_users.TryGetValue(Context.ConnectionId, out var user) && user.VoiceChannelId.HasValue)
        {
            user.IsScreenSharing = true;
            user.ScreenShareVideoUserId = videoUserId;
            user.ScreenShareQuality = quality;
            await Clients.All.SendAsync("UserStartedScreenShare",
                user.Username, user.VoiceChannelId.Value, videoUserId, (int)quality);
        }
    }

    public async Task StopScreenShare()
    {
        if (_users.TryGetValue(Context.ConnectionId, out var user) && user.IsScreenSharing)
        {
            int channelId = user.VoiceChannelId ?? 0;
            user.IsScreenSharing = false;
            user.ScreenShareVideoUserId = 0;
            await Clients.All.SendAsync("UserStoppedScreenShare", user.Username, channelId);
        }
    }

    public Dictionary<int, List<ScreenShareMemberDto>> GetAllScreenShareMembers()
    {
        return _users.Values
            .Where(u => u.IsScreenSharing && u.VoiceChannelId.HasValue && !string.IsNullOrEmpty(u.Username))
            .GroupBy(u => u.VoiceChannelId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(u => new ScreenShareMemberDto
            {
                Username = u.Username,
                VideoUserId = u.ScreenShareVideoUserId,
                Quality = u.ScreenShareQuality
            }).ToList());
    }

    public Dictionary<int, List<VoiceMemberDto>> GetAllVoiceMembers()
    {
        return _users.Values
            .Where(u => u.VoiceChannelId.HasValue && !string.IsNullOrEmpty(u.Username))
            .GroupBy(u => u.VoiceChannelId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(u => new VoiceMemberDto
            {
                Username = u.Username,
                VoiceUserId = u.VoiceUserId,
                IsMuted = u.IsMuted,
                IsDeafened = u.IsDeafened
            }).ToList());
    }

    public async Task UpdateMuteState(bool isMuted, bool isDeafened)
    {
        if (_users.TryGetValue(Context.ConnectionId, out var user) && user.VoiceChannelId.HasValue)
        {
            user.IsMuted = isMuted;
            user.IsDeafened = isDeafened;
            await Clients.All.SendAsync("UserMuteStateChanged", user.Username, user.VoiceChannelId.Value, isMuted, isDeafened);
        }
    }

    public async Task<ChannelDto> CreateChannel(string name, ChannelType type)
    {
        name = name.Trim();
        if (string.IsNullOrEmpty(name))
            throw new HubException("Channel name cannot be empty.");
        var channel = new Channel { Name = name, Type = type };
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync();
        var dto = new ChannelDto { Id = channel.Id, Name = channel.Name, Type = channel.Type };
        await Clients.All.SendAsync("ChannelCreated", dto);
        return dto;
    }

    public async Task DeleteChannel(int channelId)
    {
        var channel = await _db.Channels.FindAsync(channelId);
        if (channel is null) throw new HubException("Channel not found.");
        _db.Channels.Remove(channel);
        await _db.SaveChangesAsync();
        await Clients.All.SendAsync("ChannelDeleted", channelId);
    }

    public async Task RenameChannel(int channelId, string newName)
    {
        newName = newName.Trim();
        if (string.IsNullOrEmpty(newName))
            throw new HubException("Channel name cannot be empty.");
        var channel = await _db.Channels.FindAsync(channelId);
        if (channel is null) throw new HubException("Channel not found.");
        channel.Name = newName;
        await _db.SaveChangesAsync();
        var dto = new ChannelDto { Id = channel.Id, Name = channel.Name, Type = channel.Type };
        await Clients.All.SendAsync("ChannelRenamed", dto);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_users.TryRemove(Context.ConnectionId, out var user))
        {
            if (user.IsScreenSharing && user.VoiceChannelId.HasValue)
                await Clients.All.SendAsync("UserStoppedScreenShare", user.Username, user.VoiceChannelId.Value);

            if (user.VoiceChannelId.HasValue)
                await Clients.All.SendAsync("UserLeftVoice", user.Username, user.VoiceChannelId.Value);

            await Clients.All.SendAsync("UserDisconnected", user.Username);
        }
        await base.OnDisconnectedAsync(exception);
    }
}
