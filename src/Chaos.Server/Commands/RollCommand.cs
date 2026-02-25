using Chaos.Server.Data;
using Chaos.Server.Models;
using Chaos.Shared;
using Microsoft.AspNetCore.SignalR;

namespace Chaos.Server.Commands;

public class RollCommand : IChatCommand
{
    private readonly ChaosDbContext _db;
    public string Name => "roll";
    public string Description => "Roll dice";
    public string Usage => "/roll d<sides>  (e.g. /roll d20)";

    public RollCommand(ChaosDbContext db) => _db = db;

    public async Task ExecuteAsync(CommandContext context)
    {
        if (context.Args.Length == 0 || !TryParseDie(context.Args[0], out int sides))
        {
            await context.Caller.SendAsync("ReceiveMessage", EphemeralSystem(context.ChannelId,
                "Usage: /roll d<sides>  (e.g. /roll d20)"));
            return;
        }

        int result = Random.Shared.Next(1, sides + 1);
        string text = $"{context.Username} rolled a d{sides} and got {result}!";

        var message = new Message
        {
            ChannelId = context.ChannelId,
            Author = "System",
            Content = text,
            Timestamp = DateTime.UtcNow
        };
        _db.Messages.Add(message);
        await _db.SaveChangesAsync();

        await context.ChannelGroup.SendAsync("ReceiveMessage", new MessageDto
        {
            Id = message.Id,
            ChannelId = message.ChannelId,
            Author = "System",
            Content = text,
            Timestamp = message.Timestamp
        });
    }

    private static bool TryParseDie(string arg, out int sides)
    {
        sides = 0;
        return arg.Length >= 2 && (arg[0] == 'd' || arg[0] == 'D')
            && int.TryParse(arg.AsSpan(1), out sides) && sides >= 2;
    }

    private static MessageDto EphemeralSystem(int channelId, string text) => new()
    {
        Id = 0,
        ChannelId = channelId,
        Author = "System",
        Content = text,
        Timestamp = DateTime.UtcNow
    };
}
