using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chaos.Client.ViewModels;
using Chaos.Shared;

namespace Chaos.Client;

public partial class MainWindow : Window
{
    private static readonly string[] _imageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };

    public MainWindow()
    {
        InitializeComponent();
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico"));
        Loaded += OnLoaded;
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is MainViewModel vm)
            await vm.DisposeAsync();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            ((INotifyCollectionChanged)vm.Messages).CollectionChanged += (_, _) =>
            {
                FindScrollViewer(MessageList)?.ScrollToBottom();
            };

            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.SelectedSuggestionIndex))
                {
                    int idx = vm.SelectedSuggestionIndex;
                    if (idx >= 0 && idx < vm.SlashSuggestions.Count)
                        SuggestionList.ScrollIntoView(vm.SlashSuggestions[idx]);
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

    private void MessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel vm)
        {
            if (vm.ShowSlashSuggestions && vm.SelectedSuggestionIndex >= 0)
            {
                vm.SelectSuggestion(vm.SlashSuggestions[vm.SelectedSuggestionIndex]);
                MessageInput.CaretIndex = MessageInput.Text.Length;
                e.Handled = true;
                return;
            }

            if (vm.SendMessageCommand.CanExecute(null))
                vm.SendMessageCommand.Execute(null);
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
        }

        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && Clipboard.ContainsImage())
        {
            e.Handled = true;
            var bmp = Clipboard.GetImage();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            if (DataContext is MainViewModel vm2)
                vm2.SetPendingImage(ms.ToArray(), "clipboard.png", bmp);
        }
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
