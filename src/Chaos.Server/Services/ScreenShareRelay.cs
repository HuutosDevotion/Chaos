using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Chaos.Server.Services;

public class ScreenShareRelay : BackgroundService
{
    private readonly ILogger<ScreenShareRelay> _logger;
    private UdpClient? _udpServer;
    private const int Port = 9001;

    // Same header format as voice: 4 bytes userId + 4 bytes channelId, then video data
    // Registration packet: userId + channelId + 4 bytes magic "VDEO"
    private static readonly byte[] RegistrationMagic = "VDEO"u8.ToArray();
    private static readonly byte[] UnregisterMagic = "BYE!"u8.ToArray();

    private readonly ConcurrentDictionary<int, IPEndPoint> _userEndpoints = new();
    private readonly ConcurrentDictionary<int, int> _userChannels = new();

    public ScreenShareRelay(ILogger<ScreenShareRelay> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _udpServer = new UdpClient(Port);

        // Allow large UDP packets for video frames (up to 64KB)
        _udpServer.Client.ReceiveBufferSize = 1024 * 1024;
        _udpServer.Client.SendBufferSize = 1024 * 1024;

        _logger.LogInformation("Screen share relay listening on UDP port {Port}", Port);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udpServer.ReceiveAsync(stoppingToken);
                var data = result.Buffer;

                if (data.Length < 8)
                    continue;

                int userId = BitConverter.ToInt32(data, 0);
                int channelId = BitConverter.ToInt32(data, 4);

                // Check if this is a registration packet
                if (data.Length == 12 && data[8] == RegistrationMagic[0]
                    && data[9] == RegistrationMagic[1]
                    && data[10] == RegistrationMagic[2]
                    && data[11] == RegistrationMagic[3])
                {
                    _userEndpoints[userId] = result.RemoteEndPoint;
                    _userChannels[userId] = channelId;
                    _logger.LogInformation("Screen share user {UserId} registered from {Endpoint} in channel {Channel}",
                        userId, result.RemoteEndPoint, channelId);
                    continue;
                }

                // Check if this is an unregister packet
                if (data.Length == 12 && data[8] == UnregisterMagic[0]
                    && data[9] == UnregisterMagic[1]
                    && data[10] == UnregisterMagic[2]
                    && data[11] == UnregisterMagic[3])
                {
                    RemoveUser(userId);
                    _logger.LogInformation("Screen share user {UserId} unregistered", userId);
                    continue;
                }

                // Update endpoint (NAT may change)
                _userEndpoints[userId] = result.RemoteEndPoint;

                // Forward to all other registered users in the same channel
                foreach (var kvp in _userChannels)
                {
                    if (kvp.Key != userId && kvp.Value == channelId && _userEndpoints.TryGetValue(kvp.Key, out var endpoint))
                    {
                        try
                        {
                            await _udpServer.SendAsync(data, data.Length, endpoint);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to send video to user {UserId}", kvp.Key);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                // Windows ICMP "port unreachable" - client disconnected, safe to ignore
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Screen share relay error");
            }
        }

        _udpServer.Close();
    }

    public void RemoveUser(int userId)
    {
        _userEndpoints.TryRemove(userId, out _);
        _userChannels.TryRemove(userId, out _);
    }
}
