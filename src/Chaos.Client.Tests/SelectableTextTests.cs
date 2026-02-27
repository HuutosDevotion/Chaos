using System.Xml.Linq;
using Xunit;

namespace Chaos.Client.Tests;

/// <summary>
/// Verifies that message text is rendered with selectable, read-only TextBox
/// elements rather than non-selectable TextBlock elements.
/// </summary>
public class SelectableTextTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X   = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument LoadMainWindowXaml()
    {
        // From bin/Debug/net8.0-windows/ navigate up to src/, then into Chaos.Client/
        var xamlPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                         "Chaos.Client", "MainWindow.xaml"));
        return XDocument.Load(xamlPath);
    }

    private static XElement GetMessageItemTemplate(XDocument doc) =>
        doc.Descendants(Wpf + "DataTemplate")
           .First(e => (string?)e.Attribute(X + "Key") == "MessageItemTemplate");

    private static string TextAttr(XElement e) =>
        (string?)e.Attribute("Text") ?? "";

    // ── author ────────────────────────────────────────────────────────────────

    [Fact]
    public void MessageItemTemplate_AuthorUsesTextBox_NotTextBlock()
    {
        var template = GetMessageItemTemplate(LoadMainWindowXaml());

        var authorNode = template.Descendants(Wpf + "TextBox")
            .FirstOrDefault(e => TextAttr(e).Contains("Author"));

        Assert.NotNull(authorNode);
    }

    [Fact]
    public void MessageItemTemplate_AuthorIsNotRenderedAsTextBlock()
    {
        var template = GetMessageItemTemplate(LoadMainWindowXaml());

        var textBlockWithAuthor = template.Descendants(Wpf + "TextBlock")
            .Any(e => TextAttr(e).Contains("Author"));

        Assert.False(textBlockWithAuthor,
            "Author must use TextBox (selectable), not TextBlock.");
    }

    [Fact]
    public void MessageItemTemplate_AuthorTextBox_IsReadOnly()
    {
        var template = GetMessageItemTemplate(LoadMainWindowXaml());

        var authorTextBox = template.Descendants(Wpf + "TextBox")
            .First(e => TextAttr(e).Contains("Author"));

        Assert.Equal("True", (string?)authorTextBox.Attribute("IsReadOnly"));
    }

    [Fact]
    public void MessageItemTemplate_AuthorBinding_IsOneWay()
    {
        var template = GetMessageItemTemplate(LoadMainWindowXaml());

        var authorTextBox = template.Descendants(Wpf + "TextBox")
            .First(e => TextAttr(e).Contains("Author"));

        Assert.Contains("Mode=OneWay", TextAttr(authorTextBox));
    }

    // ── content ───────────────────────────────────────────────────────────────

    [Fact]
    public void MessageItemTemplate_ContentUsesTextBox_NotTextBlock()
    {
        var template = GetMessageItemTemplate(LoadMainWindowXaml());

        var contentNode = template.Descendants(Wpf + "TextBox")
            .FirstOrDefault(e => TextAttr(e).Contains("Content"));

        Assert.NotNull(contentNode);
    }

    [Fact]
    public void MessageItemTemplate_ContentIsNotRenderedAsTextBlock()
    {
        var template = GetMessageItemTemplate(LoadMainWindowXaml());

        var textBlockWithContent = template.Descendants(Wpf + "TextBlock")
            .Any(e => TextAttr(e).Contains("Content"));

        Assert.False(textBlockWithContent,
            "Content must use TextBox (selectable), not TextBlock.");
    }

    [Fact]
    public void MessageItemTemplate_ContentTextBox_IsReadOnly()
    {
        var template = GetMessageItemTemplate(LoadMainWindowXaml());

        var contentTextBox = template.Descendants(Wpf + "TextBox")
            .First(e => TextAttr(e).Contains("Content"));

        Assert.Equal("True", (string?)contentTextBox.Attribute("IsReadOnly"));
    }

    [Fact]
    public void MessageItemTemplate_ContentBinding_IsOneWay()
    {
        var template = GetMessageItemTemplate(LoadMainWindowXaml());

        var contentTextBox = template.Descendants(Wpf + "TextBox")
            .First(e => TextAttr(e).Contains("Content"));

        Assert.Contains("Mode=OneWay", TextAttr(contentTextBox));
    }
}
