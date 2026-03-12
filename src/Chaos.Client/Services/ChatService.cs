using System.Net.Http;
using System.Text.Json;
using Chaos.Shared;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chaos.Client.Services;

public class ChatService : IAsyncDisposable
{
    private HubConnection? _connection;
    private string _baseUrl = string.Empty;
    public string BaseUrl => _baseUrl;

    public event Action<MessageDto>? MessageReceived;
    public event Action<string, int, int>? UserJoinedVoice; // username, channelId, voiceUserId
    public event Action<string, int>? UserLeftVoice;
    public event Action<int, List<VoiceMemberDto>>? VoiceMembersReceived;
    public event Action<string>? UserConnected;
    public event Action<string>? UserDisconnected;
    public event Action<string>? Connected;
    public event Action? Disconnected;
    public event Action<ChannelDto>? ChannelCreated;
    public event Action<int>? ChannelDeleted;
    public event Action<ChannelDto>? ChannelRenamed;
    public event Action<int, string>? UserTyping; // channelId, username
    public event Action<string, int, bool, bool>? UserMuteStateChanged; // username, channelId, isMuted, isDeafened
    public event Action? PongReceived;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public async Task ConnectAsync(string serverUrl)
    {
        _baseUrl = serverUrl.Replace("/chathub", "");

        _connection = new HubConnectionBuilder()
            .WithUrl(serverUrl)
            .WithAutomaticReconnect()
            .Build();

        _connection.On<MessageDto>("ReceiveMessage", msg =>
            MessageReceived?.Invoke(msg));

        _connection.On<string, int, int>("UserJoinedVoice", (user, channelId, voiceUserId) =>
            UserJoinedVoice?.Invoke(user, channelId, voiceUserId));

        _connection.On<string, int>("UserLeftVoice", (user, channelId) =>
            UserLeftVoice?.Invoke(user, channelId));

        _connection.On<int, List<VoiceMemberDto>>("VoiceMembers", (channelId, members) =>
            VoiceMembersReceived?.Invoke(channelId, members));

        _connection.On<string>("UserConnected", user =>
            UserConnected?.Invoke(user));

        _connection.On<string>("UserDisconnected", user =>
            UserDisconnected?.Invoke(user));

        _connection.On<string>("UsernameSet", username =>
            Connected?.Invoke(username));

        _connection.On<ChannelDto>("ChannelCreated", dto => ChannelCreated?.Invoke(dto));
        _connection.On<int>("ChannelDeleted", id => ChannelDeleted?.Invoke(id));
        _connection.On<ChannelDto>("ChannelRenamed", dto => ChannelRenamed?.Invoke(dto));
        _connection.On<int, string>("UserTyping", (channelId, username) => UserTyping?.Invoke(channelId, username));
        _connection.On<string, int, bool, bool>("UserMuteStateChanged", (user, channelId, muted, deafened) =>
            UserMuteStateChanged?.Invoke(user, channelId, muted, deafened));
        _connection.On("Pong", () => PongReceived?.Invoke());

        _connection.Closed += _ =>
        {
            Disconnected?.Invoke();
            return Task.CompletedTask;
        };

        await _connection.StartAsync();
    }

    public async Task SetUsername(string username)
    {
        if (_connection is not null)
            await _connection.InvokeAsync("SetUsername", username);
    }

    public async Task<List<SlashCommandDto>> GetAvailableCommandsAsync()
    {
        if (_connection is not null)
            return await _connection.InvokeAsync<List<SlashCommandDto>>("GetAvailableCommands");
        return new();
    }

    public async Task<List<ChannelDto>> GetChannels()
    {
        if (_connection is not null)
            return await _connection.InvokeAsync<List<ChannelDto>>("GetChannels");
        return new();
    }

    public async Task<List<MessageDto>> GetMessages(int channelId)
    {
        if (_connection is not null)
            return await _connection.InvokeAsync<List<MessageDto>>("GetMessages", channelId);
        return new();
    }

    public async Task JoinTextChannel(int channelId)
    {
        if (_connection is not null)
            await _connection.InvokeAsync("JoinTextChannel", channelId);
    }

    public async Task JoinVoiceChannel(int channelId, int voiceUserId)
    {
        if (_connection is not null)
            await _connection.InvokeAsync("JoinVoiceChannel", channelId, voiceUserId);
    }

    public async Task LeaveVoiceChannel()
    {
        if (_connection is not null)
            await _connection.InvokeAsync("LeaveVoiceChannel");
    }

    public async Task<string?> UploadImageAsync(byte[] imageData, string filename)
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            using var content = new MultipartFormDataContent();
            var ext = System.IO.Path.GetExtension(filename)?.ToLower() ?? ".png";
            if (string.IsNullOrEmpty(ext)) ext = ".png";
            var safeFilename = $"upload{ext}";
            var fileContent = new ByteArrayContent(imageData);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                ext switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    ".bmp" => "image/bmp",
                    ".webp" => "image/webp",
                    _ => "image/png"
                });
            content.Add(fileContent, "file", safeFilename);
            var response = await http.PostAsync($"{_baseUrl}/api/upload", content);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var relative = doc.RootElement.GetProperty("url").GetString();
            return relative;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Upload] FAILED: {ex}");
            return null;
        }
    }

    public async Task StartTypingAsync(int channelId)
    {
        if (_connection is not null)
            await _connection.InvokeAsync("StartTyping", channelId);
    }

    public async Task SendMessage(int channelId, string content, string? imageUrl = null)
    {
        if (_connection is not null)
            await _connection.InvokeAsync("SendMessage", channelId, content, imageUrl);
    }

    public async Task<List<string>> GetConnectedUsers()
    {
        if (_connection is not null)
            return await _connection.InvokeAsync<List<string>>("GetConnectedUsers");
        return new();
    }

    public async Task<Dictionary<int, List<VoiceMemberDto>>> GetAllVoiceMembers()
    {
        if (_connection is not null)
            return await _connection.InvokeAsync<Dictionary<int, List<VoiceMemberDto>>>("GetAllVoiceMembers");
        return new();
    }

    public async Task<ChannelDto?> CreateChannelAsync(string name, ChannelType type)
    {
        if (_connection is not null)
            return await _connection.InvokeAsync<ChannelDto>("CreateChannel", name, type);
        return null;
    }

    public async Task DeleteChannelAsync(int channelId)
    {
        if (_connection is not null)
            await _connection.InvokeAsync("DeleteChannel", channelId);
    }

    public async Task RenameChannelAsync(int channelId, string newName)
    {
        if (_connection is not null)
            await _connection.InvokeAsync("RenameChannel", channelId, newName);
    }

    public async Task SendPing()
    {
        if (_connection is not null)
            await _connection.InvokeAsync("Ping");
    }

    public async Task UpdateMuteState(bool isMuted, bool isDeafened)
    {
        if (_connection is not null)
            await _connection.InvokeAsync("UpdateMuteState", isMuted, isDeafened);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
