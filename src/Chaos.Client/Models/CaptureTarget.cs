using System.Windows.Media.Imaging;

namespace Chaos.Client.Models;

public enum CaptureTargetType
{
    Screen,
    Window
}

public class CaptureTarget
{
    public CaptureTargetType Type { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public IntPtr Handle { get; init; }
    public int ScreenIndex { get; init; }
    public int Left { get; init; }
    public int Top { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public BitmapSource? Thumbnail { get; init; }
}
