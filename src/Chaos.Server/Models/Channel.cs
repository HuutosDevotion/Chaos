using Chaos.Shared;

namespace Chaos.Server.Models;

public class Channel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ChannelType Type { get; set; }
    public List<Message> Messages { get; set; } = new();
}
