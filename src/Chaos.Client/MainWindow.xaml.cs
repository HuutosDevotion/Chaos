using System.Collections.Specialized;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Chaos.Client.Behaviors;
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
        MessageInput.SelectionChanged += (_, _) => UpdateFormatButtonStates();

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
                    ApplySuggestion(vm, vm.SlashSuggestions[idx]);
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
                ApplySuggestion(vm, vm.SlashSuggestions[vm.SelectedSuggestionIndex]);
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Enter && DataContext is MainViewModel vm2)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                if (TryHandleListEnter())
                    e.Handled = true;
                return; // handled by list logic, or let TextBox insert newline naturally
            }
            if (vm2.SendMessageCommand.CanExecute(null))
                vm2.SendMessageCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            int pos = MessageInput.SelectionStart;
            int len = MessageInput.SelectionLength;
            MessageInput.Text = MessageInput.Text.Remove(pos, len).Insert(pos, "    ");
            MessageInput.SelectionStart  = pos + 4;
            MessageInput.SelectionLength = 0;
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
            ApplySuggestion(vm, cmd);
            e.Handled = true;
        }
    }

    private void ApplySuggestion(MainViewModel vm, SlashCommandDto cmd)
    {
        vm.SelectSuggestion(cmd);
        MessageInput.CaretIndex = MessageInput.Text.Length;
        MessageInput.Focus();
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

    // ── Formatting toolbar handlers ────────────────────────────────────────────
    // Buttons insert markdown syntax into the TextBox rather than applying WPF
    // text properties, keeping the input fast and the content plain text.

    private void FormatBold_Click(object sender, RoutedEventArgs e) => InsertMarkdownAround("**");
    private void FormatItalic_Click(object sender, RoutedEventArgs e) => InsertMarkdownAround("*");
    private void FormatUnderline_Click(object sender, RoutedEventArgs e) => InsertMarkdownAround("__");
    private void FormatStrikethrough_Click(object sender, RoutedEventArgs e) => InsertMarkdownAround("~~");

    // Regex patterns for 2-char delimiter spans (bold, underline, strikethrough).
    // (?!\s) / (?<!\s) mirror CommonMark: ** must touch non-whitespace on both sides.
    // This prevents "** text **" from being swallowed as bold, leaving lone * chars
    // available for the italic scan.
    private static readonly Regex BoldSpan      = new(@"\*\*(?!\s)(.*?)(?<!\s)\*\*", RegexOptions.Compiled);
    private static readonly Regex UnderlineSpan = new(@"__(.*?)__",     RegexOptions.Compiled);
    private static readonly Regex StrikeSpan    = new(@"~~(.*?)~~",     RegexOptions.Compiled);

    private void UpdateFormatButtonStates()
    {
        string text = MessageInput.Text;
        int pos = MessageInput.SelectionStart;
        TooltipHelper.SetIsActive(BoldButton,          IsCursorInSpan(text, pos, BoldSpan, 2));
        TooltipHelper.SetIsActive(ItalicButton,        IsCursorInItalicSpan(text, pos));
        TooltipHelper.SetIsActive(UnderlineButton,     IsCursorInSpan(text, pos, UnderlineSpan, 2));
        TooltipHelper.SetIsActive(StrikethroughButton, IsCursorInSpan(text, pos, StrikeSpan, 2));
    }

    // Returns true when `pos` falls inside the content region of any span matched by `pattern`.
    // markerLen is the length of the opening/closing delimiter (** = 2, __ = 2, ~~ = 2).
    private static bool IsCursorInSpan(string text, int pos, Regex pattern, int markerLen)
    {
        foreach (Match m in pattern.Matches(text))
            if (pos >= m.Index + markerLen && pos <= m.Index + m.Length - markerLen)
                return true;
        return false;
    }

    // Italic uses a manual scan instead of regex because the * vs ** ambiguity breaks
    // lookahead/lookbehind — in particular, adjacent ** (empty italic or bold boundary)
    // causes the regex to fail. This approach masks all bold-span positions first, then
    // scans for lone * pairs in the remaining text.
    private static bool IsCursorInItalicSpan(string text, int pos)
    {
        if (text.Length == 0) return false;

        // Mark every character position that belongs to a bold span
        var inBold = new bool[text.Length];
        foreach (Match m in BoldSpan.Matches(text))
            for (int i = m.Index; i < m.Index + m.Length; i++)
                inBold[i] = true;

        // Scan for lone * pairs (opening and closing) outside bold spans
        int openAt = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '*' || inBold[i]) continue;
            if (openAt < 0)
                openAt = i;                      // found opening *
            else
            {
                if (pos >= openAt + 1 && pos <= i) return true;
                openAt = -1;                     // found closing *, reset
            }
        }
        return false;
    }

    private void InsertMarkdownAround(string marker)
    {
        int start = MessageInput.SelectionStart;
        int len   = MessageInput.SelectionLength;
        string sel = MessageInput.SelectedText;
        MessageInput.Text = MessageInput.Text.Remove(start, len)
                                             .Insert(start, marker + sel + marker);
        MessageInput.SelectionStart  = start + marker.Length;
        MessageInput.SelectionLength = sel.Length;
        MessageInput.Focus();
    }

    private void FormatBullets_Click(object sender, RoutedEventArgs e) =>
        PrefixSelectedLines(i => "- ");

    private void FormatNumbering_Click(object sender, RoutedEventArgs e) =>
        PrefixSelectedLines(i => $"{i + 1}. ");

    private void FormatIndent_Click(object sender, RoutedEventArgs e) =>
        PrefixSelectedLines(i => "    ");

    private void FormatOutdent_Click(object sender, RoutedEventArgs e)
    {
        int origStart  = MessageInput.SelectionStart;
        int origLength = MessageInput.SelectionLength;

        GetSelectedLineRegion(out int lineStart, out int lineEnd);
        string region = MessageInput.Text[lineStart..lineEnd];
        string[] lines = region.Split('\n');
        string[] newLines = lines.Select(l =>
            l.StartsWith("    ") ? l[4..] : l.TrimStart(' ')).ToArray();
        string newRegion = string.Join("\n", newLines);

        MessageInput.Text = MessageInput.Text[..lineStart] + newRegion + MessageInput.Text[lineEnd..];

        if (origLength == 0)
        {
            int spacesRemoved = lines[0].Length - newLines[0].Length;
            MessageInput.SelectionStart  = Math.Max(lineStart, origStart - spacesRemoved);
            MessageInput.SelectionLength = 0;
        }
        else
        {
            MessageInput.SelectionStart  = lineStart;
            MessageInput.SelectionLength = newRegion.Length;
        }
        MessageInput.Focus();
    }

    private static readonly Regex NumberedListPrefix = new(@"^(\d+)\. ", RegexOptions.Compiled);

    private bool TryHandleListEnter()
    {
        string text = MessageInput.Text;
        int    pos  = MessageInput.SelectionStart;

        int lineStart = pos == 0 ? 0 : text.LastIndexOf('\n', pos - 1) + 1;
        int lineEnd   = text.IndexOf('\n', lineStart);
        if (lineEnd < 0) lineEnd = text.Length;
        string line = text[lineStart..lineEnd];

        // ── Bullet list ──────────────────────────────────────────
        if (line.StartsWith("- "))
        {
            bool empty = line.Length == 2;
            if (empty)
            {
                MessageInput.Text = text.Remove(lineStart, 2);
                MessageInput.SelectionStart = lineStart;
            }
            else
            {
                string insert = "\n- ";
                MessageInput.Text = text.Insert(pos, insert);
                MessageInput.SelectionStart = pos + insert.Length;
            }
            return true;
        }

        // ── Numbered list ─────────────────────────────────────────
        var m = NumberedListPrefix.Match(line);
        if (m.Success)
        {
            int    num    = int.Parse(m.Groups[1].Value);
            bool   empty  = line.Length == m.Length;
            string prefix = $"{num}. ";
            if (empty)
            {
                MessageInput.Text = text.Remove(lineStart, prefix.Length);
                MessageInput.SelectionStart = lineStart;
            }
            else
            {
                string insert = $"\n{num + 1}. ";
                MessageInput.Text = text.Insert(pos, insert);
                MessageInput.SelectionStart = pos + insert.Length;
            }
            return true;
        }

        return false;
    }

    private void PrefixSelectedLines(Func<int, string> prefixFor)
    {
        int origStart  = MessageInput.SelectionStart;
        int origLength = MessageInput.SelectionLength;

        GetSelectedLineRegion(out int lineStart, out int lineEnd);
        string region = MessageInput.Text[lineStart..lineEnd];
        string[] lines = region.Split('\n');
        string newRegion = string.Join("\n", lines.Select((l, i) => prefixFor(i) + l));

        MessageInput.Text = MessageInput.Text[..lineStart] + newRegion + MessageInput.Text[lineEnd..];

        if (origLength == 0)
        {
            // No selection: nudge cursor forward by the length of the added prefix
            MessageInput.SelectionStart  = origStart + prefixFor(0).Length;
            MessageInput.SelectionLength = 0;
        }
        else
        {
            // Had selection: keep entire modified region selected (existing behaviour)
            MessageInput.SelectionStart  = lineStart;
            MessageInput.SelectionLength = newRegion.Length;
        }
        MessageInput.Focus();
    }

    private void GetSelectedLineRegion(out int lineStart, out int lineEnd)
    {
        string text = MessageInput.Text;
        int selStart = MessageInput.SelectionStart;
        int selEnd   = selStart + MessageInput.SelectionLength;
        lineStart = selStart == 0 ? 0 : text.LastIndexOf('\n', selStart - 1) + 1;
        int nl = selEnd < text.Length ? text.IndexOf('\n', selEnd) : -1;
        lineEnd = nl >= 0 ? nl : text.Length;
    }

    private void ApplyToLineRegion(int lineStart, int lineEnd, string newRegion)
    {
        MessageInput.Text = MessageInput.Text[..lineStart] + newRegion + MessageInput.Text[lineEnd..];
        MessageInput.SelectionStart  = lineStart;
        MessageInput.SelectionLength = newRegion.Length;
        MessageInput.Focus();
    }

    private void InsertHyperlink_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        string display = MessageInput.SelectedText;
        int start = MessageInput.SelectionStart;
        int len   = MessageInput.SelectionLength;

        vm.OpenModal(new HyperlinkModalViewModel(
            initialUrl:     display.StartsWith("http") ? display : "https://",
            initialDisplay: display.StartsWith("http") ? string.Empty : display,
            confirm: (url, displayText) =>
            {
                string md = string.IsNullOrEmpty(displayText) ? url : $"[{displayText}]({url})";
                MessageInput.Text = MessageInput.Text.Remove(start, len).Insert(start, md);
                MessageInput.CaretIndex = start + md.Length;
                vm.CloseModal();
                MessageInput.Focus();
            },
            cancel: () =>
            {
                vm.CloseModal();
                MessageInput.Focus();
            }));
    }

    private async void PickImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Image",
            Filter = "Images|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.webp"
        };
        if (dlg.ShowDialog() != true) return;
        var data = await File.ReadAllBytesAsync(dlg.FileName);
        if (DataContext is MainViewModel vm)
            vm.SetPendingImage(data, Path.GetFileName(dlg.FileName), BytesToBitmapSource(data));
        MessageInput.Focus();
    }

    private bool _previewMode;

    private void TogglePreview_Click(object sender, RoutedEventArgs e)
    {
        _previewMode = !_previewMode;
        if (_previewMode)
        {
            var secondary = (Brush)FindResource("TextSecondaryBrush");
            var accent    = (Brush)FindResource("AccentBlueBrush");
            MessagePreview.Document = MarkdownRenderer.Render(
                MessageInput.Text, secondary, accent);
            MessageInput.Visibility   = Visibility.Collapsed;
            MessagePreview.Visibility = Visibility.Visible;
            FormattingToolbar.Visibility = Visibility.Visible; // keep toolbar visible while previewing
            PreviewToggleButton.Tag = "Back to edit";
            TooltipHelper.SetIsActive(PreviewToggleButton, true);
        }
        else
        {
            MessagePreview.Visibility = Visibility.Collapsed;
            MessageInput.Visibility   = Visibility.Visible;
            FormattingToolbar.ClearValue(UIElement.VisibilityProperty); // restore style-driven visibility
            MessageInput.Focus();
            PreviewToggleButton.Tag = "Preview rendered message";
            TooltipHelper.SetIsActive(PreviewToggleButton, false);
        }
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
            if (IsRichContent(msg.Content))
            {
                // Legacy XAML-serialised messages from the previous rich-text build
                AppendRichContent(doc, msg.Content, secondary, top);
            }
            else
            {
                // Markdown (or plain) text — render inline formatting, lists, links
                var rendered = MarkdownRenderer.Render(msg.Content, secondary,
                                   (Brush)FindResource("AccentBlueBrush"));
                double blockTop = top;
                foreach (var block in rendered.Blocks.ToList())
                {
                    rendered.Blocks.Remove(block);
                    block.Margin = new Thickness(16, blockTop, 16, 0);
                    if (block is Paragraph p) p.LineHeight = double.NaN;
                    doc.Blocks.Add(block);
                    blockTop = 1;
                }
            }
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

    private static bool IsRichContent(string content) =>
        content.TrimStart().StartsWith("<Section");

    /// <summary>
    /// Parses a XAML Section (produced by TextRange.Save) and appends its blocks to <paramref name="doc"/>,
    /// applying display margin and foreground. Falls back to plain-text rendering on parse errors.
    /// </summary>
    private static void AppendRichContent(FlowDocument doc, string xaml, Brush foreground, double topMargin)
    {
        try
        {
            var tempDoc = new FlowDocument();
            var range = new TextRange(tempDoc.ContentStart, tempDoc.ContentEnd);
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(xaml));
            range.Load(ms, DataFormats.Xaml);

            double top = topMargin;
            foreach (var block in tempDoc.Blocks.ToList())
            {
                tempDoc.Blocks.Remove(block);
                block.Foreground = foreground;
                if (block is Paragraph p)
                {
                    p.Margin = new Thickness(16, top, 16, 0);
                    p.LineHeight = double.NaN;
                }
                else
                {
                    block.Margin = new Thickness(16, top, 16, 0);
                }
                top = 1;
                doc.Blocks.Add(block);
            }
        }
        catch
        {
            // Corrupted or legacy content — show as plain text
            var p = new Paragraph(new Run(xaml) { Foreground = foreground })
                    { Margin = new Thickness(16, topMargin, 16, 0), LineHeight = double.NaN };
            doc.Blocks.Add(p);
        }
    }
}

