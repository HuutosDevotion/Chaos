using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Chaos.Client.Controls;

public enum SubmenuPlacement { Top, Bottom, Left, Right }

public class SubmenuHost : ContentControl
{
    private Grid? _overlay;
    private Canvas? _canvas;
    private Border? _contentBorder;

    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(SubmenuHost),
            new FrameworkPropertyMetadata(false,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnIsOpenChanged));

    public static readonly DependencyProperty PlacementTargetProperty =
        DependencyProperty.Register(nameof(PlacementTarget), typeof(UIElement), typeof(SubmenuHost));

    public static readonly DependencyProperty PlacementProperty =
        DependencyProperty.Register(nameof(Placement), typeof(SubmenuPlacement), typeof(SubmenuHost),
            new PropertyMetadata(SubmenuPlacement.Bottom));

    public static readonly DependencyProperty HorizontalOffsetProperty =
        DependencyProperty.Register(nameof(HorizontalOffset), typeof(double), typeof(SubmenuHost),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.Register(nameof(VerticalOffset), typeof(double), typeof(SubmenuHost),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty SubmenuWidthProperty =
        DependencyProperty.Register(nameof(SubmenuWidth), typeof(double), typeof(SubmenuHost),
            new PropertyMetadata(300.0));

    public static readonly DependencyProperty SubmenuHeightProperty =
        DependencyProperty.Register(nameof(SubmenuHeight), typeof(double), typeof(SubmenuHost),
            new PropertyMetadata(double.NaN));

    public static readonly DependencyProperty SubmenuMaxHeightProperty =
        DependencyProperty.Register(nameof(SubmenuMaxHeight), typeof(double), typeof(SubmenuHost),
            new PropertyMetadata(double.NaN));

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public UIElement? PlacementTarget
    {
        get => (UIElement?)GetValue(PlacementTargetProperty);
        set => SetValue(PlacementTargetProperty, value);
    }

    public SubmenuPlacement Placement
    {
        get => (SubmenuPlacement)GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    public double HorizontalOffset
    {
        get => (double)GetValue(HorizontalOffsetProperty);
        set => SetValue(HorizontalOffsetProperty, value);
    }

    public double VerticalOffset
    {
        get => (double)GetValue(VerticalOffsetProperty);
        set => SetValue(VerticalOffsetProperty, value);
    }

    public double SubmenuWidth
    {
        get => (double)GetValue(SubmenuWidthProperty);
        set => SetValue(SubmenuWidthProperty, value);
    }

    public double SubmenuHeight
    {
        get => (double)GetValue(SubmenuHeightProperty);
        set => SetValue(SubmenuHeightProperty, value);
    }

    public double SubmenuMaxHeight
    {
        get => (double)GetValue(SubmenuMaxHeightProperty);
        set => SetValue(SubmenuMaxHeightProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Unhook old handlers
        if (_overlay != null)
            _overlay.MouseLeftButtonDown -= Overlay_MouseLeftButtonDown;
        if (_contentBorder != null)
        {
            _contentBorder.MouseLeftButtonDown -= ContentBorder_MouseLeftButtonDown;
            _contentBorder.SizeChanged -= ContentBorder_SizeChanged;
        }

        _overlay = GetTemplateChild("PART_Overlay") as Grid;
        _canvas = GetTemplateChild("PART_Canvas") as Canvas;
        _contentBorder = GetTemplateChild("PART_ContentBorder") as Border;

        if (_overlay != null)
            _overlay.MouseLeftButtonDown += Overlay_MouseLeftButtonDown;

        if (_contentBorder != null)
        {
            _contentBorder.MouseLeftButtonDown += ContentBorder_MouseLeftButtonDown;
            _contentBorder.SizeChanged += ContentBorder_SizeChanged;
        }

        // Apply initial state
        if (_overlay != null)
            _overlay.Visibility = IsOpen ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SubmenuHost host)
            host.OnIsOpenChanged((bool)e.NewValue);
    }

    private void OnIsOpenChanged(bool isOpen)
    {
        if (_overlay == null) return;

        if (isOpen)
        {
            _overlay.Visibility = Visibility.Visible;
            PositionContent();
            HookWindowEvents();
        }
        else
        {
            _overlay.Visibility = Visibility.Collapsed;
            UnhookWindowEvents();
        }
    }

    private Window? _parentWindow;

    private void HookWindowEvents()
    {
        _parentWindow = Window.GetWindow(this);
        if (_parentWindow != null)
        {
            _parentWindow.SizeChanged += ParentWindow_SizeChanged;
            _parentWindow.PreviewKeyDown += ParentWindow_PreviewKeyDown;
        }
    }

    private void UnhookWindowEvents()
    {
        if (_parentWindow != null)
        {
            _parentWindow.SizeChanged -= ParentWindow_SizeChanged;
            _parentWindow.PreviewKeyDown -= ParentWindow_PreviewKeyDown;
            _parentWindow = null;
        }
    }

    private void ParentWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsOpen)
            PositionContent();
    }

    private void ParentWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsOpen)
        {
            IsOpen = false;
            e.Handled = true;
        }
    }

    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        IsOpen = false;
        e.Handled = true;
    }

    private void ContentBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Prevent click-through to overlay
        e.Handled = true;
    }

    private void ContentBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Reposition when content size changes (e.g., auto-height resolved)
        if (IsOpen)
            PositionContent();
    }

    private void PositionContent()
    {
        if (_overlay == null || _canvas == null || _contentBorder == null)
            return;

        // Ensure layout is up to date
        _overlay.UpdateLayout();

        double overlayWidth = _overlay.ActualWidth;
        double overlayHeight = _overlay.ActualHeight;

        if (overlayWidth <= 0 || overlayHeight <= 0)
            return;

        // Size the canvas to fill the overlay
        _canvas.Width = overlayWidth;
        _canvas.Height = overlayHeight;

        // Get target position in overlay coordinates
        double targetX = 0, targetY = 0;
        double targetW = 0, targetH = 0;

        if (PlacementTarget != null)
        {
            try
            {
                var transform = PlacementTarget.TransformToAncestor(_overlay);
                var targetPos = transform.Transform(new Point(0, 0));
                targetX = targetPos.X;
                targetY = targetPos.Y;

                if (PlacementTarget is FrameworkElement fe)
                {
                    targetW = fe.ActualWidth;
                    targetH = fe.ActualHeight;
                }
            }
            catch
            {
                // If transform fails (e.g., not in same visual tree), fall back to 0,0
            }
        }

        double panelWidth = SubmenuWidth;
        double panelHeight = SubmenuHeight;
        double panelMaxHeight = SubmenuMaxHeight;

        // Calculate desired position based on placement
        double desiredLeft, desiredTop;

        switch (Placement)
        {
            case SubmenuPlacement.Top:
                desiredLeft = targetX;
                desiredTop = targetY; // Will be adjusted after measuring
                break;
            case SubmenuPlacement.Left:
                desiredLeft = targetX - panelWidth;
                desiredTop = targetY;
                break;
            case SubmenuPlacement.Right:
                desiredLeft = targetX + targetW;
                desiredTop = targetY;
                break;
            default: // Bottom
                desiredLeft = targetX;
                desiredTop = targetY + targetH;
                break;
        }

        // Apply offsets
        desiredLeft += HorizontalOffset;
        desiredTop += VerticalOffset;

        // Constrain width: if right edge exceeds overlay, shift left
        if (desiredLeft + panelWidth > overlayWidth)
            desiredLeft = overlayWidth - panelWidth;
        if (desiredLeft < 0)
        {
            desiredLeft = 0;
            panelWidth = Math.Min(panelWidth, overlayWidth);
        }

        // Constrain height based on placement direction
        double availableHeight;
        if (Placement == SubmenuPlacement.Top)
        {
            // For Top placement, content goes above the target
            availableHeight = targetY + VerticalOffset;
            if (!double.IsNaN(panelMaxHeight))
                panelMaxHeight = Math.Min(panelMaxHeight, availableHeight);
            else
                panelMaxHeight = availableHeight;

            if (!double.IsNaN(panelHeight))
                panelHeight = Math.Min(panelHeight, availableHeight);
        }
        else
        {
            availableHeight = overlayHeight - desiredTop;
            if (!double.IsNaN(panelMaxHeight))
                panelMaxHeight = Math.Min(panelMaxHeight, availableHeight);
            else
                panelMaxHeight = availableHeight;

            if (!double.IsNaN(panelHeight))
                panelHeight = Math.Min(panelHeight, availableHeight);
        }

        if (desiredTop < 0)
            desiredTop = 0;

        // Apply size
        _contentBorder.Width = panelWidth;

        if (!double.IsNaN(panelHeight))
            _contentBorder.Height = panelHeight;
        else
            _contentBorder.ClearValue(HeightProperty);

        if (!double.IsNaN(panelMaxHeight) && panelMaxHeight > 0)
            _contentBorder.MaxHeight = panelMaxHeight;
        else
            _contentBorder.ClearValue(MaxHeightProperty);

        // For Top placement, position above the target after measuring
        if (Placement == SubmenuPlacement.Top)
        {
            _contentBorder.UpdateLayout();
            double actualHeight = _contentBorder.ActualHeight;
            if (actualHeight > 0)
                desiredTop = targetY + VerticalOffset - actualHeight;

            if (desiredTop < 0)
                desiredTop = 0;
        }

        Canvas.SetLeft(_contentBorder, desiredLeft);
        Canvas.SetTop(_contentBorder, desiredTop);
    }
}
