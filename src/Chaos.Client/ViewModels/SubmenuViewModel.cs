using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Chaos.Client.ViewModels;

public abstract class SubmenuViewModel : INotifyPropertyChanged
{
    private bool _isOpen;

    public bool IsOpen
    {
        get => _isOpen;
        set { if (_isOpen == value) return; _isOpen = value; OnPropertyChanged(); }
    }

    public double Width { get; set; } = 300;
    public double Height { get; set; } = double.NaN;
    public double MaxHeight { get; set; } = double.NaN;

    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;
    public void Toggle() => IsOpen = !IsOpen;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
