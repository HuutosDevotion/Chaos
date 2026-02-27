using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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

            // New messages always scroll to bottom (also a fallback attach in case
            // CollectionChanged fires before SelectedTextChannel does).
            ((INotifyCollectionChanged)vm.Messages).CollectionChanged += (_, _) =>
            {
                AttachChatScroll();
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
}
