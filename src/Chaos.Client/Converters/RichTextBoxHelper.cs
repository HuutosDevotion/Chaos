using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Chaos.Client.Converters;

/// <summary>
/// Attached property that makes RichTextBox.Document data-bindable,
/// since the native property is not a DependencyProperty.
/// </summary>
public static class RichTextBoxHelper
{
    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.RegisterAttached(
            "Document",
            typeof(FlowDocument),
            typeof(RichTextBoxHelper),
            new PropertyMetadata(null, (d, e) =>
            {
                if (d is not RichTextBox rtb || e.NewValue is not FlowDocument doc) return;

                // A FlowDocument can only belong to one RichTextBox at a time.
                // When a DataTemplate RichTextBox is destroyed (e.g. modal closed), WPF does
                // not clear the document's parent reference, so the next instantiation of the
                // template finds the document already "owned". Detach it from the stale owner
                // first so the new RichTextBox can adopt it.
                if (doc.Parent is RichTextBox prevOwner)
                    prevOwner.Document = new FlowDocument();

                rtb.Document = doc;
            }));

    public static FlowDocument GetDocument(DependencyObject obj) =>
        (FlowDocument)obj.GetValue(DocumentProperty);

    public static void SetDocument(DependencyObject obj, FlowDocument value) =>
        obj.SetValue(DocumentProperty, value);
}
