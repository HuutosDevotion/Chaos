using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Chaos.Client;

public partial class StreamViewerWindow : Window
{
    private bool _isPinned;
    private bool _isFullscreen;
    private WindowState _prevState;
    private WindowStyle _prevStyle;
    private ResizeMode _prevResize;

    public event Action? CloseRequested;
    public event Action? PopInRequested;

    public StreamViewerWindow()
    {
        InitializeComponent();
        VideoFrame.MouseLeftButtonDown += VideoFrame_DoubleClick;
    }

    public void SetStreamerName(string name)
    {
        StreamerName.Text = $"LIVE - {name}";
    }

    public void UpdateFrame(BitmapSource frame)
    {
        VideoFrame.Source = frame;
        NoStreamText.Visibility = Visibility.Collapsed;
    }

    private void VideoFrame_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            ToggleFullscreen();
    }

    private void FullscreenBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            WindowStyle = _prevStyle;
            ResizeMode = _prevResize;
            WindowState = _prevState;
            TopBar.Visibility = Visibility.Visible;
            _isFullscreen = false;
        }
        else
        {
            _prevState = WindowState;
            _prevStyle = WindowStyle;
            _prevResize = ResizeMode;
            TopBar.Visibility = Visibility.Collapsed;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            _isFullscreen = true;
        }
    }

    private void PinBtn_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        Topmost = _isPinned;
        PinBtn.Content = _isPinned ? "\U0001F4CD" : "\U0001F4CC";
        PinBtn.ToolTip = _isPinned ? "Unpin" : "Pin on top";
    }

    private void PopInBtn_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        PopInRequested?.Invoke();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        CloseRequested?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isFullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
