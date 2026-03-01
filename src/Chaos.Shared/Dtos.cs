namespace Chaos.Shared;

public enum ChannelType
{
    Text,
    Voice
}

public class ChannelDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ChannelType Type { get; set; }
}

public class MessageDto
{
    public int Id { get; set; }
    public int ChannelId { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? ImageUrl { get; set; }
    public bool HasImage => !string.IsNullOrEmpty(ImageUrl);
}

public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
}

public class VoiceMemberDto
{
    public string Username { get; set; } = string.Empty;
    public int VoiceUserId { get; set; }
}

public class VoicePacket
{
    public int UserId { get; set; }
    public int ChannelId { get; set; }
    public byte[] AudioData { get; set; } = Array.Empty<byte>();
}

public class NewMessageIndicatorDto
{
    public int ChannelId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string ContentPreview { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public List<string> MentionedUsers { get; set; } = new();
}

public class SlashCommandDto
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Usage { get; set; } = string.Empty;
}
