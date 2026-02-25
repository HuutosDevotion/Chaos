using System.Windows;
using Chaos.Shared;

namespace Chaos.Client;

public partial class ChannelDialog : Window
{
    public string ChannelName { get; private set; } = string.Empty;
    public ChannelType SelectedType { get; private set; } = ChannelType.Text;

    public ChannelDialog(string title, string initialName, bool showTypeSelector)
    {
        InitializeComponent();
        Title = title;
        NameBox.Text = initialName;
        TypePanel.Visibility = showTypeSelector ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Channel name cannot be empty.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ChannelName = name;
        SelectedType = VoiceRadio.IsChecked == true ? ChannelType.Voice : ChannelType.Text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
