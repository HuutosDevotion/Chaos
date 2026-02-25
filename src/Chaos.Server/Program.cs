using System.Net;
using System.Net.Sockets;
using Chaos.Server.Data;
using Chaos.Server.Hubs;
using Chaos.Server.Services;
using Microsoft.EntityFrameworkCore;

var logPath = Path.Combine(AppContext.BaseDirectory, "server.log");

void Log(string msg)
{
    var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}";
    Console.WriteLine(line);
    File.AppendAllText(logPath, line + Environment.NewLine);
}

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    Log($"FATAL: {e.ExceptionObject}");
};

Log("Starting Chaos Server...");

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

// wwwroot must exist before Build() so UseStaticFiles() gets a PhysicalFileProvider, not NullFileProvider
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "uploads"));

var app = builder.Build();

// Auto-migrate and seed database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ChaosDbContext>();
    db.Database.EnsureCreated();
}

app.UseCors();
app.UseStaticFiles();
app.MapHub<ChatHub>("/chathub");
app.MapGet("/", () => "Chaos Server is running");

app.MapPost("/api/upload", async (IFormFile file, IWebHostEnvironment env) =>
{
    var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
    var ext = Path.GetExtension(file.FileName).ToLower();
    if (!allowed.Contains(ext)) return Results.BadRequest("Not an image");

    var dir = Path.Combine(env.ContentRootPath, "wwwroot", "uploads");
    Directory.CreateDirectory(dir);

    var name = $"{Guid.NewGuid()}{ext}";
    await using var stream = File.Create(Path.Combine(dir, name));
    await file.CopyToAsync(stream);

    return Results.Ok(new { url = $"/uploads/{name}" });
}).DisableAntiforgery();

// Print connection info
var localIp = GetLocalIpAddress();
Log("═══════════════════════════════════════════");
Log("  CHAOS SERVER");
Log("═══════════════════════════════════════════");
Log($"  SignalR:  http://{localIp}:5000/chathub");
Log($"  Voice:   udp://{localIp}:9000");
Log("═══════════════════════════════════════════");

try
{
    app.Run();
}
catch (Exception ex)
{
    Log($"FATAL: {ex}");
    Console.Error.WriteLine("Server crashed. See server.log for details. Press any key to exit...");
    Console.ReadKey();
    throw;
}

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

public partial class Program { }
