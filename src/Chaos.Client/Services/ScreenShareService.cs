using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chaos.Client.Models;
using Chaos.Shared;

namespace Chaos.Client.Services;

public class ScreenShareService : IDisposable
{
    private UdpClient? _udpClient;
    private CancellationTokenSource? _captureCts;
    private CancellationTokenSource? _receiveCts;
    private Thread? _captureThread;
    private Task? _receiveTask;

    private int _videoUserId;
    private int _channelId;
    private IPEndPoint? _serverEndpoint;

    private volatile bool _isStreaming;
    private volatile bool _isWatching;

    private CaptureTarget? _captureTarget;

    private static readonly byte[] RegistrationMagic = "VDEO"u8.ToArray();
    private static readonly byte[] UnregisterMagic = "BYE!"u8.ToArray();

    // Quality presets: (width, height, fps, jpegQuality)
    private static readonly Dictionary<StreamQuality, (int W, int H, int Fps, int Q)> Presets = new()
    {
        [StreamQuality.Low] = (854, 480, 10, 40),
        [StreamQuality.Medium] = (1280, 720, 15, 55),
        [StreamQuality.High] = (1920, 1080, 30, 70),
    };

    private StreamQuality _quality = StreamQuality.Medium;
    private int _frameId;

    // Adaptive quality tracking
    private int _framesComplete;
    private int _framesDropped;
    private DateTime _lastQualityCheck = DateTime.UtcNow;

    public event Action<int, BitmapSource>? FrameReceived; // senderId, frame
    public event Action<string>? Error;

    public bool IsStreaming => _isStreaming;
    public bool IsWatching => _isWatching;

    public void StartStreaming(string serverHost, int serverPort, int videoUserId, int channelId, StreamQuality quality, CaptureTarget target)
    {
        Stop();

        _videoUserId = videoUserId;
        _channelId = channelId;
        _quality = quality;
        _frameId = 0;
        _captureTarget = target;

        var ip = ResolveHost(serverHost);
        _serverEndpoint = new IPEndPoint(ip, serverPort);

        _udpClient = new UdpClient(_serverEndpoint.AddressFamily);
        _udpClient.Client.SendBufferSize = 1024 * 1024;

        // Register with relay
        var regPacket = new byte[12];
        BitConverter.GetBytes(videoUserId).CopyTo(regPacket, 0);
        BitConverter.GetBytes(channelId).CopyTo(regPacket, 4);
        RegistrationMagic.CopyTo(regPacket, 8);
        _udpClient.Send(regPacket, regPacket.Length, _serverEndpoint);

        _captureCts = new CancellationTokenSource();
        _captureThread = new Thread(() => CaptureLoop(_captureCts.Token));
        _captureThread.SetApartmentState(ApartmentState.STA);
        _captureThread.IsBackground = true;
        _captureThread.Start();
        _isStreaming = true;
    }

