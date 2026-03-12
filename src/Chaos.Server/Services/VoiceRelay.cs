using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Chaos.Server.Services;

public class VoiceRelay : BackgroundService
{
    private readonly ILogger<VoiceRelay> _logger;
    private UdpClient? _udpServer;
    private const int Port = 9000;

    // Header: 4 bytes userId + 4 bytes channelId, then audio data
    // Registration packet: userId + channelId + 4 bytes magic "RGST"
    private static readonly byte[] RegistrationMagic = "RGST"u8.ToArray();

    // Maps usedId -> endpoint
    private readonly ConcurrentDictionary<int, IPEndPoint> _userEndpoints = new();
    // Maps userId -> channelId
    private readonly ConcurrentDictionary<int, int> _userChannels = new();

    public VoiceRelay(ILogger<VoiceRelay> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _udpServer = new UdpClient(Port);
        _logger.LogInformation("Voice relay listening on UDP port {Port}", Port);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udpServer.ReceiveAsync(stoppingToken);
                var data = result.Buffer;

                if (data.Length < 12)
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
                    _logger.LogInformation("Voice user {UserId} registered from {Endpoint} in channel {Channel}",
                        userId, result.RemoteEndPoint, channelId);
                    continue;
                }

                // Update endpoint (NAT may change)
                _userEndpoints[userId] = result.RemoteEndPoint;
                _userChannels[userId] = channelId;

                // Forward to all other users in the same channel
                var audioData = data; // Forward entire packet including header so receiver knows who sent it
                foreach (var kvp in _userChannels)
                {
                    if (kvp.Key != userId && kvp.Value == channelId && _userEndpoints.TryGetValue(kvp.Key, out var endpoint))
                    {
                        try
                        {
                            await _udpServer.SendAsync(audioData, audioData.Length, endpoint);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to send audio to user {UserId}", kvp.Key);
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
                _logger.LogError(ex, "Voice relay error");
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
