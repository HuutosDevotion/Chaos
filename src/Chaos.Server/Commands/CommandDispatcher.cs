using Chaos.Shared;
using Microsoft.AspNetCore.SignalR;

namespace Chaos.Server.Commands;

public class CommandDispatcher
{
    private readonly Dictionary<string, IChatCommand> _commands;

    public CommandDispatcher(IEnumerable<IChatCommand> commands)
        => _commands = commands.ToDictionary(c => c.Name.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);

    public List<SlashCommandDto> GetCommandInfos() =>
        _commands.Values
            .Select(c => new SlashCommandDto { Name = c.Name, Description = c.Description, Usage = c.Usage })
            .OrderBy(c => c.Name)
            .ToList();

    // Returns true if content was a slash-command (handled or error sent)
    public async Task<bool> TryDispatchAsync(
        string content, string username, int channelId,
        IClientProxy channelGroup, ISingleClientProxy caller)
    {
        if (string.IsNullOrWhiteSpace(content) || content[0] != '/') return false;

        var parts = content[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        var verb = parts[0];
        if (!_commands.TryGetValue(verb, out var command))
        {
            await caller.SendAsync("ReceiveMessage", new MessageDto
            {
                Id = 0,
                ChannelId = channelId,
                Author = "System",
                Content = $"Unknown command: /{verb}",
                Timestamp = DateTime.UtcNow
            });
            return true;
        }

        await command.ExecuteAsync(new CommandContext(username, channelId, parts[1..], channelGroup, caller));
        return true;
    }
}
