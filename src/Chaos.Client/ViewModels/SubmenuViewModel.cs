using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace Chaos.Client.ViewModels;

public abstract class SubmenuViewModel : INotifyPropertyChanged
{
    private bool _isOpen;

    /// <summary>Whether the submenu is currently open. Bind Popup.IsOpen two-way to this.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        set { if (_isOpen == value) return; _isOpen = value; OnPropertyChanged(); }
    }

    /// <summary>The element the submenu should appear relative to.</summary>
    public UIElement? PlacementTarget { get; set; }

    /// <summary>Popup placement mode (default: Bottom).</summary>
    public PlacementMode Placement { get; set; } = PlacementMode.Bottom;

    public double HorizontalOffset { get; set; }
    public double VerticalOffset { get; set; }

    /// <summary>Popup width. Subclass sets in constructor.</summary>
    public double Width { get; set; } = 300;

    /// <summary>Popup height. NaN = auto height.</summary>
    public double Height { get; set; } = double.NaN;

    /// <summary>Maximum height before scrolling. NaN = unconstrained.</summary>
    public double MaxHeight { get; set; } = double.NaN;

    /// <summary>Open the submenu relative to the given element.</summary>
    public void Open(UIElement placementTarget)
    {
        PlacementTarget = placementTarget;
        IsOpen = true;
    }

    /// <summary>Close the submenu.</summary>
    public void Close()
    {
        IsOpen = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
