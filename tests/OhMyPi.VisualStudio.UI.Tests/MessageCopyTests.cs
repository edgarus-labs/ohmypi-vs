using Omp.Core;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static OhMyPi.VisualStudio.UI.Tests.Wpf;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>The copy button of a message copies the whole message.</summary>
[Collection("wpf")]
public sealed class MessageCopyTests
{
    private static bool IsCopy(Button button) => System.Windows.Automation.AutomationProperties.GetName(button) == "Copy message";

    /// <summary>The nearest ancestor of <paramref name="inner"/> that holds a copy button.</summary>
    private static DependencyObject Message(DependencyObject inner)
    {
        for (var node = VisualTreeHelper.GetParent(inner); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (Descendants(node).OfType<Button>().Any(IsCopy))
            {
                return node;
            }
        }

        throw new Xunit.Sdk.XunitException("no message around the element");
    }

    [Fact]
    public void Copying_a_message_puts_the_whole_message_on_the_clipboard()
    {
        var service = new FakeService
        {
            Transcript = new TranscriptItem[]
            {
                new UserItem { Id = "u1", Text = "Fix the bug" },
                new AssistantItem { Id = "a1", Text = "Done.\n\nAll good." },
            },
        };
        RunSta((window, control) =>
        {
            var answer = Descendants(window).OfType<RichTextBox>().Single(r => r.IsVisible && new System.Windows.Documents.TextRange(r.Document.ContentStart, r.Document.ContentEnd).Text.Contains("All good."));
            var copied = "";
            control.SetClipboard = text => copied = text;
            Click(Descendants(Message(answer)).OfType<Button>().Single(IsCopy));
            Pump();
            Assert.Equal("Done.\n\nAll good.", copied);

            Click(Descendants(Message(Named<Border>(window, "You"))).OfType<Button>().Single(IsCopy));
            Pump();
            Assert.Equal("Fix the bug", copied);
        }, service, new FakeHost());
    }
}
