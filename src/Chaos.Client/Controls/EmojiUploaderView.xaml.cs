using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chaos.Client.ViewModels;

namespace Chaos.Client.Controls;

public partial class EmojiUploaderView : UserControl
{
    private bool _isDragging;
    private Point _dragStart;
    private double _startOffsetX, _startOffsetY;

    private BitmapFrame[]? _sourceFrames;
    private int _sourceFrameIndex;
    private DispatcherTimer? _frameTimer;

    public EmojiUploaderView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => StopAnimation();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        StopAnimation();
        if (DataContext is not EmojiUploaderViewModel vm || !vm.IsGif) return;

        try
        {
            var decoder = new GifBitmapDecoder(
                new MemoryStream(vm.SourceBytes),
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count <= 1) return;

            _sourceFrames = decoder.Frames.ToArray();
            _sourceFrameIndex = 0;

            // Read frame delay from GIF metadata (in 10ms units), default to 100ms
            int delayMs = 100;
            var metadata = decoder.Frames[0].Metadata as BitmapMetadata;
            if (metadata != null)
            {
                try
                {
                    var delay = metadata.GetQuery("/grctlext/Delay");
                    if (delay is ushort d && d > 0)
                        delayMs = d * 10;
                }
                catch { }
            }

            _frameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
            _frameTimer.Tick += OnFrameTick;
            _frameTimer.Start();
        }
        catch { }
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
        double center = EmojiUploaderViewModel.CanvasSize / 2.0;
        vm.SetZoomCentered(vm.Zoom * factor, center, center);
        e.Handled = true;
    }
}
