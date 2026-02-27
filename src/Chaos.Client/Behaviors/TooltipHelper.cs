using System.Windows;
using System.Windows.Controls;

namespace Chaos.Client.Behaviors;

public static class TooltipHelper
{
    public static readonly DependencyProperty CenterOnElementProperty =
        DependencyProperty.RegisterAttached(
            "CenterOnElement",
            typeof(bool),
            typeof(TooltipHelper),
            new PropertyMetadata(false, OnCenterOnElementChanged));

    public static bool GetCenterOnElement(DependencyObject obj) =>
        (bool)obj.GetValue(CenterOnElementProperty);

    public static void SetCenterOnElement(DependencyObject obj, bool value) =>
        obj.SetValue(CenterOnElementProperty, value);

    private static void OnCenterOnElementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            element.ToolTipOpening -= OnToolTipOpening;
            if ((bool)e.NewValue)
                element.ToolTipOpening += OnToolTipOpening;
        }
    }

    private static void OnToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ToolTip is not ToolTip tooltip) return;

        // Force the template so Measure has something to work with.
        tooltip.ApplyTemplate();
        tooltip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        // ToolTipOpening fires before PopupControlService configures the popup, so offsets
        // set here are picked up before positioning. Write to both the ToolTip's own
        // HorizontalOffset *and* the ToolTipService attached property on the owner — different
        // WPF versions/code paths may read either one.
        Apply(element, tooltip, tooltip.DesiredSize.Width);

        // Opened fires once the popup is visible and laid out. ActualWidth is the real
        // rendered width, correcting any inaccuracy from Measure (e.g. unresolved bindings).
        RoutedEventHandler? handler = null;
        handler = (_, _) =>
        {
            tooltip.Opened -= handler;
            Apply(element, tooltip, tooltip.ActualWidth);
        };
        tooltip.Opened += handler;
    }

    private static void Apply(FrameworkElement element, ToolTip tooltip, double tooltipWidth)
    {
        if (tooltipWidth <= 0) return;
        double offset = (element.ActualWidth - tooltipWidth) / 2;
        tooltip.HorizontalOffset = offset;
        ToolTipService.SetHorizontalOffset(element, offset);
    }
}
