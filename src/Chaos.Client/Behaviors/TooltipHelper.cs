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

        if (element.Tag is string tag && tag.Length > 0)
            tooltip.Content = tag;

        tooltip.ApplyTemplate();
        tooltip.Measure(new Size(200, double.PositiveInfinity));

        if (tooltip.DesiredSize.Height <= 0) return;

        double halfTarget = element.ActualHeight / 2;
        double halfTip    = tooltip.DesiredSize.Height / 2;

        var    elementTopScreen   = element.PointToScreen(new Point(0, 0)).Y;
        double tooltipTopOnScreen = elementTopScreen - tooltip.DesiredSize.Height;
        bool   flip               = tooltipTopOnScreen < SystemParameters.WorkArea.Top;

        if (flip)
            tooltip.Template = (ControlTemplate)Application.Current.Resources["TooltipTemplateFlipped"];
        else
            tooltip.ClearValue(ToolTip.TemplateProperty);

        tooltip.VerticalOffset = flip
            ? halfTarget + halfTip      // bubble below element, tail points up
            : -(halfTarget + halfTip);  // bubble above element, tail points down
    }
}