    public void StartWatching(string serverHost, int serverPort, int videoUserId, int channelId)
    {
        if (_isWatching) return;

        // If we're streaming, self-preview comes from the capture loop — no need for a receive loop
        if (_isStreaming) return;

        _videoUserId = videoUserId;
        _channelId = channelId;

        var ip = ResolveHost(serverHost);
        _serverEndpoint = new IPEndPoint(ip, serverPort);

        // Always create a fresh UDP client for viewers to ensure clean relay registration
        _udpClient?.Dispose();

        _udpClient = new UdpClient(_serverEndpoint.AddressFamily);
        _udpClient.Client.ReceiveBufferSize = 1024 * 1024;

        // Register to receive frames
        var regPacket = new byte[12];
        BitConverter.GetBytes(videoUserId).CopyTo(regPacket, 0);
        BitConverter.GetBytes(channelId).CopyTo(regPacket, 4);
        RegistrationMagic.CopyTo(regPacket, 8);
        _udpClient.Send(regPacket, regPacket.Length, _serverEndpoint);

        _receiveCts = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveLoop(_receiveCts.Token));
        _isWatching = true;
    }

    private void CaptureLoop(CancellationToken ct)
    {
        var target = _captureTarget!;

        while (!ct.IsCancellationRequested)
        {
            // Use current quality (may change via adaptive adjustment)
            var preset = Presets[_quality];
            var frameInterval = TimeSpan.FromMilliseconds(1000.0 / preset.Fps);

            // Check if window target is still alive
            if (target.Type == CaptureTargetType.Window && !IsWindow(target.Handle))
            {
                SendUnregister();
                _isStreaming = false;
                Error?.Invoke("Captured window was closed.");
                return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var jpegData = CaptureTargetAsJpeg(target, preset.W, preset.H, preset.Q);

                if (jpegData is null || jpegData.Length == 0)
                    continue;

                _frameId++;

                // Packet format:
                //   Single:     [4B userId][4B channelId][4B frameId][1B flags=0x00][jpegData]
                //   Fragmented: [4B userId][4B channelId][4B frameId][1B flags=0x01][2B chunkIdx][2B totalChunks][chunkData]
                const int singleHeaderSize = 13;
                const int fragHeaderSize = 17;
                const int maxPayload = 1200; // MTU-safe for all networks

                if (jpegData.Length + singleHeaderSize <= maxPayload)
                {
                    // Single packet (small frames only — rare at reasonable quality)
                    var packet = new byte[singleHeaderSize + jpegData.Length];
                    BitConverter.GetBytes(_videoUserId).CopyTo(packet, 0);
                    BitConverter.GetBytes(_channelId).CopyTo(packet, 4);
                    BitConverter.GetBytes(_frameId).CopyTo(packet, 8);
                    packet[12] = 0x00;
                    Buffer.BlockCopy(jpegData, 0, packet, singleHeaderSize, jpegData.Length);

                    _udpClient?.Send(packet, packet.Length, _serverEndpoint);
                }
                else
                {
                    int chunkDataSize = maxPayload - fragHeaderSize; // 1183 bytes per fragment
                    int totalChunks = (jpegData.Length + chunkDataSize - 1) / chunkDataSize;

                    for (int i = 0; i < totalChunks && !ct.IsCancellationRequested; i++)
                    {
                        int offset = i * chunkDataSize;
                        int len = Math.Min(chunkDataSize, jpegData.Length - offset);

                        var packet = new byte[fragHeaderSize + len];
                        BitConverter.GetBytes(_videoUserId).CopyTo(packet, 0);
                        BitConverter.GetBytes(_channelId).CopyTo(packet, 4);
                        BitConverter.GetBytes(_frameId).CopyTo(packet, 8);
                        packet[12] = 0x01;
                        BitConverter.GetBytes((short)i).CopyTo(packet, 13);
                        BitConverter.GetBytes((short)totalChunks).CopyTo(packet, 15);
                        Buffer.BlockCopy(jpegData, offset, packet, fragHeaderSize, len);

                        _udpClient?.Send(packet, packet.Length, _serverEndpoint);
                    }
                }

                _framesComplete++;

                // Local self-preview
                var preview = DecodeJpeg(jpegData);
                if (preview is not null)
                    FrameReceived?.Invoke(_videoUserId, preview);

                // Adaptive quality check every 5 seconds
                if ((DateTime.UtcNow - _lastQualityCheck).TotalSeconds >= 5)
                    AdjustQuality();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Error?.Invoke($"Capture error: {ex.Message}");
            }

            // Frame rate limiter
            var elapsed = sw.Elapsed;
            if (elapsed < frameInterval)
                Thread.Sleep(frameInterval - elapsed);
        }
    }

    private void AdjustQuality()
    {
        var total = _framesComplete + _framesDropped;
        _lastQualityCheck = DateTime.UtcNow;
        if (total == 0) return;

        var lossRate = (double)_framesDropped / total;
        if (lossRate > 0.20 && _quality > StreamQuality.Low)
            _quality = _quality - 1;
        else if (lossRate < 0.05 && _quality < StreamQuality.High)
            _quality = _quality + 1;

        _framesComplete = 0;
        _framesDropped = 0;
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        // Buffer for fragmented frames: frameId -> (chunks[], received count, total)
        var frameBuffer = new Dictionary<int, (byte[][] Chunks, int Received, int Total)>();

        while (!ct.IsCancellationRequested && _udpClient is not null)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(ct);
                var data = result.Buffer;

                if (data.Length < 13)
                    continue;

                int senderId = BitConverter.ToInt32(data, 0);
                int frameId = BitConverter.ToInt32(data, 8);
                byte flags = data[12];

                byte[] jpegData;

                if (flags == 0x01 && data.Length >= 17)
                {
                    // Fragmented frame
                    short chunkIdx = BitConverter.ToInt16(data, 13);
                    short totalChunks = BitConverter.ToInt16(data, 15);
                    int payloadLen = data.Length - 17;

                    if (!frameBuffer.TryGetValue(frameId, out var entry))
                    {
                        entry = (new byte[totalChunks][], 0, totalChunks);
                        frameBuffer[frameId] = entry;
                    }

                    if (chunkIdx < entry.Chunks.Length && entry.Chunks[chunkIdx] is null)
                    {
                        entry.Chunks[chunkIdx] = new byte[payloadLen];
                        Buffer.BlockCopy(data, 17, entry.Chunks[chunkIdx], 0, payloadLen);
                        entry.Received++;
                        frameBuffer[frameId] = entry;
                    }

                    // Clean stale incomplete frames — count as dropped for adaptive quality
                    var staleIds = frameBuffer.Keys.Where(k => k < frameId - 10).ToList();
                    foreach (var id in staleIds)
                    {
                        _framesDropped++;
                        frameBuffer.Remove(id);
                    }

                    if (entry.Received < entry.Total)
                        continue;

                    // Reassemble
                    using var ms = new MemoryStream();
                    foreach (var chunk in entry.Chunks)
                    {
                        if (chunk is not null)
                            ms.Write(chunk, 0, chunk.Length);
                    }
                    jpegData = ms.ToArray();
                    frameBuffer.Remove(frameId);
                }
                else
                {
                    // Single packet frame (flags == 0x00)
                    jpegData = new byte[data.Length - 13];
                    Buffer.BlockCopy(data, 13, jpegData, 0, jpegData.Length);
                }

                // Decode JPEG to BitmapSource on background, then raise event
                var bitmapSource = DecodeJpeg(jpegData);
                if (bitmapSource is not null)
                    FrameReceived?.Invoke(senderId, bitmapSource);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private static byte[]? CaptureTargetAsJpeg(CaptureTarget target, int targetWidth, int targetHeight, int quality)
    {
        try
        {
            if (target.Type == CaptureTargetType.Screen)
                return CaptureScreenRegionAsJpeg(target.Left, target.Top, target.Width, target.Height, targetWidth, targetHeight, quality);
            else
                return CaptureWindowAsJpeg(target.Handle, targetWidth, targetHeight, quality);
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? CaptureScreenRegionAsJpeg(int srcLeft, int srcTop, int srcWidth, int srcHeight, int targetWidth, int targetHeight, int quality)
    {
        IntPtr hdc = GetDC(IntPtr.Zero);
        IntPtr memDC = CreateCompatibleDC(hdc);
        IntPtr hBitmap = CreateCompatibleBitmap(hdc, srcWidth, srcHeight);
        IntPtr oldBitmap = SelectObject(memDC, hBitmap);
        try
        {
            BitBlt(memDC, 0, 0, srcWidth, srcHeight, hdc, srcLeft, srcTop, SRCCOPY);
            SelectObject(memDC, oldBitmap);

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            return ScaleAndEncode(source, srcWidth, srcHeight, targetWidth, targetHeight, quality);
        }
        finally
        {
            DeleteObject(hBitmap);
            DeleteDC(memDC);
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    private static byte[]? CaptureWindowAsJpeg(IntPtr hWnd, int targetWidth, int targetHeight, int quality)
    {
        if (!GetWindowRect(hWnd, out RECT rect))
            return null;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return null;

        IntPtr hdc = GetDC(IntPtr.Zero);
        IntPtr memDC = CreateCompatibleDC(hdc);
        IntPtr hBitmap = CreateCompatibleBitmap(hdc, width, height);
        IntPtr oldBitmap = SelectObject(memDC, hBitmap);
        try
        {
            PrintWindow(hWnd, memDC, PW_RENDERFULLCONTENT);
            SelectObject(memDC, oldBitmap);

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            return ScaleAndEncode(source, width, height, targetWidth, targetHeight, quality);
        }
        finally
        {
            DeleteObject(hBitmap);
            DeleteDC(memDC);
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    private static byte[] ScaleAndEncode(BitmapSource source, int srcWidth, int srcHeight, int targetWidth, int targetHeight, int quality)
    {
        double scaleX = (double)targetWidth / srcWidth;
        double scaleY = (double)targetHeight / srcHeight;
        double scale = Math.Min(scaleX, scaleY);

        BitmapSource finalSource;
        if (Math.Abs(scale - 1.0) > 0.01)
        {
            var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            finalSource = scaled;
        }
        else
        {
            finalSource = source;
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(finalSource));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static BitmapSource? DecodeJpeg(byte[] jpegData)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = new MemoryStream(jpegData);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public void StopStreaming()
    {
        _captureCts?.Cancel();
        _isStreaming = false;
    }

    public void StopWatching()
    {
        _receiveCts?.Cancel();
        _isWatching = false;
    }

    public void Stop()
    {
        _captureCts?.Cancel();
        _receiveCts?.Cancel();
        _isStreaming = false;
        _isWatching = false;

        // Wait for background work to finish before disposing shared resources
        _captureThread?.Join(TimeSpan.FromSeconds(2));
        _captureThread = null;

        SendUnregister();
        _udpClient?.Dispose();
        _udpClient = null;

        _captureCts?.Dispose();
        _captureCts = null;
        _receiveCts?.Dispose();
        _receiveCts = null;
        _captureTarget = null;
    }

    private void SendUnregister()
    {
        if (_udpClient is null || _serverEndpoint is null) return;
        try
        {
            var packet = new byte[12];
            BitConverter.GetBytes(_videoUserId).CopyTo(packet, 0);
            BitConverter.GetBytes(_channelId).CopyTo(packet, 4);
            UnregisterMagic.CopyTo(packet, 8);
            _udpClient.Send(packet, packet.Length, _serverEndpoint);
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
    }

    private static IPAddress ResolveHost(string host)
    {
        if (IPAddress.TryParse(host, out var parsed))
            return parsed;
        var addresses = Dns.GetHostAddresses(host);
        return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.First();
    }

    // Win32 P/Invoke for screen capture
    private const int SRCCOPY = 0x00CC0020;
    private const int PW_RENDERFULLCONTENT = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int srcX, int srcY, int rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
}
