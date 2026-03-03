using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chaos.Client.ViewModels;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Chaos.Client.Controls;

public partial class EmojiUploaderView : UserControl
{
    private bool _isDragging;
    private System.Windows.Point _dragStart;
    private double _startOffsetX, _startOffsetY;

    private BitmapSource[]? _sourceFrames;
    private int _sourceFrameIndex;
    private DispatcherTimer? _frameTimer;

    public EmojiUploaderView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => StopAnimation();

        // Clip content to rounded corners — WPF's ClipToBounds doesn't respect CornerRadius
        ContentGrid.SizeChanged += (_, e) =>
        {
            var geo = new RectangleGeometry(
                new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 11, 11);
            geo.Freeze();
            ContentGrid.Clip = geo;
        };

        // Feed actual canvas size to ViewModel and re-center on first layout
        bool _canvasInitialized = false;
        EditorCanvas.SizeChanged += (_, e) =>
        {
            if (DataContext is EmojiUploaderViewModel vm)
            {
                vm.CanvasWidth = e.NewSize.Width;
                vm.CanvasHeight = e.NewSize.Height;
                if (!_canvasInitialized)
                {
                    _canvasInitialized = true;
                    vm.CenterImage();
                }
            }
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        StopAnimation();
        if (DataContext is not EmojiUploaderViewModel vm || !vm.IsGif) return;

        try
        {
            using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(vm.SourceBytes);
            if (image.Frames.Count <= 1) return;

            var frames = new BitmapSource[image.Frames.Count];
            int delayMs = 100;

            for (int i = 0; i < image.Frames.Count; i++)
            {
                using var frame = image.Frames.CloneFrame(i);
                frames[i] = ConvertToBitmapSource(frame);

                if (i == 0)
                {
                    var gifMeta = frame.Frames[0].Metadata.GetGifMetadata();
                    if (gifMeta.FrameDelay > 0)
                        delayMs = gifMeta.FrameDelay * 10;
                }
            }

            _sourceFrames = frames;
            _sourceFrameIndex = 0;

            _frameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
            _frameTimer.Tick += OnFrameTick;
            _frameTimer.Start();
        }
        catch { }
    }

    private static BitmapSource ConvertToBitmapSource(Image<Rgba32> image)
    {
        int w = image.Width, h = image.Height;
        var pixels = new byte[w * h * 4];
        image.CopyPixelDataTo(pixels);

        // RGBA → BGRA swap for WPF
        for (int i = 0; i < pixels.Length; i += 4)
            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4);
        bmp.Freeze();
        return bmp;
    }

    private void OnFrameTick(object? sender, EventArgs e)
    {
        if (_sourceFrames is null) return;
        _sourceFrameIndex = (_sourceFrameIndex + 1) % _sourceFrames.Length;
        SourceImageElement.Source = _sourceFrames[_sourceFrameIndex];
    }

    private void StopAnimation()
    {
        _frameTimer?.Stop();
        _frameTimer = null;
        _sourceFrames = null;
    }

    private void EditorCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not EmojiUploaderViewModel vm) return;
        _isDragging = true;
        _dragStart = e.GetPosition((IInputElement)sender);
        _startOffsetX = vm.OffsetX;
        _startOffsetY = vm.OffsetY;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void EditorCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || DataContext is not EmojiUploaderViewModel vm) return;
        var pos = e.GetPosition((IInputElement)sender);
        vm.OffsetX = _startOffsetX + (pos.X - _dragStart.X);
        vm.OffsetY = _startOffsetY + (pos.Y - _dragStart.Y);
    }

    private void EditorCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
        e.Handled = true;
    }

    private void EditorCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not EmojiUploaderViewModel vm) return;
        double factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
        double centerX = vm.CanvasWidth / 2.0;
        double centerY = vm.CanvasHeight / 2.0;
        vm.SetZoomCentered(vm.Zoom * factor, centerX, centerY);
        e.Handled = true;
    }
}
