using Chaos.Server.Data;
using Chaos.Server.Models;
using Chaos.Shared;
using Microsoft.AspNetCore.SignalR;

namespace Chaos.Server.Commands;

public class ShrugCommand : IChatCommand
{
    private readonly ChaosDbContext _db;
    public string Name => "shrug";
    public string Description => "Append a shrug to your message";
    public string Usage => "/shrug [message]";

    public ShrugCommand(ChaosDbContext db) => _db = db;

    public async Task ExecuteAsync(CommandContext context)
    {
        var text = context.Args.Length > 0
            ? $"{string.Join(" ", context.Args)} ¯\\_(ツ)_/¯"
            : "¯\\_(ツ)_/¯";

        var message = new Message
        {
            ChannelId = context.ChannelId,
            Author = context.Username,
            Content = text,
            Timestamp = DateTime.UtcNow
        };
        _db.Messages.Add(message);
        await _db.SaveChangesAsync();

        await context.ChannelGroup.SendAsync("ReceiveMessage", new MessageDto
        {
            Id = message.Id,
            ChannelId = message.ChannelId,
            Author = message.Author,
            Content = text,
            Timestamp = message.Timestamp
        });
    }
}
