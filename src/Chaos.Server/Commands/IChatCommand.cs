using Chaos.Server.Data;
using Microsoft.AspNetCore.SignalR;

namespace Chaos.Server.Commands;

public record CommandContext(
    string Username,
    int ChannelId,
    string[] Args,
    IClientProxy ChannelGroup,
    ISingleClientProxy Caller
);

public interface IChatCommand
{
    string Name { get; }  // lowercase, e.g. "roll"
    Task ExecuteAsync(CommandContext context);
}
