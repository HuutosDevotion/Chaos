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

        // If the element's Tag holds a string, use it as the tooltip text.
        // Tag is bound on the Button (correct DataContext) so this always resolves,
        // avoiding the DataContext inheritance problem inside the popup.
        if (element.Tag is string tag && tag.Length > 0)
            tooltip.Content = tag;

        // Measure so the height reflects the actual content.
        tooltip.ApplyTemplate();
        tooltip.Measure(new Size(200, double.PositiveInfinity));

        // Placement="Center" handles horizontal centering automatically.
        // Set VerticalOffset so the tail tip sits flush against the element's top edge.
        if (tooltip.DesiredSize.Height > 0)
            tooltip.VerticalOffset = -(element.ActualHeight / 2 + tooltip.DesiredSize.Height / 2);
    }
}
