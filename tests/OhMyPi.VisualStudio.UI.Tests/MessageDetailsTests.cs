using OhMyPi.VisualStudio.UI.Views;
using Omp.Core;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using static OhMyPi.VisualStudio.UI.Tests.Wpf;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>A user message with pasted text and attached files, and clicking a path in tool output.</summary>
[Collection("wpf")]
public sealed class MessageDetailsTests
{
    [Fact]
    public void A_user_message_shows_its_pasted_text_collapsed_and_its_attached_files_as_links()
    {
        var text = "Look at this\n\n<pasted-text name=\"log.txt\">\nline 1\nline 2\n</pasted-text>\n\nAttached: @src/a.cs";
        var service = new FakeService { Transcript = new TranscriptItem[] { new UserItem { Id = "u1", Text = text } } };
        var host = new FakeHost();
        RunSta((window, control) =>
        {
            Assert.True(HasText(window, "Look at this"));
            var pasted = Named<ToggleButton>(window, "log.txt · 2 lines");
            pasted.IsChecked = true;
            Pump();
            Assert.True(HasText(window, "line 2"));
            Click(Named<Button>(window, "@src/a.cs"));
            Pump();
            Assert.Contains(("src/a.cs", (int?)null), host.Opened);
        }, service, host);
    }

    [Fact]
    public void Clicking_a_path_in_a_search_result_opens_the_file_and_selecting_text_does_not()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "omp-click-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "a.cs"), "x");
        var result = new ToolResultView { Text = "a.cs:7: match" };
        var service = new FakeService { Cwd = dir, Transcript = new TranscriptItem[] { Tool("t1", "grep", "{\"pattern\":\"match\"}", ToolStatus.Done, result) } };
        var host = new FakeHost();
        try
        {
            RunSta((window, control) =>
            {
                var box = Named<RichTextBox>(window, "Result");
                var start = box.Document.ContentStart.GetInsertionPosition(System.Windows.Documents.LogicalDirection.Forward);
                var origin = start.GetNextInsertionPosition(System.Windows.Documents.LogicalDirection.Forward)!.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward).TopLeft;
                var point = new Point(origin.X + 2, origin.Y + 2);
                Assert.True(FileClicks.OpenAt(box, point));
                Assert.False(FileClicks.OpenAt(box, new Point(-50, -50)));
                Assert.Single(host.Opened);

                box.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseMoveEvent });
                box.Selection.Select(start, start.GetPositionAtOffset(4)!);
                box.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
                Assert.Single(host.Opened);
                box.Selection.Select(start, start);
                box.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });
            }, service, host);
        }
        finally
        {
            System.IO.Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void A_click_on_the_right_half_of_a_character_in_code_output_belongs_to_that_character()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "omp-click-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "a.cs"), "x");
        var result = new ToolResultView { Text = "x a.cs:7 y" };
        var service = new FakeService { Cwd = dir, Transcript = new TranscriptItem[] { Tool("t1", "grep", "{\"pattern\":\"y\"}", ToolStatus.Done, result) } };
        var host = new FakeHost();
        try
        {
            RunSta((window, control) =>
            {
                var box = Named<RichTextBox>(window, "Result");
                Point RightHalf(int index)
                {
                    foreach (var run in box.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single().Inlines.OfType<System.Windows.Documents.Run>())
                    {
                        if (index < run.Text.Length)
                        {
                            var left = run.ContentStart.GetPositionAtOffset(index)!.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward);
                            var right = run.ContentStart.GetPositionAtOffset(index + 1)!.GetCharacterRect(System.Windows.Documents.LogicalDirection.Backward);

                            return new Point(right.Left - 1, left.Top + left.Height / 2);
                        }

                        index -= run.Text.Length;
                    }

                    throw new System.ArgumentOutOfRangeException(nameof(index));
                }

                Assert.False(FileClicks.OpenAt(box, RightHalf(1)));
                Assert.True(FileClicks.OpenAt(box, RightHalf(7)));
                var opened = Assert.Single(host.Opened);
                Assert.EndsWith("a.cs", opened.Path);
                Assert.Equal(7, opened.Line);
            }, service, host);
        }
        finally
        {
            System.IO.Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void A_text_box_without_a_render_context_opens_nothing() => RunStaWindow(window =>
                                                                            {
                                                                                var box = new TextBox { Text = "a.cs:7" };
                                                                                window.Content = box;
                                                                                Pump();
                                                                                Assert.False(FileClicks.OpenAt(box, new Point(2, 2)));
                                                                            });
}
