using System.Net;
using System.Net.Sockets;
using Chaos.Server.Data;
using Chaos.Server.Hubs;
using Chaos.Server.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to listen on all interfaces
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5000);
});

// Add services
builder.Services.AddSignalR();
builder.Services.AddDbContext<ChaosDbContext>(options =>
    options.UseSqlite("Data Source=chaos.db"));
builder.Services.AddHostedService<VoiceRelay>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

// Auto-migrate and seed database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChaosDbContext>();
    db.Database.EnsureCreated();
}

app.UseCors();
app.MapHub<ChatHub>("/chathub");
app.MapGet("/", () => "Chaos Server is running");

// Print connection info
var localIp = GetLocalIpAddress();
Console.WriteLine("═══════════════════════════════════════════");
Console.WriteLine("  CHAOS SERVER");
Console.WriteLine("═══════════════════════════════════════════");
Console.WriteLine($"  SignalR:  http://{localIp}:5000/chathub");
Console.WriteLine($"  Voice:   udp://{localIp}:9000");
Console.WriteLine("═══════════════════════════════════════════");

app.Run();

static string GetLocalIpAddress()
{
    try
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
        socket.Connect("8.8.8.8", 65530);
        if (socket.LocalEndPoint is IPEndPoint endPoint)
            return endPoint.Address.ToString();
    }
    catch { }
    return "localhost";
}
