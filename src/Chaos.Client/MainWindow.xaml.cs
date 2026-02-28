using System.Collections.Specialized;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Chaos.Client.ViewModels;
using Chaos.Shared;

namespace Chaos.Client;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll", PreserveSig = false)]
    private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int pref = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch { /* Windows 10 — rounded corners not supported, fail silently */ }
    }

    private static readonly string[] _imageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };

    // Last known bounds while the window was in Normal state.
    // Updated by SizeChanged/LocationChanged only when not maximized/minimized,
    // so they always represent the correct restore geometry.
    private double _restoreLeft, _restoreTop, _restoreWidth, _restoreHeight;

    public MainWindow()
    {
        InitializeComponent();
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico"));

        if (DataContext is MainViewModel vm)
        {
            var (left, top, width, height, maximized) = vm.GetWindowBounds();
            if (width >= 400 && height >= 300) { Width = width; Height = height; }
            if (IsPositionOnScreen(left, top, width > 0 ? width : Width))
            {
                Left = left;
                Top = top;
                WindowStartupLocation = WindowStartupLocation.Manual;
            }

            // Seed restore fields from saved values before maximizing.
            _restoreLeft   = left;
            _restoreTop    = top;
            _restoreWidth  = width >= 400 ? width : Width;
            _restoreHeight = height >= 300 ? height : Height;

            if (maximized) WindowState = WindowState.Maximized;
        }

        SizeChanged     += (_, _) => { if (WindowState == WindowState.Normal) { _restoreWidth = Width; _restoreHeight = Height; } };
        LocationChanged += (_, _) => { if (WindowState == WindowState.Normal) { _restoreLeft  = Left;  _restoreTop    = Top;    } };
        Loaded          += OnLoaded;
        StateChanged    += (_, _) => UpdateMaximizeIcon();
    }

    private void UpdateMaximizeIcon()
    {
        var icon = MaximizeButton.Template.FindName("MaximizeIcon", MaximizeButton) as Border;
        if (icon is null) return;
        icon.Margin = WindowState == WindowState.Maximized ? new Thickness(2, 0, 0, 2) : new Thickness(0);

        // When maximized, WPF moves the window off-screen by ResizeBorderThickness to hide
        // the resize handles. Compensate with an equal margin on the root element.
        var chrome = WindowChrome.GetWindowChrome(this);
        double t = WindowState == WindowState.Maximized ? chrome.ResizeBorderThickness.Left : 0;
        RootGrid.Margin = new Thickness(t);
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is MainViewModel vm)
        {
            vm.UpdateWindowBounds(_restoreLeft, _restoreTop, _restoreWidth, _restoreHeight,
                WindowState == WindowState.Maximized);
            await vm.DisposeAsync();
        }
    }

    /// <summary>
    /// Returns true if the window's title-bar centre point falls within the virtual screen,
    /// preventing the window from being restored to a disconnected monitor.
    /// </summary>
    private static bool IsPositionOnScreen(double left, double top, double width)
    {
        double cx = left + width / 2;
        return cx    > SystemParameters.VirtualScreenLeft
            && cx    < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
            && top   > SystemParameters.VirtualScreenTop
            && top   < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            SetupImagePreview(vm);

            // Intercept clicks on images in the RichTextBox before the RichTextBox
            // captures the mouse for text selection.
            MessageList.PreviewMouseLeftButtonDown += (_, e) =>
            {
                var hit = VisualTreeHelper.HitTest(MessageList, e.GetPosition(MessageList));
                if (hit?.VisualHit is Image img && img.Tag is string url)
                {
                    vm.OpenImagePreviewModal(url);
                    e.Handled = true;
                }
            };

            // Null until the ListBox is first rendered (it lives inside a Collapsed grid
            // at startup, so its control template isn't applied until IsConnected = true).
            ScrollViewer? chatScroll = null;
            bool atBottom = true;

            void AttachChatScroll()
            {
                if (chatScroll is not null) return;
                chatScroll = FindScrollViewer(MessageList);
                if (chatScroll is null) return;

                chatScroll.ScrollChanged += (_, _) =>
                    atBottom = chatScroll.VerticalOffset >= chatScroll.ScrollableHeight - 50;

                chatScroll.SizeChanged += (_, _) =>
                    { if (atBottom) chatScroll.ScrollToBottom(); };
            }

            // Rebuild the document when appearance settings that affect layout change.
            vm.Settings.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(AppSettings.MessageSpacing) or nameof(AppSettings.GroupMessages) or nameof(AppSettings.FontSize))
                    RebuildMessageDoc(vm);
            };

            // Rebuild / append to the FlowDocument and scroll to bottom on every change.
            ((INotifyCollectionChanged)vm.Messages).CollectionChanged += (_, args) =>
            {
                AttachChatScroll();
                if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems is not null)
                {
                    foreach (MessageViewModel msg in args.NewItems)
                        AppendMessageToDoc(msg);
                }
                else
                {
                    RebuildMessageDoc(vm);
                }
                chatScroll?.ScrollToBottom();
            };

            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.SelectedTextChannel) && vm.SelectedTextChannel is not null)
                {
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => MessageInput.Focus());
                    // ContextIdle fires after layout; by then the ListBox template is applied
                    // and the scroll viewer exists.
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () =>
                    {
                        AttachChatScroll();
                        chatScroll?.ScrollToBottom();
                        atBottom = true;
                    });
                }

                if (args.PropertyName == nameof(MainViewModel.SelectedSuggestionIndex))
                {
                    int idx = vm.SelectedSuggestionIndex;
                    if (idx >= 0 && idx < vm.SlashSuggestions.Count)
                        SuggestionList.ScrollIntoView(vm.SlashSuggestions[idx]);
                }

                if (args.PropertyName == nameof(MainViewModel.ActiveModal) && vm.ActiveModal is not null)
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
                    {
                        var textBox = FindFirstDescendant<TextBox>(ModalContentControl);
                        if (textBox is not null) { textBox.Focus(); textBox.SelectAll(); }
                    });
            };
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject obj)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is ScrollViewer sv) return sv;
            var result = FindScrollViewer(child);
            if (result is not null) return result;
        }
        return null;
    }

    private void LoginPanel_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel vm)
        {
            if (vm.ConnectCommand.CanExecute(null))
                vm.ConnectCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static BitmapSource BytesToBitmapSource(byte[] data)
    {
        using var ms = new MemoryStream(data);
        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }

    private void ChatInput_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            e.Effects = files.Any(f => _imageExtensions.Contains(Path.GetExtension(f).ToLower()))
                ? DragDropEffects.Copy : DragDropEffects.None;
        }
        else e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private async void ChatInput_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        var imageFile = files.FirstOrDefault(f => _imageExtensions.Contains(Path.GetExtension(f).ToLower()));
        if (imageFile is null || DataContext is not MainViewModel vm) return;
        var data = await File.ReadAllBytesAsync(imageFile);
        vm.SetPendingImage(data, Path.GetFileName(imageFile), BytesToBitmapSource(data));
    }

    private void MessageInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.ShowSlashSuggestions)
        {
            if (e.Key == Key.Down)
            {
                vm.NavigateSuggestions(1);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up)
            {
                vm.NavigateSuggestions(-1);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Tab)
            {
                int idx = vm.SelectedSuggestionIndex >= 0 ? vm.SelectedSuggestionIndex : 0;
                if (idx < vm.SlashSuggestions.Count)
                {
                    vm.SelectSuggestion(vm.SlashSuggestions[idx]);
                    MessageInput.CaretIndex = MessageInput.Text.Length;
                }
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                vm.DismissSuggestions();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter && vm.SelectedSuggestionIndex >= 0)
            {
                vm.SelectSuggestion(vm.SlashSuggestions[vm.SelectedSuggestionIndex]);
                MessageInput.CaretIndex = MessageInput.Text.Length;
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Enter && DataContext is MainViewModel vm2)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                return; // Shift+Enter inserts a newline naturally
            if (vm2.SendMessageCommand.CanExecute(null))
                vm2.SendMessageCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && Clipboard.ContainsImage())
        {
            e.Handled = true;
            var bmp = Clipboard.GetImage();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            if (DataContext is MainViewModel vm3)
                vm3.SetPendingImage(ms.ToArray(), "clipboard.png", bmp);
        }
    }

    private void ModalBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseModal();
    }

    private void Modal_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // prevent click from reaching the backdrop
    }

    private void ModalOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is MainViewModel vm && vm.IsAnyModalOpen)
        {
            vm.CloseModal();
            e.Handled = true;
        }
    }

    private static T? FindFirstDescendant<T>(DependencyObject obj) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is T found) return found;
            var result = FindFirstDescendant<T>(child);
            if (result is not null) return result;
        }
        return null;
    }

    private void SuggestionList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement el &&
            el.DataContext is SlashCommandDto cmd &&
            DataContext is MainViewModel vm)
        {
            vm.SelectSuggestion(cmd);
            MessageInput.Focus();
            MessageInput.CaretIndex = MessageInput.Text.Length;
            e.Handled = true;
        }
    }

    // ── Image Preview ─────────────────────────────────────────────────────────

    private bool _isImageZoomed;
    private bool _isPanning;
    private bool _isDragging;
    private Point _panStart;
    private double _panScrollX, _panScrollY;

    private void SetupImagePreview(MainViewModel vm)
    {
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(MainViewModel.ActiveModal)) return;
            if (vm.ActiveModal is ImagePreviewModalViewModel preview)
            {
                ImagePreviewImg.Source = new BitmapImage(new Uri(preview.ImageUrl));
                _isImageZoomed = false;
                _isPanning = false;
                SetImageZoom(false);
            }
            else
            {
                _isPanning = false;
                _isDragging = false;
                ImagePreviewPanel.ReleaseMouseCapture();
            }
        };
    }

    private void SetImageZoom(bool zoomed)
    {
        _isImageZoomed = zoomed;
        if (zoomed)
        {
            ImagePreviewImg.MaxWidth = double.PositiveInfinity;
            ImagePreviewImg.MaxHeight = double.PositiveInfinity;
            ImageActionButtons.Visibility = Visibility.Collapsed;
            double viewW = ImagePreviewScroll.ActualWidth;
            double viewH = ImagePreviewScroll.ActualHeight;
            double scale = 1.5;
            if (ImagePreviewImg.Source is BitmapSource bmp && bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
            {
                scale = Math.Max(viewW / bmp.PixelWidth, viewH / bmp.PixelHeight);
                scale = Math.Max(scale, 1.5);
            }
            ImagePreviewImg.Stretch = Stretch.None;
            ImagePreviewImg.LayoutTransform = new ScaleTransform(scale, scale);
            ImagePreviewPanel.Cursor = Cursors.ScrollAll;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                ImagePreviewScroll.ScrollToHorizontalOffset(
                    (ImagePreviewScroll.ExtentWidth - ImagePreviewScroll.ViewportWidth) / 2);
                ImagePreviewScroll.ScrollToVerticalOffset(
                    (ImagePreviewScroll.ExtentHeight - ImagePreviewScroll.ViewportHeight) / 2);
            });
        }
        else
        {
            ImagePreviewImg.MaxWidth = 700;
            ImagePreviewImg.MaxHeight = 700;
            ImagePreviewImg.Stretch = Stretch.Uniform;
            ImagePreviewImg.LayoutTransform = Transform.Identity;
            ImagePreviewPanel.Cursor = Cursors.Hand;
            ImageActionButtons.Visibility = Visibility.Visible;
        }
    }

    private void ImagePreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var pos = e.GetPosition(ImagePreviewPanel);
        var hit = VisualTreeHelper.HitTest(ImagePreviewPanel, pos);
        if (!IsDescendantOrSelf(hit?.VisualHit, ImagePreviewImg))
        {
            // Clicked empty space — close the modal
            if (DataContext is MainViewModel vm)
                vm.CloseModal();
            return;
        }
        _isPanning = true;
        _isDragging = false;
        _panStart = pos;
        _panScrollX = ImagePreviewScroll.HorizontalOffset;
        _panScrollY = ImagePreviewScroll.VerticalOffset;
        ImagePreviewPanel.CaptureMouse();
    }

    private static bool IsDescendantOrSelf(DependencyObject? element, DependencyObject target)
    {
        var current = element;
        while (current is not null)
        {
            if (current == target) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void ImagePreview_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning) return;
        var pos = e.GetPosition(ImagePreviewPanel);
        double dx = pos.X - _panStart.X;
        double dy = pos.Y - _panStart.Y;
        if (!_isDragging && (Math.Abs(dx) > 4 || Math.Abs(dy) > 4))
            _isDragging = true;
        if (_isDragging && _isImageZoomed)
        {
            ImagePreviewScroll.ScrollToHorizontalOffset(_panScrollX - dx);
            ImagePreviewScroll.ScrollToVerticalOffset(_panScrollY - dy);
        }
    }

    private void ImagePreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ImagePreviewPanel.ReleaseMouseCapture();
        bool wasPanning = _isPanning;
        _isPanning = false;
        if (wasPanning && !_isDragging)
            SetImageZoom(!_isImageZoomed);
        _isDragging = false;
    }

    private void ImagePreviewClose_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is MainViewModel vm)
            vm.CloseModal();
    }

    private void ImagePreviewSave_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (ImagePreviewImg.Source is not BitmapSource bmp) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save Image",
            Filter = "PNG Image|*.png|JPEG Image|*.jpg;*.jpeg|All Files|*.*",
            DefaultExt = ".png",
            FileName = "image"
        };
        if (dlg.ShowDialog() != true) return;

        BitmapEncoder encoder = dlg.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                dlg.FileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            ? new JpegBitmapEncoder()
            : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(dlg.FileName);
        encoder.Save(fs);
    }

    private void ImagePreviewCopy_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (ImagePreviewImg.Source is BitmapSource bmp)
            Clipboard.SetImage(bmp);
    }

    // ── Message FlowDocument ──────────────────────────────────────────────────

    private void RebuildMessageDoc(MainViewModel vm)
    {
        var doc = new FlowDocument { PagePadding = new Thickness(0) };
        MessageList.Document = doc;
        foreach (var msg in vm.Messages)
            AppendMessageToDoc(msg);
    }

    private void AppendMessageToDoc(MessageViewModel msg)
    {
        var doc       = MessageList.Document;
        var primary   = (Brush)FindResource("TextPrimaryBrush");
        var secondary = (Brush)FindResource("TextSecondaryBrush");
        var muted     = (Brush)FindResource("TextMutedBrush");
        double fontSize = (DataContext as MainViewModel)?.Settings.FontSize ?? 14;

        if (msg.ShowHeader)
        {
            var p = new Paragraph { Margin = new Thickness(16, msg.Padding.Top, 16, 0), LineHeight = double.NaN };
            p.Inlines.Add(new Run(msg.Author) { Foreground = primary, FontWeight = FontWeights.SemiBold, FontSize = fontSize + 2 });
            p.Inlines.Add(new Run($"  {msg.Timestamp:HH:mm}") { Foreground = muted, FontSize = 11 });
            doc.Blocks.Add(p);
        }

        if (!string.IsNullOrEmpty(msg.Content))
        {
            double top = msg.ShowHeader ? 2.0 : msg.Padding.Top;
            var p = new Paragraph(new Run(msg.Content) { Foreground = secondary })
                    { Margin = new Thickness(16, top, 16, 0), LineHeight = double.NaN };
            doc.Blocks.Add(p);
        }

        if (msg.HasImage && msg.ImageUrl is not null)
        {
            var imageUrl = msg.ImageUrl;
            var placeholder = new Paragraph(new Run("Loading image...") { Foreground = muted, FontStyle = FontStyles.Italic })
                              { Margin = new Thickness(16, 4, 16, 0) };
            doc.Blocks.Add(placeholder);

            _ = Task.Run(async () =>
            {
                try
                {
                    using var http = new HttpClient();
                    var bytes = await http.GetByteArrayAsync(imageUrl);
                    Dispatcher.Invoke(() =>
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.StreamSource = new MemoryStream(bytes);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze();

                        var img = new Image { Source = bmp, MaxWidth = 400, MaxHeight = 300,
                                      Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left,
                                      Cursor = Cursors.Hand, Tag = msg.ImageUrl };
                        var p = new Paragraph(new InlineUIContainer(img)) { Margin = new Thickness(16, 4, 16, 0) };
                        doc.Blocks.InsertAfter(placeholder, p);
                        doc.Blocks.Remove(placeholder);
                    });
                }
                catch
                {
                    Dispatcher.Invoke(() =>
                    {
                        ((Run)placeholder.Inlines.FirstInline).Text = "Failed to load image";
                    });
                }
            });
        }
    }
}
