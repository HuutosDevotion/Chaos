using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using Chaos.Client.ViewModels;

namespace Chaos.Client;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            ((INotifyCollectionChanged)vm.Messages).CollectionChanged += (_, _) =>
            {
                if (MessageList.Items.Count > 0)
                    MessageList.ScrollIntoView(MessageList.Items[^1]);
            };
        }
    }

    private void MessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel vm)
        {
            if (vm.SendMessageCommand.CanExecute(null))
                vm.SendMessageCommand.Execute(null);
            e.Handled = true;
        }
    }
}
