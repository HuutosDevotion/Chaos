using Chaos.Server.Data;
using Xunit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Chaos.Tests.Fixtures;

public class ChaosServerFixture : WebApplicationFactory<Program>
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"chaos_test_{Guid.NewGuid():N}");

    private string DbPath => Path.Combine(_tempDir, "test_chaos.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_tempDir);

        builder.UseContentRoot(_tempDir);

        builder.ConfigureTestServices(services =>
        {
            // Remove existing ChaosDbContext options so we can replace the connection string
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ChaosDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            services.AddDbContext<ChaosDbContext>(options =>
                options.UseSqlite($"Data Source={DbPath}"));
        });
    }

    /// <summary>
    /// Creates a SignalR HubConnection routed through the in-process TestServer.
    /// Uses LongPolling because TestServer does not support WebSockets.
    /// </summary>
    public HubConnection CreateHubConnection()
    {
        return new HubConnectionBuilder()
            .WithUrl("http://localhost/chathub", options =>
            {
                options.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }
}

[CollectionDefinition("ChaosServer")]
public class ChaosServerCollection : ICollectionFixture<ChaosServerFixture> { }
