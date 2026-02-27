using System.IO;
using System.Xml.Linq;
using Xunit;

namespace Chaos.Client.Tests;

/// <summary>
/// Verifies that chat messages are rendered inside a read-only RichTextBox,
/// which makes all text selectable. The old DataTemplate+TextBox approach has
/// been replaced by a FlowDocument built entirely in code-behind.
/// </summary>
public class SelectableTextTests
{
    private static readonly XNamespace Wpf  = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X    = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Atch = "http://schemas.microsoft.com/winfx/2006/xaml/presentation"; // same ns, just for clarity

    private static XDocument LoadMainWindowXaml()
    {
        var xamlPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                         "Chaos.Client", "MainWindow.xaml"));
        return XDocument.Load(xamlPath);
    }

    private static XElement GetMessageList(XDocument doc) =>
        doc.Descendants(Wpf + "RichTextBox")
           .First(e => (string?)e.Attribute(X + "Name") == "MessageList");

    // ── MessageItemTemplate is gone ───────────────────────────────────────────

    [Fact]
    public void MessageItemTemplate_NoLongerExists()
    {
        var doc = LoadMainWindowXaml();
        var template = doc.Descendants(Wpf + "DataTemplate")
            .FirstOrDefault(e => (string?)e.Attribute(X + "Key") == "MessageItemTemplate");
        Assert.Null(template);
    }

    // ── MessageList RichTextBox ───────────────────────────────────────────────

    [Fact]
    public void MessageList_ExistsAsRichTextBox()
    {
        var doc = LoadMainWindowXaml();
        var element = GetMessageList(doc);
        Assert.NotNull(element);
    }

    [Fact]
    public void MessageList_IsReadOnly()
    {
        var element = GetMessageList(LoadMainWindowXaml());
        Assert.Equal("True", (string?)element.Attribute("IsReadOnly"));
    }

    [Fact]
    public void MessageList_HasVerticalScrollBar()
    {
        var element = GetMessageList(LoadMainWindowXaml());
        // Attached properties are stored as dotted local names in XAML XML.
        var raw = element.Attributes()
            .FirstOrDefault(a => a.Name.LocalName == "ScrollViewer.VerticalScrollBarVisibility");
        Assert.NotNull(raw);
        Assert.Equal("Auto", raw!.Value);
    }

    [Fact]
    public void MessageList_HorizontalScrollBarDisabled()
    {
        var element = GetMessageList(LoadMainWindowXaml());
        var raw = element.Attributes()
            .FirstOrDefault(a => a.Name.LocalName == "ScrollViewer.HorizontalScrollBarVisibility");
        Assert.NotNull(raw);
        Assert.Equal("Disabled", raw!.Value);
    }
}
