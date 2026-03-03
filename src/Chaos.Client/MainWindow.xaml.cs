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

    // ── RichTextBox text helpers ─────────────────────────────────────────────
    // These provide TextBox-like API for the RichTextBox input, serializing
    // emoji InlineUIContainers as :shortcode: text.

    private bool _suppressTextSync;
    private bool _applyingFormatPreview;
    private InlineFormatPreview? _formatPreview;
    private static readonly Regex InputEmojiDetect = new(@":([A-Za-z0-9_]+(?:~\d+)?):", RegexOptions.Compiled);

    /// <summary>
    /// Checks whether the caret is inside an unclosed ``` fenced code block
    /// by walking the paragraph's inlines and counting fence delimiter lines
    /// (segments separated by LineBreak). Odd count = inside a code block.
    /// </summary>
    private bool IsCaretInFencedBlock()
    {
        var para = MessageInput.CaretPosition.Paragraph;
        if (para is null) return false;

        var caret = MessageInput.CaretPosition;
        int fenceCount = 0;
        var lineText = new StringBuilder();

        foreach (var inline in para.Inlines)
        {
            // Stop counting once we've passed the caret position.
            if (inline.ElementStart.CompareTo(caret) >= 0)
                break;

            if (inline is LineBreak)
            {
                string line = lineText.ToString().Trim();
                if (line.StartsWith("```") && (line.Length == 3 || line[3..].All(char.IsLetterOrDigit)))
                    fenceCount++;
                lineText.Clear();
            }
            else if (inline is Run run)
            {
                lineText.Append(run.Text);
            }
        }

        // Check the final line segment (before the caret).
        string lastLine = lineText.ToString().Trim();
        if (lastLine.StartsWith("```") && (lastLine.Length == 3 || lastLine[3..].All(char.IsLetterOrDigit)))
            fenceCount++;

        return fenceCount % 2 == 1;
    }

    private string GetInputText()
    {
        var doc = MessageInput.Document;
        var sb = new StringBuilder();
        bool firstBlock = true;
        foreach (var block in doc.Blocks)
        {
            if (!firstBlock) sb.Append('\n');
            firstBlock = false;
            if (block is Paragraph para)
            {
                foreach (var inline in para.Inlines)
                {
                    if (inline is Run run)
                        sb.Append(run.Text);
                    else if (inline is InlineUIContainer uic && uic.Child is Image img && img.Tag is string code)
                        sb.Append(code);
                    else if (inline is LineBreak)
                        sb.Append('\n');
                }
            }
        }
        return sb.ToString();
    }

    private static string GetSelectedTextWithEmoji(RichTextBox rtb)
    {
        var sel = rtb.Selection;
        if (sel.IsEmpty) return string.Empty;

        var sb = new StringBuilder();
        bool firstParagraph = true;

        foreach (var block in rtb.Document.Blocks.OfType<Paragraph>())
        {
            if (block.ElementEnd.CompareTo(sel.Start) <= 0) continue;
            if (block.ElementStart.CompareTo(sel.End) >= 0) break;

            if (!firstParagraph) sb.Append('\n');
            firstParagraph = false;

            foreach (var inline in block.Inlines)
            {
                if (inline.ElementEnd.CompareTo(sel.Start) <= 0) continue;
                if (inline.ElementStart.CompareTo(sel.End) >= 0) break;

                if (inline is Run run)
                {
                    var runStart = run.ContentStart.CompareTo(sel.Start) < 0 ? sel.Start : run.ContentStart;
                    var runEnd = run.ContentEnd.CompareTo(sel.End) > 0 ? sel.End : run.ContentEnd;
                    sb.Append(new TextRange(runStart, runEnd).Text);
                }
                else if (inline is InlineUIContainer { Child: Image { Tag: string code } })
                {
                    sb.Append(code);
                }
            }
        }

        return sb.ToString();
    }

    private void SetInputText(string text)
    {
        _suppressTextSync = true;
        try
        {
            MessageInput.Document.Blocks.Clear();
            if (string.IsNullOrEmpty(text))
            {
                MessageInput.Document.Blocks.Add(new Paragraph());
                return;
            }
            var lines = text.Split('\n');
            foreach (var line in lines)
            {
                var para = new Paragraph { Margin = new Thickness(0) };
                if (string.IsNullOrEmpty(line))
                {
                    para.Inlines.Add(new Run());
                }
                else
                {
                    para.Inlines.Add(new Run(line) { Foreground = (Brush)FindResource("TextPrimaryBrush") });
                }
                MessageInput.Document.Blocks.Add(para);
            }
        }
        finally
        {
            _suppressTextSync = false;
        }
    }

    private int GetInputCursorOffset()
    {
        var start = MessageInput.Document.ContentStart;
        var caret = MessageInput.CaretPosition;
        return GetTextOffset(start, caret);
    }

    private void SetInputCursorOffset(int offset)
    {
        var pos = GetTextPointerAtOffset(MessageInput.Document.ContentStart, offset);
        if (pos is not null)
            MessageInput.CaretPosition = pos;
    }

    private int GetInputSelectionLength()
    {
        if (MessageInput.Selection.IsEmpty) return 0;
        var sel = MessageInput.Selection;
        return GetTextOffset(sel.Start, sel.End);
    }

    private string GetInputSelectedText()
    {
        if (MessageInput.Selection.IsEmpty) return string.Empty;
        // Serialize selection with emoji support
        var sb = new StringBuilder();
        var start = MessageInput.Selection.Start;
        var end = MessageInput.Selection.End;

        var current = start;
        while (current is not null && current.CompareTo(end) < 0)
        {
            if (current.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var next = current.GetNextContextPosition(LogicalDirection.Forward);
                if (next is not null)
                {
                    var effectiveEnd = next.CompareTo(end) > 0 ? end : next;
                    var range = new TextRange(current, effectiveEnd);
                    sb.Append(range.Text);
                }
            }
            else if (current.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.EmbeddedElement)
            {
                var element = current.GetAdjacentElement(LogicalDirection.Forward);
                if (element is InlineUIContainer uic && uic.Child is Image img && img.Tag is string code)
                    sb.Append(code);
            }
            current = current.GetNextContextPosition(LogicalDirection.Forward);
        }
        return sb.ToString();
    }

    private void SetInputSelection(int start, int length)
    {
        var startPos = GetTextPointerAtOffset(MessageInput.Document.ContentStart, start);
        if (startPos is null) return;
        if (length == 0)
        {
            MessageInput.CaretPosition = startPos;
            return;
        }
        var endPos = GetTextPointerAtOffset(startPos, length);
        if (endPos is not null)
            MessageInput.Selection.Select(startPos, endPos);
    }

    private static int GetTextOffset(TextPointer start, TextPointer end)
    {
        int offset = 0;
        var current = start;
        while (current is not null && current.CompareTo(end) < 0)
        {
            var ctx = current.GetPointerContext(LogicalDirection.Forward);
            if (ctx == TextPointerContext.Text)
            {
                var next = current.GetNextContextPosition(LogicalDirection.Forward);
                if (next is not null)
                {
                    var effective = next.CompareTo(end) > 0 ? end : next;
                    offset += new TextRange(current, effective).Text.Length;
                }
            }
            else if (ctx == TextPointerContext.EmbeddedElement)
            {
                var element = current.GetAdjacentElement(LogicalDirection.Forward);
                if (element is InlineUIContainer uic && uic.Child is Image img && img.Tag is string code)
                    offset += code.Length;
                else
                    offset++;
            }
            else if (ctx == TextPointerContext.ElementEnd)
            {
                // Check if this is the end of a Paragraph (newline)
                var element = current.GetAdjacentElement(LogicalDirection.Forward);
                if (element is Paragraph && current.GetNextContextPosition(LogicalDirection.Forward)?.CompareTo(end) <= 0)
                    offset++; // count as newline
            }
            current = current.GetNextContextPosition(LogicalDirection.Forward);
        }
        return offset;
    }

    private static TextPointer? GetTextPointerAtOffset(TextPointer start, int offset)
    {
        int remaining = offset;
        var current = start;
        while (current is not null && remaining > 0)
        {
            var ctx = current.GetPointerContext(LogicalDirection.Forward);
            if (ctx == TextPointerContext.Text)
            {
                var next = current.GetNextContextPosition(LogicalDirection.Forward);
                if (next is not null)
                {
                    int textLen = new TextRange(current, next).Text.Length;
                    if (textLen >= remaining)
                        return current.GetPositionAtOffset(remaining, LogicalDirection.Forward);
                    remaining -= textLen;
                }
            }
            else if (ctx == TextPointerContext.EmbeddedElement)
            {
                var element = current.GetAdjacentElement(LogicalDirection.Forward);
                int len = 1;
                if (element is InlineUIContainer uic && uic.Child is Image img && img.Tag is string code)
                    len = code.Length;
                if (len >= remaining)
                {
                    // Skip past the embedded element
                    return current.GetNextContextPosition(LogicalDirection.Forward)?
                        .GetNextContextPosition(LogicalDirection.Forward);
                }
                remaining -= len;
            }
            else if (ctx == TextPointerContext.ElementEnd)
            {
                var element = current.GetAdjacentElement(LogicalDirection.Forward);
                if (element is Paragraph)
                {
                    remaining--; // newline
                    if (remaining <= 0)
                        return current.GetNextContextPosition(LogicalDirection.Forward);
                }
            }
            current = current.GetNextContextPosition(LogicalDirection.Forward);
        }
        return current ?? start.DocumentEnd;
    }

    private void SyncInputToViewModel()
    {
        if (_suppressTextSync) return;
        if (DataContext is MainViewModel vm)
        {
            _suppressTextSync = true;
            vm.MessageText = GetInputText();
            _suppressTextSync = false;
        }
    }

    private void DetectAndReplaceEmojis()
    {
        if (DataContext is not MainViewModel vm || !vm.EmojiService.IsLoaded) return;

        // Walk all Runs looking for :shortcode: patterns
        var runs = new List<Run>();
        foreach (var block in MessageInput.Document.Blocks.OfType<Paragraph>())
            runs.AddRange(block.Inlines.OfType<Run>());

        foreach (var run in runs)
        {
            var match = InputEmojiDetect.Match(run.Text);
            if (!match.Success) continue;

            string shortcode = match.Groups[1].Value;
            var emoji = vm.EmojiService.Resolve(shortcode);
            if (emoji is null) continue;

            // Replace the :shortcode: text with an emoji image
            string before = run.Text[..match.Index];
            string after = run.Text[(match.Index + match.Length)..];

            var parent = run.Parent as Paragraph;
            if (parent is null) continue;

            _suppressTextSync = true;
            try
            {
                var img = new Image
                {
                    Width = 20,
                    Height = 20,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = $":{shortcode}:",
                };
                img.ToolTip = new ToolTip { Content = $":{shortcode}:" };
                ToolTipService.SetInitialShowDelay(img, 0);

                var cached = vm.EmojiService.GetCachedImage(emoji);
                if (cached is not null)
                {
                    img.Source = cached;
                }
                else
                {
                    _ = Task.Run(async () =>
                    {
                        var bmp = await vm.EmojiService.GetImageAsync(emoji);
                        if (bmp is not null)
                            Application.Current?.Dispatcher.Invoke(() => img.Source = bmp);
                    });
                }

                var container = new InlineUIContainer(img) { BaselineAlignment = BaselineAlignment.TextBottom };

                // Insert new inlines
                if (!string.IsNullOrEmpty(after))
                {
                    var afterRun = new Run(after) { Foreground = run.Foreground };
                    parent.Inlines.InsertAfter(run, afterRun);
                }
                parent.Inlines.InsertAfter(run, container);

                if (!string.IsNullOrEmpty(before))
                {
                    run.Text = before;
                }
                else
                {
                    parent.Inlines.Remove(run);
                }

                // Move cursor to after the emoji
                MessageInput.CaretPosition = container.ElementEnd;
            }
            finally
            {
                _suppressTextSync = false;
            }
            break; // Process one emoji at a time
        }
    }

    // Last known bounds while the window was in Normal state.
    // Updated by SizeChanged/LocationChanged only when not maximized/minimized,
    // so they always represent the correct restore geometry.
    private double _restoreLeft, _restoreTop, _restoreWidth, _restoreHeight;

    public MainWindow()
    {
        InitializeComponent();
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico"));

        // Hook up RichTextBox text sync and emoji detection
        Loaded += (_, _) =>
        {
            // Set initial empty document
            MessageInput.Document = new FlowDocument(new Paragraph { Margin = new Thickness(0) })
            {
                PagePadding = new Thickness(0),
            };

            // Override Copy/Cut to serialize emoji images as :shortcode: text
            MessageInput.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) =>
            {
                var text = GetSelectedTextWithEmoji(MessageInput);
                if (string.IsNullOrEmpty(text)) return;
                Clipboard.SetText(text);
                e.Handled = true;
            }));
            MessageInput.CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut, (_, e) =>
            {
                var text = GetSelectedTextWithEmoji(MessageInput);
                if (string.IsNullOrEmpty(text)) return;
                Clipboard.SetText(text);
                MessageInput.Selection.Text = string.Empty;
                e.Handled = true;
            }));

            _formatPreview = new InlineFormatPreview(
                (Brush)FindResource("TextPrimaryBrush"),
                (Brush)FindResource("TextMutedBrush"));

            MessageInput.TextChanged += (_, _) =>
            {
                if (_suppressTextSync || _applyingFormatPreview) return;
                SyncInputToViewModel();
                DetectAndReplaceEmojis();

                _applyingFormatPreview = true;
                _suppressTextSync = true;
                try { _formatPreview.Apply(MessageInput); }
                finally { _suppressTextSync = false; _applyingFormatPreview = false; }

                // Update emoji autocomplete immediately (in-memory search is fast)
                if (DataContext is MainViewModel vm3)
                {
                    var caret = MessageInput.CaretPosition;
                    string textBeforeCaret = caret.GetTextInRun(LogicalDirection.Backward);
                    if (string.IsNullOrEmpty(textBeforeCaret))
                        vm3.DismissEmojiSuggestions();
                    else
                        vm3.UpdateEmojiSuggestions(textBeforeCaret, textBeforeCaret.Length);
                }
            };

            // Sync ViewModel → RichTextBox when MessageText is cleared (e.g. after send)
            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(MainViewModel.MessageText) && !_suppressTextSync)
                    {
                        string currentText = GetInputText();
                        if (currentText != vm.MessageText)
                        {
                            SetInputText(vm.MessageText);
                        }
                    }
                };
            }
        };

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

            // Override Copy to serialize emoji images as :shortcode: text
            MessageList.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) =>
            {
                var text = GetSelectedTextWithEmoji(MessageList);
                if (string.IsNullOrEmpty(text)) return;
                Clipboard.SetText(text);
                e.Handled = true;
            }));

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

            // Eagerly build the emoji grid in the background after emojis load
            vm.EmojiService.EmojisLoaded += () =>
            {
                _emojiGridBuilt = false;
                _cachedEmojiGridChildren = null;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
                    PopulateEmojiGrid(null));
            };

            // Rebuild grid when images finish preloading so it's ready with real images
            vm.EmojiService.ImagesReady += () =>
            {
                _emojiGridBuilt = false;
                _cachedEmojiGridChildren = null;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
                    PopulateEmojiGrid(null));
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

            // Shared sizing logic for autocomplete + picker repositioning
            void SizeAutocomplete()
            {
                double inputWidth = TextInputBorder.ActualWidth;
                double menuWidth = inputWidth > 40 ? inputWidth - 32 : inputWidth;
                EmojiAutocompleteHost.SubmenuWidth = menuWidth;
                EmojiAutocompleteHost.HorizontalOffset = (inputWidth - menuWidth) / 2;
            }

            vm.EmojiAutocomplete.BeforeOpen = SizeAutocomplete;
            EmojiAutocompleteHost.Repositioning += SizeAutocomplete;

            EmojiPickerHost.Repositioning += () =>
                EmojiPickerHost.HorizontalOffset = TextInputBorder.ActualWidth - EmojiPickerHost.SubmenuWidth;

            vm.EmojiAutocomplete.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(EmojiAutocompleteViewModel.SelectedIndex))
                {
                    int idx = vm.EmojiAutocomplete.SelectedIndex;
                    if (idx >= 0 && idx < vm.EmojiAutocomplete.Suggestions.Count)
                        EmojiSuggestionList.ScrollIntoView(vm.EmojiAutocomplete.Suggestions[idx]);
                }
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

    private void EmojiSuggestionList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement el &&
            el.DataContext is EmojiSuggestionItem item &&
            DataContext is MainViewModel vm)
        {
            ApplyEmojiSuggestion(vm, item);
            e.Handled = true;
        }
    }

    private void ApplyEmojiSuggestion(MainViewModel vm, EmojiSuggestionItem item)
    {
        // Work directly with the Run at the caret to avoid offset misalignment
        // caused by InlineUIContainers (emoji images) in the document.
        var caret = MessageInput.CaretPosition;
        string textBefore = caret.GetTextInRun(LogicalDirection.Backward);
        int colonIdx = textBefore.LastIndexOf(':');
        if (colonIdx >= 0)
        {
            string replacement = $"{item.CommandName} ";
            int charsToDelete = textBefore.Length - colonIdx;

            // Delete from the ':' to the caret
            var deleteStart = caret.GetPositionAtOffset(-charsToDelete, LogicalDirection.Backward);
            if (deleteStart is not null)
            {
                _suppressTextSync = true;
                try
                {
                    new TextRange(deleteStart, caret).Text = replacement;
                    // Caret is automatically placed after the inserted text
                }
                finally
                {
                    _suppressTextSync = false;
                }
            }
        }

        DetectAndReplaceEmojis();
        SyncInputToViewModel();

        vm.DismissEmojiSuggestions();
        vm.EmojiService.TrackUsage(item.Emoji.Name);
        MessageInput.Focus();
    }

    private void MessageInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Emoji suggestions take priority when visible
        if (DataContext is MainViewModel vm && vm.EmojiAutocomplete.IsOpen)
        {
            var ac = vm.EmojiAutocomplete;
            if (e.Key == Key.Down)
            {
                ac.Navigate(1);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up)
            {
                ac.Navigate(-1);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Tab)
            {
                int idx = ac.SelectedIndex >= 0 ? ac.SelectedIndex : 0;
                if (idx < ac.Suggestions.Count)
                    ApplyEmojiSuggestion(vm, ac.Suggestions[idx]);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter && ac.SelectedIndex >= 0)
            {
                ApplyEmojiSuggestion(vm, ac.Suggestions[ac.SelectedIndex]);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                ac.Dismiss();
                e.Handled = true;
                return;
            }
        }

        if (DataContext is MainViewModel vm1 && vm1.ShowSlashSuggestions)
        {
            if (e.Key == Key.Down)
            {
                vm1.NavigateSuggestions(1);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up)
            {
                vm1.NavigateSuggestions(-1);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Tab)
            {
                int idx = vm1.SelectedSuggestionIndex >= 0 ? vm1.SelectedSuggestionIndex : 0;
                if (idx < vm1.SlashSuggestions.Count)
                    ApplySuggestion(vm1, vm1.SlashSuggestions[idx]);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                vm1.DismissSuggestions();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter && vm1.SelectedSuggestionIndex >= 0)
            {
                ApplySuggestion(vm1, vm1.SlashSuggestions[vm1.SelectedSuggestionIndex]);
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
            // Inside an unclosed ``` block, Enter inserts a LineBreak (not a Paragraph).
            if (IsCaretInFencedBlock())
            {
                var newPos = MessageInput.CaretPosition.InsertLineBreak();
                MessageInput.CaretPosition = newPos;
                e.Handled = true;
                return;
            }
            if (vm2.SendMessageCommand.CanExecute(null))
                vm2.SendMessageCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            int pos = GetInputCursorOffset();
            int len = GetInputSelectionLength();
            string text = GetInputText();
            SetInputText(text.Remove(pos, len).Insert(pos, "    "));
            SetInputSelection(pos + 4, 0);
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
        SetInputCursorOffset(GetInputText().Length);
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

    private void FormatBold_Click(object sender, RoutedEventArgs e)          => ToggleInlineFormat("**");
    private void FormatItalic_Click(object sender, RoutedEventArgs e)        => ToggleInlineFormat("*");
    private void FormatUnderline_Click(object sender, RoutedEventArgs e)     => ToggleInlineFormat("__");
    private void FormatStrikethrough_Click(object sender, RoutedEventArgs e) => ToggleInlineFormat("~~");

    // Span regex patterns and cursor-detection helpers live in FormatDetection (see FormatDetection.cs).

    private void UpdateFormatButtonStates()
    {
        string text = GetInputText();
        int pos = GetInputCursorOffset();
        bool inBoldItalic = FormatDetection.IsCursorInSpan(text, pos, FormatDetection.BoldItalicSpan, 3);
        TooltipHelper.SetIsActive(BoldButton,          inBoldItalic || FormatDetection.IsCursorInSpan(text, pos, FormatDetection.BoldSpan, 2));
        TooltipHelper.SetIsActive(ItalicButton,        inBoldItalic || FormatDetection.IsCursorInItalicSpan(text, pos));
        TooltipHelper.SetIsActive(UnderlineButton,     FormatDetection.IsCursorInSpan(text, pos, FormatDetection.UnderlineSpan, 2));
        TooltipHelper.SetIsActive(StrikethroughButton, FormatDetection.IsCursorInSpan(text, pos, FormatDetection.StrikeSpan, 2));

        int lineStartPos = (pos == 0 || text.Length == 0) ? 0 : text.LastIndexOf('\n', Math.Min(pos, text.Length) - 1) + 1;
        bool inFencedCode = FormatDetection.IsLineInFencedBlock(text, lineStartPos);
        TooltipHelper.SetIsActive(CodeButton,  inFencedCode
            || FormatDetection.IsCursorInSpan(text, pos, FormatDetection.CodeSpan, 1)
            || FormatDetection.IsCursorInSpan(text, pos, FormatDetection.TripleInlineCodeSpan, 3));
        int lineEndPos = text.IndexOf('\n', lineStartPos);
        if (lineEndPos < 0) lineEndPos = text.Length;
        string currentLine = text[lineStartPos..lineEndPos];
        TooltipHelper.SetIsActive(QuoteButton, currentLine.StartsWith("> "));
    }

    // Returns true when `pos` falls inside the content region of any span matched by `pattern`.
    // markerLen is the length of the opening/closing delimiter (** = 2, __ = 2, ~~ = 2).
    private void InsertMarkdownAround(string marker)
    {
        int start = GetInputCursorOffset();
        int len   = GetInputSelectionLength();
        string sel = GetInputSelectedText();
        string text = GetInputText();

        text = text.Remove(start, len).Insert(start, marker + sel + marker);
        SetInputText(text);
        SetInputSelection(start + marker.Length, sel.Length);
        MessageInput.Focus();
    }

    // With a selection, always add the wrapper. With a point cursor, toggle off if already inside the span.
    private void ToggleInlineFormat(string marker)
    {
        if (GetInputSelectionLength() > 0) { InsertMarkdownAround(marker); return; }

        string text = GetInputText();
        int pos = GetInputCursorOffset();

        if (marker == "**")
        {
            // Bold is outer — check bold+italic first
            var m = FindEnclosingSpan(text, pos, FormatDetection.BoldItalicSpan, 3);
            if (m.HasValue) { RemoveSpanMarkers(m.Value.s, 2, m.Value.s + m.Value.l - 2, 2); return; }
            m = FindEnclosingSpan(text, pos, FormatDetection.BoldSpan, 2);
            if (m.HasValue) { RemoveSpanMarkers(m.Value.s, 2, m.Value.s + m.Value.l - 2, 2); return; }
        }
        else if (marker == "*")
        {
            // Italic is inner — strip the inner * from bold+italic
            var m = FindEnclosingSpan(text, pos, FormatDetection.BoldItalicSpan, 3);
            if (m.HasValue) { RemoveSpanMarkers(m.Value.s + 2, 1, m.Value.s + m.Value.l - 3, 1); return; }
            var it = FindItalicSpanContaining(text, pos);
            if (it.HasValue) { RemoveSpanMarkers(it.Value.open, 1, it.Value.close, 1); return; }
        }
        else if (marker == "__")
        {
            var m = FindEnclosingSpan(text, pos, FormatDetection.UnderlineSpan, 2);
            if (m.HasValue) { RemoveSpanMarkers(m.Value.s, 2, m.Value.s + m.Value.l - 2, 2); return; }
        }
        else if (marker == "~~")
        {
            var m = FindEnclosingSpan(text, pos, FormatDetection.StrikeSpan, 2);
            if (m.HasValue) { RemoveSpanMarkers(m.Value.s, 2, m.Value.s + m.Value.l - 2, 2); return; }
        }
        else if (marker == "`")
        {
            var m = FindEnclosingSpan(text, pos, FormatDetection.TripleInlineCodeSpan, 3);
            if (m.HasValue) { RemoveSpanMarkers(m.Value.s, 3, m.Value.s + m.Value.l - 3, 3); return; }
            m = FindEnclosingSpan(text, pos, FormatDetection.CodeSpan, 1);
            if (m.HasValue) { RemoveSpanMarkers(m.Value.s, 1, m.Value.s + m.Value.l - 1, 1); return; }
        }

        InsertMarkdownAround(marker);
    }

    private static (int s, int l)? FindEnclosingSpan(string text, int pos, Regex pattern, int markerLen)
    {
        foreach (Match m in pattern.Matches(text))
            if (pos >= m.Index + markerLen && pos <= m.Index + m.Length - markerLen)
                return (m.Index, m.Length);
        return null;
    }

    // Like FindEnclosingSpan but returns the character positions of the opening and closing * for italic.
    private static (int open, int close)? FindItalicSpanContaining(string text, int pos)
    {
        bool[] masked = FormatDetection.MaskBoldDelimiters(text);

        int openAt = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '*' || masked[i]) continue;
            if (openAt < 0) openAt = i;
            else
            {
                if (pos >= openAt + 1 && pos <= i) return (openAt, i);
                openAt = -1;
            }
        }
        return null;
    }

    private void RemoveSpanMarkers(int openAt, int openLen, int closeAt, int closeLen)
    {
        int origPos = GetInputCursorOffset();
        string text = GetInputText();
        // Remove close first (higher index) so openAt stays valid
        text = text.Remove(closeAt, closeLen);
        text = text.Remove(openAt, openLen);
        // Adjust cursor
        int pos = origPos;
        if (pos >= closeAt + closeLen) pos -= closeLen;
        else if (pos > closeAt)        pos  = closeAt;
        if (pos >= openAt + openLen)   pos -= openLen;
        else if (pos > openAt)         pos  = openAt;
        SetInputText(text);
        SetInputSelection(pos, 0);
        MessageInput.Focus();
    }

    private void FormatCode_Click(object sender, RoutedEventArgs e)  => ToggleCode();
    private void FormatQuote_Click(object sender, RoutedEventArgs e) => ToggleQuote();

    private void ToggleCode()
    {
        // Multi-line selection → fenced code block
        if (GetInputSelectionLength() > 0 && GetInputSelectedText().Contains('\n'))
        {
            ToggleFencedCode();
            return;
        }
        // Single-line or cursor → inline backtick
        ToggleInlineFormat("`");
    }

    private void ToggleFencedCode()
    {
        GetSelectedLineRegion(out int lineStart, out int lineEnd);
        string text = GetInputText();
        string region = text[lineStart..lineEnd];
        string[] lines = region.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        // Already fenced → remove the fence lines (opening may be ```<number>)
        if (lines.Length >= 2 && lines[0].StartsWith("```") && lines[^1] == "```")
        {
            string inner = string.Join("\n", lines[1..^1]);
            SetInputText(text[..lineStart] + inner + text[lineEnd..]);
            SetInputSelection(lineStart, inner.Length);
            MessageInput.Focus();
            return;
        }

        string fenced = "```\n" + region + "\n```";
        SetInputText(text[..lineStart] + fenced + text[lineEnd..]);
        SetInputSelection(lineStart, fenced.Length);
        MessageInput.Focus();
    }

    private void ToggleQuote()
    {
        int origStart  = GetInputCursorOffset();
        int origLength = GetInputSelectionLength();

        GetSelectedLineRegion(out int lineStart, out int lineEnd);
        string text = GetInputText();
        string region = text[lineStart..lineEnd];
        string[] lines = region.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        bool allQuoted = lines.All(l => string.IsNullOrEmpty(l) || l.StartsWith("> "));
        string[] newLines = lines.Select(line =>
        {
            if (string.IsNullOrEmpty(line)) return line;
            return allQuoted ? line[2..] : $"> {line}";
        }).ToArray();

        string newRegion = string.Join("\n", newLines);
        SetInputText(text[..lineStart] + newRegion + text[lineEnd..]);

        if (origLength == 0)
        {
            int delta = allQuoted ? -2 : 2;
            SetInputSelection(Math.Max(lineStart, origStart + delta), 0);
        }
        else
        {
            SetInputSelection(lineStart, newRegion.Length);
        }
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
        int origStart  = GetInputCursorOffset();
        int origLength = GetInputSelectionLength();

        GetSelectedLineRegion(out int lineStart, out int lineEnd);
        string text = GetInputText();
        string region = text[lineStart..lineEnd];
        string[] lines = region.Split('\n');
        string[] newLines = lines.Select(l =>
            l.StartsWith("    ") ? l[4..] : l.TrimStart(' ')).ToArray();
        string newRegion = string.Join("\n", newLines);

        SetInputText(text[..lineStart] + newRegion + text[lineEnd..]);

        if (origLength == 0)
        {
            int spacesRemoved = lines[0].Length - newLines[0].Length;
            SetInputSelection(Math.Max(lineStart, origStart - spacesRemoved), 0);
        }
        else
        {
            SetInputSelection(lineStart, newRegion.Length);
        }
        MessageInput.Focus();
    }

    private static readonly Regex NumberedListPrefix = new(@"^(\d+)\. ", RegexOptions.Compiled);

    private bool TryHandleListEnter()
    {
        string text = GetInputText();
        int    pos  = GetInputCursorOffset();

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
                SetInputText(text.Remove(lineStart, 2));
                SetInputCursorOffset(lineStart);
            }
            else
            {
                string insert = "\n- ";
                SetInputText(text.Insert(pos, insert));
                SetInputCursorOffset(pos + insert.Length);
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
                SetInputText(text.Remove(lineStart, prefix.Length));
                SetInputCursorOffset(lineStart);
            }
            else
            {
                string insert = $"\n{num + 1}. ";
                SetInputText(text.Insert(pos, insert));
                SetInputCursorOffset(pos + insert.Length);
            }
            return true;
        }

        return false;
    }


    private void PrefixSelectedLines(Func<int, string> prefixFor)
    {
        int origStart  = GetInputCursorOffset();
        int origLength = GetInputSelectionLength();

        GetSelectedLineRegion(out int lineStart, out int lineEnd);
        string text = GetInputText();
        bool hasTrailingNewline = lineEnd < text.Length;
        string region = text[lineStart..lineEnd];
        string[] lines = region.Split('\n');
        string newRegion = string.Join("\n", lines.Select((l, i) => prefixFor(i) + l));

        SetInputText(text[..lineStart] + newRegion + text[lineEnd..]);

        if (origLength == 0)
        {
            SetInputSelection(origStart + prefixFor(0).Length, 0);
        }
        else
        {
            SetInputSelection(lineStart, hasTrailingNewline ? newRegion.Length - 1 : newRegion.Length);
        }
        MessageInput.Focus();
    }

    private void GetSelectedLineRegion(out int lineStart, out int lineEnd)
    {
        string text = GetInputText();
        int selStart = GetInputCursorOffset();
        int selEnd   = selStart + GetInputSelectionLength();
        lineStart = selStart == 0 ? 0 : text.LastIndexOf('\n', selStart - 1) + 1;
        int nl = selEnd < text.Length ? text.IndexOf('\n', selEnd) : -1;
        lineEnd = nl >= 0 ? nl : text.Length;
    }

    private void ApplyToLineRegion(int lineStart, int lineEnd, string newRegion)
    {
        string text = GetInputText();
        SetInputText(text[..lineStart] + newRegion + text[lineEnd..]);
        SetInputSelection(lineStart, newRegion.Length);
        MessageInput.Focus();
    }

    private void InsertHyperlink_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        string display = GetInputSelectedText();
        int start = GetInputCursorOffset();
        int len   = GetInputSelectionLength();

        vm.OpenModal(new HyperlinkModalViewModel(
            initialUrl:     display.StartsWith("http") ? display : "https://",
            initialDisplay: display.StartsWith("http") ? string.Empty : display,
            confirm: (url, displayText) =>
            {
                string md = string.IsNullOrEmpty(displayText) ? url : $"[{displayText}]({url})";
                string text = GetInputText();
                SetInputText(text.Remove(start, len).Insert(start, md));
                SetInputCursorOffset(start + md.Length);
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
            var emojiSvc = (DataContext as MainViewModel)?.EmojiService;
            MessagePreview.Document = MarkdownRenderer.Render(
                GetInputText(), secondary, accent, emojiSvc);
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

    // ── Emoji Picker ─────────────────────────────────────────────────────────

    private readonly HashSet<string> _collapsedCategories = new();
    private readonly Dictionary<string, FrameworkElement> _categoryHeaders = new();
    private bool _emojiGridBuilt;
    private List<UIElement>? _cachedEmojiGridChildren;

    private void EmojiPicker_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.EmojiPicker.IsOpen)
        {
            vm.EmojiPicker.Close();
            return;
        }
        EmojiSearchBox.Text = string.Empty;
        RestoreCachedEmojiGrid();
        BuildCategorySidebar(vm);
        EmojiPickerHost.HorizontalOffset = TextInputBorder.ActualWidth - EmojiPickerHost.SubmenuWidth;
        vm.EmojiPicker.Open();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => EmojiSearchBox.Focus());
    }

    private void RestoreCachedEmojiGrid()
    {
        if (_emojiGridBuilt && _cachedEmojiGridChildren is not null)
        {
            EmojiGridPanel.Children.Clear();
            foreach (var child in _cachedEmojiGridChildren)
                EmojiGridPanel.Children.Add(child);
            EmojiScrollViewer.ScrollToTop();
            return;
        }
        PopulateEmojiGrid(null);
    }

    private void BuildCategorySidebar(MainViewModel vm)
    {
        CategorySidebar.Children.Clear();

        var categories = new List<(string Name, Shared.EmojiDto? Icon)>();
        var frequent = vm.EmojiService.GetFrequentlyUsed();
        if (frequent.Count > 0)
            categories.Add(("Frequently Used", frequent[0]));

        foreach (var (cat, emojis) in vm.EmojiService.GetGroupedByCategory())
        {
            var first = emojis.Count > 0 ? emojis[0] : null;
            categories.Add((cat, first));
        }

        var sidebarStyle = (Style)FindResource("EmojiCategorySidebarButton");
        foreach (var (cat, icon) in categories)
        {
            var btnImg = new System.Windows.Controls.Image
            {
                Width = 18,
                Height = 18,
                Stretch = Stretch.Uniform,
                Source = icon != null ? vm.EmojiService.GetCachedImage(icon) : null,
            };

            var btn = new System.Windows.Controls.Button
            {
                Content = btnImg,
                Style = sidebarStyle,
                ToolTip = new ToolTip { Content = cat },
            };
            ToolTipService.SetInitialShowDelay(btn, 0);

            var capturedCat = cat;
            btn.Click += (_, _) =>
            {
                if (_categoryHeaders.TryGetValue(capturedCat, out var header))
                    header.BringIntoView();
            };

            CategorySidebar.Children.Add(btn);
        }
    }

    private void EmojiSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        string filter = EmojiSearchBox.Text.Trim();
        if (string.IsNullOrEmpty(filter))
        {
            RestoreCachedEmojiGrid();
        }
        else
        {
            PopulateEmojiGrid(filter);
        }
    }

    private void PopulateEmojiGrid(string? filter)
    {
        EmojiGridPanel.Children.Clear();
        _categoryHeaders.Clear();
        if (DataContext is not MainViewModel vm || !vm.EmojiService.IsLoaded) return;

        if (filter is not null)
        {
            // Search mode: flat grid of matching emojis
            var results = vm.EmojiService.Search(filter, 100);
            if (results.Count == 0)
            {
                EmojiGridPanel.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = "No emojis found",
                    Foreground = (Brush)FindResource("TextMutedBrush"),
                    FontSize = 13,
                    Margin = new Thickness(8, 16, 8, 8),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                return;
            }
            var wrap = new System.Windows.Controls.WrapPanel();
            foreach (var emoji in results)
                wrap.Children.Add(CreateEmojiButton(emoji, vm));
            EmojiGridPanel.Children.Add(wrap);
            return;
        }

        // Category mode with optional frequently used
        var frequent = vm.EmojiService.GetFrequentlyUsed();
        if (frequent.Count > 0)
            AddCategorySection("Frequently Used", frequent, vm);

        foreach (var (category, emojis) in vm.EmojiService.GetGroupedByCategory())
            AddCategorySection(category, emojis, vm);

        // Cache the built grid children for fast re-display
        _cachedEmojiGridChildren = new List<UIElement>();
        foreach (UIElement child in EmojiGridPanel.Children)
            _cachedEmojiGridChildren.Add(child);
        _emojiGridBuilt = true;
    }

    private void AddCategorySection(string category, List<Shared.EmojiDto> emojis, MainViewModel vm)
    {
        bool collapsed = _collapsedCategories.Contains(category);

        var nameText = new System.Windows.Controls.TextBlock
        {
            Text = category,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var caretText = new System.Windows.Controls.TextBlock
        {
            Text = collapsed ? "\u276E" : "\u276F",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(nameText);
        panel.Children.Add(caretText);

        var header = new System.Windows.Controls.Button
        {
            Content = panel,
            Style = (Style)FindResource("EmojiCategoryHeader"),
        };
        WrapPanel? wrapRef = null;
        header.Click += (_, _) =>
        {
            if (_collapsedCategories.Contains(category))
            {
                _collapsedCategories.Remove(category);
                caretText.Text = "\u276F";
                if (wrapRef != null) wrapRef.Visibility = Visibility.Visible;
            }
            else
            {
                _collapsedCategories.Add(category);
                caretText.Text = "\u276E";
                if (wrapRef != null) wrapRef.Visibility = Visibility.Collapsed;
            }
        };
        _categoryHeaders[category] = header;
        EmojiGridPanel.Children.Add(header);

        var wrap = new System.Windows.Controls.WrapPanel();
        if (collapsed) wrap.Visibility = Visibility.Collapsed;
        foreach (var emoji in emojis)
            wrap.Children.Add(CreateEmojiButton(emoji, vm));
        wrapRef = wrap;
        EmojiGridPanel.Children.Add(wrap);
    }

    private System.Windows.Controls.Button CreateEmojiButton(Shared.EmojiDto emoji, MainViewModel vm)
    {
        var img = new System.Windows.Controls.Image
        {
            Width = 32,
            Height = 32,
            Stretch = Stretch.Uniform,
            Source = vm.EmojiService.GetCachedImage(emoji),
        };

        var btn = new System.Windows.Controls.Button
        {
            Content = img,
            Style = (Style)FindResource("EmojiGridButton"),
        };

        btn.Click += (_, _) => OnEmojiClicked(emoji, vm);
        btn.MouseEnter += (_, _) =>
        {
            EmojiPreviewImage.Source = vm.EmojiService.GetCachedImage(emoji);
            EmojiPreviewName.Text = $":{emoji.Name}:";
        };

        return btn;
    }

    private void OnEmojiClicked(Shared.EmojiDto emoji, MainViewModel vm)
    {
        // Insert directly at the caret's TextPointer to avoid offset misalignment
        // caused by InlineUIContainers (emoji images) in the document.
        string code = $":{emoji.Name}: ";
        var caret = MessageInput.CaretPosition;

        _suppressTextSync = true;
        try
        {
            // Insert text at caret — if caret is at an element boundary, get an insertion position
            var insertPos = caret.GetInsertionPosition(LogicalDirection.Forward);
            insertPos.InsertTextInRun(code);
            // Move caret past the inserted text
            var newPos = insertPos.GetPositionAtOffset(code.Length, LogicalDirection.Forward);
            if (newPos is not null)
                MessageInput.CaretPosition = newPos;
        }
        finally
        {
            _suppressTextSync = false;
        }

        DetectAndReplaceEmojis();
        SyncInputToViewModel();

        vm.EmojiService.TrackUsage(emoji.Name);
        vm.EmojiPicker.Close();
        MessageInput.Focus();
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
                // Determine emoji service and size
                var vm = DataContext as MainViewModel;
                var emojiService = vm?.EmojiService;
                int emojiSize = MarkdownRenderer.IsEmojiOnly(msg.Content, emojiService) ? 48 : 32;

                // Markdown (or plain) text — render inline formatting, lists, links, emojis
                var rendered = MarkdownRenderer.Render(msg.Content, secondary,
                                   (Brush)FindResource("AccentBlueBrush"), emojiService, emojiSize);
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
//   :emoji_name:  inline emoji images
//   - item / * item  (bullet list lines)
//   1. item           (numbered list lines)
//   Plain text passes through unchanged.
internal static class MarkdownRenderer
{
    [Flags]
    private enum TextStyle { None = 0, Bold = 1, Italic = 2, Underline = 4, Strike = 8 }

    // Inline pattern: order matters — composite/longer tokens first.
    // Groups: 1-2 ***bold+italic***, 3-4 **bold**, 5-6 *italic*,
    //         7-8 __underline__, 9-10 ~~strike~~,
    //         11-12 ```code``` (triple-backtick inline), 13-14 `code` (single-backtick inline),
    //         15-17 [text](url), 18 bare URL, 19-20 :emoji:
    private static readonly System.Text.RegularExpressions.Regex InlinePattern =
        new(@"(\*\*\*(.+?)\*\*\*)|(\*\*(.+?)\*\*)|(\*(.+?)\*)|(__(.+?)__)|(\~\~(.+?)\~\~)|(```(.+?)```)|(`(.+?)`)|(\[(.+?)\]\((https?://\S+?)\))|(https?://\S+)|(:([A-Za-z0-9_]+(?:~\d+)?):)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    // Regex for detecting emoji-only messages (after stripping shortcodes, only whitespace remains)
    private static readonly System.Text.RegularExpressions.Regex EmojiOnlyPattern =
        new(@"^(\s*:[A-Za-z0-9_]+(?:~\d+)?:\s*)+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    // Same pattern but with Singleline so . matches \n — used to detect spans that cross lines.
    // Backtick patterns are intentionally omitted: inline code does not span lines, and
    // including ``` here would cause fenced code blocks to be incorrectly collapsed.
    private static readonly System.Text.RegularExpressions.Regex MultilineInlineSpan =
        new(@"(\*\*\*(.+?)\*\*\*)|(\*\*(.+?)\*\*)|(\*(.+?)\*)|(__(.+?)__)|(\~\~(.+?)\~\~)",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

    private static readonly System.Text.RegularExpressions.Regex NumberedLine =
        new(@"^(\d+)\.\s", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex WholeLine_SingleCode =
        new(@"^`([^`]+)`$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex WholeLine_TripleCode =
        new(@"^```(.+)```$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Windows.Media.SolidColorBrush CodeBlockBg =
        new(System.Windows.Media.Color.FromRgb(0x1E, 0x1F, 0x22)); // BgDarkest

    private static Block MakeCodeBlock(string content, Brush textBrush, int startLine = 1)
    {
        var mutedBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x6D, 0x6F, 0x78)); // TextMuted
        var sepBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x3B, 0x3D, 0x43));

        string[] codeLines = content.Split('\n');
        string lineNums = string.Join("\n", Enumerable.Range(startLine, codeLines.Length));

        var lineNumTb = new System.Windows.Controls.TextBlock
        {
            Text              = lineNums,
            FontFamily        = new System.Windows.Media.FontFamily("Consolas"),
            Foreground        = mutedBrush,
            TextAlignment     = System.Windows.TextAlignment.Right,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
        };
        var sep = new System.Windows.Controls.Border
        {
            Width             = 1,
            Background        = sepBrush,
            Margin            = new Thickness(8, 0, 8, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
        };
        var codeTb = new System.Windows.Controls.TextBlock
        {
            Text              = content,
            FontFamily        = new System.Windows.Media.FontFamily("Consolas"),
            Foreground        = textBrush,
            TextWrapping      = System.Windows.TextWrapping.Wrap,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
        };

        var grid = new System.Windows.Controls.Grid();
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            { Width = System.Windows.GridLength.Auto });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            { Width = System.Windows.GridLength.Auto });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        System.Windows.Controls.Grid.SetColumn(lineNumTb, 0);
        System.Windows.Controls.Grid.SetColumn(sep,       1);
        System.Windows.Controls.Grid.SetColumn(codeTb,    2);
        grid.Children.Add(lineNumTb);
        grid.Children.Add(sep);
        grid.Children.Add(codeTb);

        return new BlockUIContainer(new System.Windows.Controls.Border
        {
            Background   = CodeBlockBg,
            CornerRadius = new CornerRadius(4),
            Padding      = new Thickness(10, 8, 10, 8),
            Margin       = new Thickness(0, 2, 0, 2),
            Child        = grid,
        });
    }

    public static bool IsEmojiOnly(string text, Services.EmojiService? emojiService)
    {
        if (emojiService is null || !emojiService.IsLoaded) return false;
        if (!EmojiOnlyPattern.IsMatch(text)) return false;
        // Verify all shortcodes actually resolve to emojis
        foreach (System.Text.RegularExpressions.Match m in
            System.Text.RegularExpressions.Regex.Matches(text, @":([A-Za-z0-9_]+(?:~\d+)?):"))
        {
            if (emojiService.Resolve(m.Groups[1].Value) is null) return false;
        }
        return true;
    }

    public static FlowDocument Render(string text, Brush textBrush, Brush linkBrush) =>
        Render(text, textBrush, linkBrush, null, 32);

    public static FlowDocument Render(string text, Brush textBrush, Brush linkBrush,
                                      Services.EmojiService? emojiService, int emojiSize = 32)
    {
        var doc = new FlowDocument();
        if (string.IsNullOrEmpty(text)) return doc;

        // Replace \n inside inline spans and alignment blocks with \x01 so they stay on one
        // "line" after splitting. \x01 is re-expanded to LineBreak inlines in ParseInlines.
        string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        string collapsed  = MultilineInlineSpan.Replace(normalized, m =>
            m.Value.Contains('\n') ? m.Value.Replace('\n', '\x01') : m.Value);
        string[] rawLines = collapsed.Split('\n');

        static int IndentLevel(string raw)
        {
            int spaces = 0;
            foreach (char c in raw)
            {
                if (c == ' ')  spaces++;
                else if (c == '\t') spaces += 4;
                else break;
            }
            return spaces / 4;
        }

        // Single-line message that is entirely an inline code span → render as code block
        if (rawLines.Length == 1)
        {
            var wm = WholeLine_TripleCode.Match(rawLines[0]);
            if (!wm.Success) wm = WholeLine_SingleCode.Match(rawLines[0]);
            if (wm.Success)
            {
                doc.Blocks.Add(MakeCodeBlock(wm.Groups[1].Value, textBrush));
                return doc;
            }
        }

        int i = 0;
        while (i < rawLines.Length)
        {
            string line = rawLines[i];

            // Opening fence: ``` or ```<number> (e.g. ```42 starts at line 42)
            string? fenceSuffix = line == "```" ? ""
                : (line.StartsWith("```") && line[3..].Trim() is { Length: > 0 } s
                   && s.All(char.IsDigit)) ? s : null;
            if (fenceSuffix != null)
            {
                int startLine = fenceSuffix.Length > 0 ? int.Parse(fenceSuffix) : 1;
                i++;
                var sb = new System.Text.StringBuilder();
                while (i < rawLines.Length && rawLines[i] != "```")
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(rawLines[i]);
                    i++;
                }
                if (i < rawLines.Length) i++; // consume closing ```
                doc.Blocks.Add(MakeCodeBlock(sb.ToString(), textBrush, startLine));
                continue;
            }

            if (line.StartsWith("> "))
            {
                // Collect consecutive quote lines into one block so the bar is continuous
                var contentTb = new System.Windows.Controls.TextBlock
                {
                    Foreground   = textBrush,
                    TextWrapping = System.Windows.TextWrapping.Wrap,
                };
                bool firstQuoteLine = true;
                while (i < rawLines.Length && rawLines[i].StartsWith("> "))
                {
                    if (!firstQuoteLine) contentTb.Inlines.Add(new LineBreak());
                    // ParseInlines fills a Paragraph (Documents.InlineCollection);
                    // migrate inlines to the TextBlock (Controls.InlineCollection)
                    var tempPara = new Paragraph();
                    ParseInlines(rawLines[i][2..], tempPara.Inlines, textBrush, linkBrush, TextStyle.None, emojiService, emojiSize);
                    foreach (var il in tempPara.Inlines.ToList())
                    {
                        tempPara.Inlines.Remove(il);
                        contentTb.Inlines.Add(il);
                    }
                    firstQuoteLine = false;
                    i++;
                }

                var bar = new System.Windows.Controls.Border
                {
                    Width             = 2,
                    Background        = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x4E, 0x50, 0x58)),
                    Margin            = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                };
                var grid = new System.Windows.Controls.Grid();
                grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
                    { Width = System.Windows.GridLength.Auto });
                grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
                    { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                System.Windows.Controls.Grid.SetColumn(bar,       0);
                System.Windows.Controls.Grid.SetColumn(contentTb, 1);
                grid.Children.Add(bar);
                grid.Children.Add(contentTb);
                doc.Blocks.Add(new BlockUIContainer(grid) { Margin = new Thickness(4, 2, 0, 2) });
                continue;
            }

            {
                string lineT = line.TrimStart(' ', '\t');
                if (lineT.StartsWith("- ") || lineT.StartsWith("* "))
                {
                    while (i < rawLines.Length)
                    {
                        string raw = rawLines[i];
                        string tr  = raw.TrimStart(' ', '\t');
                        if (!tr.StartsWith("- ") && !tr.StartsWith("* ")) break;
                        int level    = IndentLevel(raw);
                        var itemList = new List { MarkerStyle = TextMarkerStyle.None, Padding = new Thickness(20 + level * 16, 0, 0, 0) };
                        var lip      = new Paragraph { TextIndent = -14, Foreground = textBrush, Margin = new Thickness(0) };
                        lip.Inlines.Add(new Run("• "));
                        ParseInlines(tr[2..], lip.Inlines, textBrush, linkBrush, TextStyle.None, emojiService, emojiSize);
                        itemList.ListItems.Add(new ListItem(lip) { Margin = new Thickness(0), Padding = new Thickness(0) });
                        doc.Blocks.Add(itemList);
                        i++;
                    }
                    continue;
                }
            }

            {
                string lineT = line.TrimStart(' ', '\t');
                var m0 = NumberedLine.Match(lineT);
                if (m0.Success)
                {
                    while (i < rawLines.Length)
                    {
                        string raw = rawLines[i];
                        string tr  = raw.TrimStart(' ', '\t');
                        var m = NumberedLine.Match(tr);
                        if (!m.Success) break;
                        int level    = IndentLevel(raw);
                        string marker = m.Groups[1].Value + ". ";
                        string item   = tr[m.Length..];
                        var itemList = new List { MarkerStyle = TextMarkerStyle.None, Padding = new Thickness(20 + level * 16, 0, 0, 0) };
                        var lip      = new Paragraph { TextIndent = -14, Foreground = textBrush, Margin = new Thickness(0) };
                        lip.Inlines.Add(new Run(marker));
                        ParseInlines(item, lip.Inlines, textBrush, linkBrush, TextStyle.None, emojiService, emojiSize);
                        itemList.ListItems.Add(new ListItem(lip) { Margin = new Thickness(0), Padding = new Thickness(0) });
                        doc.Blocks.Add(itemList);
                        i++;
                    }
                    continue;
                }
            }

            var para = new Paragraph { Foreground = textBrush };
            ParseInlines(line, para.Inlines, textBrush, linkBrush, TextStyle.None, emojiService, emojiSize);
            doc.Blocks.Add(para);
            i++;
        }

        return doc;
    }

    // Recurse into each span's content so nested formats (e.g. **__text__**) compose correctly.
    private static void ParseInlines(string text, InlineCollection inlines,
                                     Brush textBrush, Brush linkBrush,
                                     TextStyle style = TextStyle.None,
                                     Services.EmojiService? emojiService = null,
                                     int emojiSize = 32)
    {
        text = text.Replace("\\:", ":").Replace("\\\\", "\\");
        int lastEnd = 0;
        foreach (System.Text.RegularExpressions.Match m in InlinePattern.Matches(text))
        {
            if (m.Index > lastEnd)
                AddInlines(text[lastEnd..m.Index], inlines, textBrush, style);

            if (m.Groups[1].Success)       // ***bold+italic***
                ParseInlines(m.Groups[2].Value,  inlines, textBrush, linkBrush, style | TextStyle.Bold | TextStyle.Italic, emojiService, emojiSize);
            else if (m.Groups[3].Success)  // **bold**
                ParseInlines(m.Groups[4].Value,  inlines, textBrush, linkBrush, style | TextStyle.Bold, emojiService, emojiSize);
            else if (m.Groups[5].Success)  // *italic*
                ParseInlines(m.Groups[6].Value,  inlines, textBrush, linkBrush, style | TextStyle.Italic, emojiService, emojiSize);
            else if (m.Groups[7].Success)  // __underline__
                ParseInlines(m.Groups[8].Value,  inlines, textBrush, linkBrush, style | TextStyle.Underline, emojiService, emojiSize);
            else if (m.Groups[9].Success)  // ~~strike~~
                ParseInlines(m.Groups[10].Value, inlines, textBrush, linkBrush, style | TextStyle.Strike, emojiService, emojiSize);
            else if (m.Groups[11].Success) // ```code``` (triple-backtick inline)
                inlines.Add(MakeCodeRun(m.Groups[12].Value, textBrush));
            else if (m.Groups[13].Success) // `code` (single-backtick inline)
                inlines.Add(MakeCodeRun(m.Groups[14].Value, textBrush));
            else if (m.Groups[15].Success) // [text](url)
                inlines.Add(MakeLink(m.Groups[16].Value, m.Groups[17].Value, linkBrush));
            else if (m.Groups[19].Success) // :emoji:
            {
                string shortcode = m.Groups[20].Value;
                var emoji = emojiService?.Resolve(shortcode);
                if (emoji is not null)
                    inlines.Add(MakeEmojiInline(emoji, emojiService!, emojiSize));
                else
                    AddInlines(m.Value, inlines, textBrush, style); // unresolved — show as text
            }
            else                           // bare URL
                inlines.Add(MakeLink(m.Value, m.Value, linkBrush));

            lastEnd = m.Index + m.Length;
        }

        if (lastEnd < text.Length)
            AddInlines(text[lastEnd..], inlines, textBrush, style);

        if (!inlines.Any())
            inlines.Add(MakeRun(string.Empty, textBrush, style));
    }

    // Adds text to inlines, expanding \x01 placeholders (collapsed newlines) into LineBreaks.
    private static void AddInlines(string text, InlineCollection inlines, Brush brush, TextStyle style)
    {
        string[] parts = text.Split('\x01');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) inlines.Add(new LineBreak());
            if (parts[i].Length > 0) inlines.Add(MakeRun(parts[i], brush, style));
        }
    }

    private static Run MakeRun(string text, Brush brush, TextStyle style)
    {
        var run = new Run(text) { Foreground = brush };
        if (style.HasFlag(TextStyle.Bold))   run.FontWeight = FontWeights.Bold;
        if (style.HasFlag(TextStyle.Italic)) run.FontStyle  = FontStyles.Italic;
        var deco = new TextDecorationCollection();
        if (style.HasFlag(TextStyle.Underline)) foreach (var d in TextDecorations.Underline)      deco.Add(d);
        if (style.HasFlag(TextStyle.Strike))    foreach (var d in TextDecorations.Strikethrough)  deco.Add(d);
        if (deco.Count > 0) run.TextDecorations = deco;
        return run;
    }

    private static Inline MakeCodeRun(string text, Brush brush)
    {
        var tb = new System.Windows.Controls.TextBlock
        {
            Text       = text,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            Foreground = brush,
        };
        return new InlineUIContainer(new System.Windows.Controls.Border
        {
            Background   = CodeBlockBg,
            CornerRadius = new CornerRadius(3),
            Padding      = new Thickness(4, 1, 4, 1),
            Margin       = new Thickness(1, 0, 1, 0),
            Child        = tb,
        }) { BaselineAlignment = BaselineAlignment.Center };
    }

    private static Inline MakeEmojiInline(Shared.EmojiDto emoji, Services.EmojiService emojiService, int size)
    {
        var img = new System.Windows.Controls.Image
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Tag = $":{emoji.Name}:",
        };

        var tt = new ToolTip
        {
            Content = $":{emoji.Name}:",
            Placement = PlacementMode.Mouse,
            Template = Application.Current?.FindResource("TooltipTemplateNoTail") as ControlTemplate,
        };
        img.ToolTip = tt;
        ToolTipService.SetInitialShowDelay(img, 0);

        // Load image async
        var cached = emojiService.GetCachedImage(emoji);
        if (cached is not null)
        {
            img.Source = cached;
        }
        else
        {
            _ = Task.Run(async () =>
            {
                var bmp = await emojiService.GetImageAsync(emoji);
                if (bmp is not null)
                    Application.Current?.Dispatcher.Invoke(() => img.Source = bmp);
            });
        }

        return new InlineUIContainer(img) { BaselineAlignment = BaselineAlignment.TextBottom };
    }

    private static Hyperlink MakeLink(string label, string url, Brush brush)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return new Hyperlink(new Run(label)) { Foreground = brush };

        var tt = new ToolTip
        {
            Content = uri.AbsoluteUri,
            Placement = PlacementMode.Mouse,
            Template = Application.Current?.FindResource("TooltipTemplateNoTail") as ControlTemplate,
        };
        var link = new Hyperlink(new Run(label)) { NavigateUri = uri, Foreground = brush, ToolTip = tt };
        ToolTipService.SetInitialShowDelay(link, 500);
        link.RequestNavigate += (_, e) =>
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        return link;
    }
}