// ── Markdown renderer ─────────────────────────────────────────────────────────
// Converts a simple markdown string to a WPF FlowDocument.
// Supported syntax:
//   **bold**  *italic*  __underline__  ~~strike~~
//   [text](url)  bare https:// URLs
//   - item / * item  (bullet list lines)
//   1. item           (numbered list lines)
//   Plain text passes through unchanged.
internal static class MarkdownRenderer
{
    // Inline pattern: order matters — longer tokens first.
    private static readonly System.Text.RegularExpressions.Regex InlinePattern =
        new(@"(\*\*(.+?)\*\*)|(\*(.+?)\*)|(__(.+?)__)|(\~\~(.+?)\~\~)|(\[(.+?)\]\((https?://\S+?)\))|(https?://\S+)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex NumberedLine =
        new(@"^\d+\.\s", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static FlowDocument Render(string text, Brush textBrush, Brush linkBrush)
    {
        var doc = new FlowDocument();
        if (string.IsNullOrEmpty(text)) return doc;

        string[] rawLines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        int i = 0;
        while (i < rawLines.Length)
        {
            string line = rawLines[i];

            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                var list = new List { MarkerStyle = TextMarkerStyle.Disc };
                while (i < rawLines.Length && (rawLines[i].StartsWith("- ") || rawLines[i].StartsWith("* ")))
                {
                    var li = new ListItem(new Paragraph());
                    ParseInlines(rawLines[i][2..], ((Paragraph)li.Blocks.FirstBlock).Inlines, textBrush, linkBrush);
                    list.ListItems.Add(li);
                    i++;
                }
                doc.Blocks.Add(list);
                continue;
            }

            if (NumberedLine.IsMatch(line))
            {
                var list = new List { MarkerStyle = TextMarkerStyle.Decimal };
                while (i < rawLines.Length && NumberedLine.IsMatch(rawLines[i]))
                {
                    string item = NumberedLine.Replace(rawLines[i], string.Empty);
                    var li = new ListItem(new Paragraph());
                    ParseInlines(item, ((Paragraph)li.Blocks.FirstBlock).Inlines, textBrush, linkBrush);
                    list.ListItems.Add(li);
                    i++;
                }
                doc.Blocks.Add(list);
                continue;
            }

            var para = new Paragraph { Foreground = textBrush };
            ParseInlines(line, para.Inlines, textBrush, linkBrush);
            doc.Blocks.Add(para);
            i++;
        }

        return doc;
    }

    private static void ParseInlines(string text, InlineCollection inlines,
                                     Brush textBrush, Brush linkBrush)
    {
        int lastEnd = 0;
        foreach (System.Text.RegularExpressions.Match m in InlinePattern.Matches(text))
        {
            if (m.Index > lastEnd)
                inlines.Add(new Run(text[lastEnd..m.Index]) { Foreground = textBrush });

            if (m.Groups[1].Success)       // **bold**
                inlines.Add(new Run(m.Groups[2].Value) { FontWeight = FontWeights.Bold, Foreground = textBrush });
            else if (m.Groups[3].Success)  // *italic*
                inlines.Add(new Run(m.Groups[4].Value) { FontStyle = FontStyles.Italic, Foreground = textBrush });
            else if (m.Groups[5].Success)  // __underline__
                inlines.Add(new Run(m.Groups[6].Value) { TextDecorations = TextDecorations.Underline, Foreground = textBrush });
            else if (m.Groups[7].Success)  // ~~strike~~
                inlines.Add(new Run(m.Groups[8].Value) { TextDecorations = TextDecorations.Strikethrough, Foreground = textBrush });
            else if (m.Groups[9].Success)  // [text](url)
                inlines.Add(MakeLink(m.Groups[10].Value, m.Groups[11].Value, linkBrush));
            else                           // bare URL
                inlines.Add(MakeLink(m.Value, m.Value, linkBrush));

            lastEnd = m.Index + m.Length;
        }

        if (lastEnd < text.Length)
            inlines.Add(new Run(text[lastEnd..]) { Foreground = textBrush });

        if (!inlines.Any())
            inlines.Add(new Run(string.Empty) { Foreground = textBrush });
    }

    private static Hyperlink MakeLink(string label, string url, Brush brush)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return new Hyperlink(new Run(label)) { Foreground = brush };

        var tt = new ToolTip
        {
            Content = uri.AbsoluteUri,
            Placement = PlacementMode.Mouse,
            Template = (ControlTemplate)Application.Current.FindResource("TooltipTemplateNoTail")
        };
        var link = new Hyperlink(new Run(label)) { NavigateUri = uri, Foreground = brush, ToolTip = tt };
        ToolTipService.SetInitialShowDelay(link, 500);
        link.RequestNavigate += (_, e) =>
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        return link;
    }
}
