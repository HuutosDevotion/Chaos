using System.Net;
using Xunit;
using System.Net.Sockets;
using Chaos.Tests.Fixtures;

namespace Chaos.Tests;

[Collection("ChaosServer")]
public class VoiceRelayTests
{
    // The fixture is required to ensure the server (and VoiceRelay on UDP:9000) is running.
    public VoiceRelayTests(ChaosServerFixture _) { }

    private static readonly IPEndPoint VoiceServer = new(IPAddress.Loopback, 9000);

    private static byte[] MakeRegistrationPacket(int userId, int channelId)
    {
        var packet = new byte[12];
        BitConverter.TryWriteBytes(packet.AsSpan(0, 4), userId);
        BitConverter.TryWriteBytes(packet.AsSpan(4, 4), channelId);
        "RGST"u8.CopyTo(packet.AsSpan(8));
        return packet;
    }

    private static byte[] MakeAudioPacket(int userId, int channelId, byte[] audio)
    {
        var packet = new byte[8 + audio.Length];
        BitConverter.TryWriteBytes(packet.AsSpan(0, 4), userId);
        BitConverter.TryWriteBytes(packet.AsSpan(4, 4), channelId);
        audio.CopyTo(packet.AsSpan(8));
        return packet;
    }

    [Fact]
    public async Task Registration_UserCanSendAudioAfterRegistering()
    {
        using var user1 = new UdpClient(0);
        using var user2 = new UdpClient(0);

        const int userId1 = 10001, userId2 = 10002, channelId = 200;

        await user1.SendAsync(MakeRegistrationPacket(userId1, channelId), VoiceServer);
        await user2.SendAsync(MakeRegistrationPacket(userId2, channelId), VoiceServer);
        await Task.Delay(50);

        var audio = new byte[] { 1, 2, 3, 4, 5 };
        await user1.SendAsync(MakeAudioPacket(userId1, channelId, audio), VoiceServer);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var result = await user2.ReceiveAsync(cts.Token);

        Assert.True(result.Buffer.Length > 0);
    }

    [Fact]
    public async Task AudioPacket_ForwardedToOtherUserInSameChannel()
    {
        using var user1 = new UdpClient(0);
        using var user2 = new UdpClient(0);

        const int userId1 = 10003, userId2 = 10004, channelId = 201;

        await user1.SendAsync(MakeRegistrationPacket(userId1, channelId), VoiceServer);
        await user2.SendAsync(MakeRegistrationPacket(userId2, channelId), VoiceServer);
        await Task.Delay(50);

        var audio = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        var sentPacket = MakeAudioPacket(userId1, channelId, audio);
        await user1.SendAsync(sentPacket, VoiceServer);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var result = await user2.ReceiveAsync(cts.Token);

        Assert.Equal(sentPacket, result.Buffer);
    }

    [Fact]
    public async Task AudioPacket_NotForwardedToUserInDifferentChannel()
    {
        using var user1 = new UdpClient(0);
        using var user2 = new UdpClient(0);

        const int userId1 = 10005, userId2 = 10006;

        await user1.SendAsync(MakeRegistrationPacket(userId1, 300), VoiceServer);
        await user2.SendAsync(MakeRegistrationPacket(userId2, 301), VoiceServer);
        await Task.Delay(50);

        await user1.SendAsync(MakeAudioPacket(userId1, 300, new byte[] { 1, 2, 3, 4 }), VoiceServer);

        using var cts = new CancellationTokenSource(500);
        try
        {
            await user2.ReceiveAsync(cts.Token);
            Assert.Fail("Should not have received audio from a different channel");
        }
        catch (OperationCanceledException) { /* expected */ }
    }

    [Fact]
    public async Task AudioPacket_PayloadIntegrityPreserved()
    {
        using var user1 = new UdpClient(0);
        using var user2 = new UdpClient(0);

        const int userId1 = 10007, userId2 = 10008, channelId = 202;

        await user1.SendAsync(MakeRegistrationPacket(userId1, channelId), VoiceServer);
        await user2.SendAsync(MakeRegistrationPacket(userId2, channelId), VoiceServer);
        await Task.Delay(50);

        // 40 ms @ 16 kHz 16-bit mono = 16000 * 0.04 * 2 = 1280 bytes
        var audio = new byte[1280];
        for (var i = 0; i < audio.Length; i++)
            audio[i] = (byte)(i % 256);

        var sentPacket = MakeAudioPacket(userId1, channelId, audio);
        await user1.SendAsync(sentPacket, VoiceServer);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var result = await user2.ReceiveAsync(cts.Token);

        Assert.Equal(sentPacket, result.Buffer);
    }

    [Fact]
    public async Task AudioPacket_NotReflectedBackToSender()
    {
        using var sender = new UdpClient(0);
        using var receiver = new UdpClient(0);

        const int senderId = 10009, receiverId = 10010, channelId = 203;

        await sender.SendAsync(MakeRegistrationPacket(senderId, channelId), VoiceServer);
        await receiver.SendAsync(MakeRegistrationPacket(receiverId, channelId), VoiceServer);
        await Task.Delay(50);

        var packet = MakeAudioPacket(senderId, channelId, new byte[] { 0xFF, 0xFE, 0xFD });
        await sender.SendAsync(packet, VoiceServer);

        // Drain the receiver first — this confirms the relay processed the packet
        using var receiverCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var receiverResult = await receiver.ReceiveAsync(receiverCts.Token);
        Assert.True(receiverResult.Buffer.Length > 0);

        // Sender must NOT have received its own packet
        using var senderCts = new CancellationTokenSource(500);
        try
        {
            await sender.ReceiveAsync(senderCts.Token);
            Assert.Fail("Sender should not receive its own packet");
        }
        catch (OperationCanceledException) { /* expected */ }
    }
}
