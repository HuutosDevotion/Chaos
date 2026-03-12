using System.Windows;
using System.Windows.Controls;
using Chaos.Client.Models;
using Chaos.Client.Services;
using Chaos.Shared;

namespace Chaos.Client;

public partial class ScreenShareDialog : Window
{
    private readonly List<CaptureTarget> _screens;
    private readonly List<CaptureTarget> _windows;
    private CaptureTarget? _selectedTarget;

    public StreamQuality SelectedQuality { get; private set; } = StreamQuality.Medium;
    public CaptureTarget? SelectedTarget => _selectedTarget;

    public ScreenShareDialog()
    {
        InitializeComponent();

        _screens = CaptureEnumerator.GetScreens();
        _windows = CaptureEnumerator.GetWindows();

        PopulateScreens();
        PopulateWindows();
    }

    private void PopulateScreens()
    {
        ScreensList.Children.Clear();
        foreach (var screen in _screens)
        {
            var rb = new RadioButton
            {
                Style = (Style)FindResource("ScreenCard"),
                DataContext = screen,
                GroupName = "CaptureTarget"
            };
            rb.Checked += (_, _) =>
            {
                _selectedTarget = screen;
                // Uncheck window cards
                foreach (var child in WindowsList.Children)
                    if (child is RadioButton wrb) wrb.IsChecked = false;
                GoLiveButton.IsEnabled = true;
            };
            ScreensList.Children.Add(rb);
        }
    }

    private void PopulateWindows()
    {
        WindowsList.Children.Clear();
        foreach (var window in _windows)
        {
            var rb = new RadioButton
            {
                Style = (Style)FindResource("ScreenCard"),
                DataContext = window,
                GroupName = "CaptureTarget"
            };
            rb.Checked += (_, _) =>
            {
                _selectedTarget = window;
                // Uncheck screen cards
                foreach (var child in ScreensList.Children)
                    if (child is RadioButton srb) srb.IsChecked = false;
                GoLiveButton.IsEnabled = true;
            };
            WindowsList.Children.Add(rb);
        }
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (ScreensPanel is null || WindowsPanel is null) return;

        if (sender == ScreensTab)
        {
            ScreensPanel.Visibility = Visibility.Visible;
            WindowsPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            ScreensPanel.Visibility = Visibility.Collapsed;
            WindowsPanel.Visibility = Visibility.Visible;
        }
    }

    private void GoLive_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTarget is null) return;

        SelectedQuality = QualityCombo.SelectedIndex switch
        {
            0 => StreamQuality.Low,
            1 => StreamQuality.Medium,
            2 => StreamQuality.High,
            _ => StreamQuality.Medium
        };
        DialogResult = true;
    }
}
