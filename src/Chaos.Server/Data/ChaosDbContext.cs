using Chaos.Server.Models;
using Chaos.Shared;
using Microsoft.EntityFrameworkCore;

namespace Chaos.Server.Data;

public class ChaosDbContext : DbContext
{
    public ChaosDbContext(DbContextOptions<ChaosDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Channel>().HasData(
            new Channel { Id = 1, Name = "general", Type = ChannelType.Text },
            new Channel { Id = 2, Name = "random", Type = ChannelType.Text },
            new Channel { Id = 3, Name = "Voice Chat", Type = ChannelType.Voice }
        );
    }
}
