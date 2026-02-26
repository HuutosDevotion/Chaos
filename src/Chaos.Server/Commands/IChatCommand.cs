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
    string Name { get; }         // lowercase, e.g. "roll"
    string Description { get; }  // short description shown in autocomplete
    string Usage { get; }        // usage hint, e.g. "/roll d20"
    Task ExecuteAsync(CommandContext context);
}
