using OhMyPi.VisualStudio.UI.Model;
using OhMyPi.VisualStudio.UI.Views;
using Omp.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static OhMyPi.VisualStudio.UI.Tests.Wpf;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>The prompt box: sending and queueing, the send/stop button, attachments from paste and drop, and their failures.</summary>
[Collection("wpf")]
public sealed class ComposerFlowsTests
{
    /// <summary>
    /// Represents a test harness used to capture and verify the state of the composer, including notices, logs, drafts, and action triggers.
    /// </summary>
    private sealed class Harness
    {
        /// <summary>
        /// Initializes a new instance of the Harness class and configures the internal composer with event handlers for notices, logging, drafts, and stop requests.
        /// </summary>
        public Harness()
        {
            Composer = new Composer((level, text) => Notices.Add($"{level}: {text}"), (message, _) => Logged.Add(message));
            Composer.Submitted += Drafts.Add;
            Composer.StopRequested += () => Stops++;
        }

        /// <summary>
        /// Gets the composer.
        /// </summary>
        public Composer Composer { get; }

        /// <summary>
        /// Gets the collection of notices.
        /// </summary>
        public List<string> Notices { get; } = new List<string>();

        /// <summary>
        /// Gets the collection of logged.
        /// </summary>
        public List<string> Logged { get; } = new List<string>();

        /// <summary>
        /// Gets the collection of drafts.
        /// </summary>
        public List<Draft> Drafts { get; } = new List<Draft>();

        /// <summary>
        /// Gets or sets the stops.
        /// </summary>
        public int Stops { get; set; }

        /// <summary>
        /// Gets the action.
        /// </summary>
        public Button Action => Descendants(Composer).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) is "Send" or "Stop");
    }

    private static void Run(Action<Harness> body) =>
        RunStaWindow(window =>
        {
            var harness = new Harness();
            window.Resources.MergedDictionaries.Add(new OmpChatControl().Resources);
            window.Content = harness.Composer;
            Pump();
            body(harness);
        });

    private static byte[] Png()
    {
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = new MemoryStream())
        {
            encoder.Save(stream);

            return stream.ToArray();
        }
    }

    private static void Paste(TextBox input, IDataObject data) =>
        input.RaiseEvent(new DataObjectPastingEventArgs(data, false, data.GetFormats()[0]));

    private static DragEventArgs Drag(IDataObject data, RoutedEvent routedEvent)
    {
        var constructor = typeof(DragEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var args = (DragEventArgs)constructor.Invoke([data, DragDropKeyStates.None, DragDropEffects.Copy | DragDropEffects.Move, null!, new Point()]);
        args.RoutedEvent = routedEvent;

        return args;
    }

    [Fact]
    public void Enter_sends_alt_enter_queues_and_shift_enter_is_a_new_line() => Run(h =>
                                                                                     {
                                                                                         h.Composer.Input.Text = "one";
                                                                                         Assert.True(h.Composer.HandleKey(Key.Enter, Key.None, ModifierKeys.None));
                                                                                         h.Composer.Input.Text = "two";
                                                                                         Assert.False(h.Composer.HandleKey(Key.Enter, Key.None, ModifierKeys.Shift));
                                                                                         Assert.False(h.Composer.HandleKey(Key.A, Key.None, ModifierKeys.None));
                                                                                         h.Composer.SetStatus(busy: true, unavailable: false);
                                                                                         Assert.True(h.Composer.HandleKey(Key.System, Key.Enter, ModifierKeys.Alt));
                                                                                         Assert.Equal(new[] { ("one", PromptMode.Auto), ("two", PromptMode.FollowUp) }, h.Drafts.Select(d => (d.Text, d.Mode)));
                                                                                     });

    [Fact]
    public void The_action_button_sends_while_idle_and_stops_while_busy() => Run(h =>
                                                                                  {
                                                                                      h.Composer.Input.Text = "go";
                                                                                      Pump();
                                                                                      Click(h.Action);
                                                                                      Assert.Single(h.Drafts);
                                                                                      h.Composer.SetStatus(busy: true, unavailable: false);
                                                                                      Click(h.Action);
                                                                                      Assert.Equal(1, h.Stops);
                                                                                  });

    [Fact]
    public void An_unavailable_omp_sends_nothing() => Run(h =>
                                                           {
                                                               h.Composer.SetStatus(busy: false, unavailable: true);
                                                               h.Composer.Input.Text = "lost";
                                                               h.Composer.HandleKey(Key.Enter, Key.None, ModifierKeys.None);
                                                               Assert.Empty(h.Drafts);
                                                           });

    [Fact]
    public void Commands_and_the_active_document_round_trip() => Run(h =>
                                                                      {
                                                                          var commands = new[] { new SlashCommandView { Name = "compact" } };
                                                                          h.Composer.Commands = commands;
                                                                          Assert.Same(commands, h.Composer.Commands);
                                                                          h.Composer.ActiveDocument = "C:\\repo\\a.cs";
                                                                          Assert.Equal("C:\\repo\\a.cs", h.Composer.ActiveDocument);
                                                                          Assert.True(h.Composer.AttachButton.IsEnabled);
                                                                          h.Composer.ActiveDocument = null;
                                                                          Assert.False(h.Composer.AttachButton.IsEnabled);
                                                                      });

    [Fact]
    public void A_chip_can_be_removed_and_a_restored_draft_does_not_overwrite_new_text() => Run(h =>
                                                                                                 {
                                                                                                     h.Composer.AddAttachments(new Attachment[] { new FileAttachment("C:\\repo\\a.cs"), new PastedTextAttachment("Pasted text 1", new string('x', 600)) });
                                                                                                     Pump();
                                                                                                     Click(Named<Button>(h.Composer, "Remove a.cs"));
                                                                                                     Pump();
                                                                                                     Assert.Empty(AllNamed<Button>(h.Composer, "Remove a.cs"));
                                                                                                     Assert.Single(AllNamed<Button>(h.Composer, "Remove Pasted text 1"));

                                                                                                     h.Composer.Input.Text = "newer";
                                                                                                     h.Composer.Restore(new Draft("older", new Attachment[0], PromptMode.Auto));
                                                                                                     Assert.Equal("newer", h.Composer.Input.Text);
                                                                                                 });

    [Fact]
    public void Long_pasted_text_becomes_an_attachment_and_short_text_stays_inline() => Run(h =>
                                                                                             {
                                                                                                 Paste(h.Composer.Input, new DataObject(DataFormats.UnicodeText, "short"));
                                                                                                 Assert.Empty(AllNamed<Button>(h.Composer, "Remove Pasted text 1"));
                                                                                                 Paste(h.Composer.Input, new DataObject(DataFormats.UnicodeText, string.Join("\r\n", Enumerable.Range(0, 40))));
                                                                                                 Pump();
                                                                                                 Assert.Single(AllNamed<Button>(h.Composer, "Remove Pasted text 1"));
                                                                                                 Paste(h.Composer.Input, new DataObject(DataFormats.Html, "<b>x</b>"));
                                                                                                 Assert.Empty(h.Notices);
                                                                                             });

    [Fact]
    public void A_pasted_bitmap_becomes_an_image_and_an_unreadable_paste_is_reported() => Run(h =>
                                                                                               {
                                                                                                   var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
                                                                                                   Paste(h.Composer.Input, new DataObject(DataFormats.Bitmap, bitmap));
                                                                                                   Pump();
                                                                                                   Assert.Single(AllNamed<Button>(h.Composer, "Remove Image 1"));

                                                                                                   var broken = new DataObject();
                                                                                                   broken.SetData(DataFormats.Bitmap, "not a bitmap");
                                                                                                   Paste(h.Composer.Input, broken);
                                                                                                   Assert.Single(AllNamed<Button>(h.Composer, "Remove Image 1"));
                                                                                               });

    /// <summary>Ctrl+V as the text box runs it: the Paste command against the system clipboard.</summary>
    private static void PasteClipboard(TextBox input)
    {
        input.Focus();
        Assert.True(ApplicationCommands.Paste.CanExecute(null, input), "Paste is disabled for this clipboard content");
        ApplicationCommands.Paste.Execute(null, input);
        Pump();
    }

    [Fact]
    public void Ctrl_V_attaches_a_screenshot_a_png_and_a_copied_image_file_from_the_clipboard()
    {
        var dir = Path.Combine(Path.GetTempPath(), "omp-paste-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "shot.png");
            File.WriteAllBytes(file, Png());
            Run(h =>
            {
                Clipboard.SetImage(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8));
                PasteClipboard(h.Composer.Input);
                Assert.Single(AllNamed<Button>(h.Composer, "Remove Image 1"));

                var png = new DataObject();
                png.SetData("PNG", new MemoryStream(Png()));
                Clipboard.SetDataObject(png, true);
                PasteClipboard(h.Composer.Input);
                Assert.Single(AllNamed<Button>(h.Composer, "Remove Image 2"));

                Clipboard.SetFileDropList(new System.Collections.Specialized.StringCollection { file });
                PasteClipboard(h.Composer.Input);
                Pump(300);
                Assert.Single(AllNamed<Button>(h.Composer, "Remove shot.png"));

                Assert.Equal("", h.Composer.Input.Text);
                Assert.Empty(h.Notices);
            });
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Ctrl_V_of_long_text_attaches_it_and_short_text_goes_into_the_input() => Run(h =>
                                                                                             {
                                                                                                 Clipboard.SetText(string.Join("\r\n", Enumerable.Range(0, 40)));
                                                                                                 PasteClipboard(h.Composer.Input);
                                                                                                 Assert.Single(AllNamed<Button>(h.Composer, "Remove Pasted text 1"));
                                                                                                 Assert.Equal("", h.Composer.Input.Text);

                                                                                                 for (var attempt = 0; attempt < 5 && h.Composer.Input.Text != "short"; attempt++)
                                                                                                 {
                                                                                                     Clipboard.SetText("short");
                                                                                                     PasteClipboard(h.Composer.Input);
                                                                                                     Pump(100);
                                                                                                 }

                                                                                                 Assert.Equal("short", h.Composer.Input.Text);
                                                                                             });

    [Fact]
    public void An_image_whose_data_cannot_be_decoded_shows_without_a_thumbnail_and_is_logged() => Run(h =>
                                                                                                        {
                                                                                                            h.Composer.AddAttachments(new Attachment[] { new ImageAttachment("Broken", "!!not base64!!", "image/png", 10) });
                                                                                                            Pump();
                                                                                                            Assert.Single(AllNamed<Button>(h.Composer, "Remove Broken"));
                                                                                                            Assert.Contains("Showing the thumbnail of Broken failed", h.Logged);
                                                                                                        });

    [Fact]
    public void Dropped_files_become_mentions_and_dropped_images_attach_unless_unreadable_or_too_large()
    {
        var dir = Path.Combine(Path.GetTempPath(), "omp-drop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var good = Path.Combine(dir, "shot.png");
            File.WriteAllBytes(good, Png());
            var garbage = Path.Combine(dir, "fake.png");
            File.WriteAllBytes(garbage, [1, 2, 3]);
            var huge = Path.Combine(dir, "huge.jpg");
            using (var stream = File.Create(huge))
            {
                stream.SetLength(Attachments.MaxImageBytes + 1);
            }

            var code = Path.Combine(dir, "a.cs");
            File.WriteAllText(code, "x");
            Run(h =>
            {
                var over = Drag(new DataObject(DataFormats.FileDrop, new[] { code }), UIElement.PreviewDragOverEvent);
                h.Composer.Input.RaiseEvent(over);
                Assert.Equal(DragDropEffects.Copy, over.Effects);
                var notFiles = Drag(new DataObject(DataFormats.UnicodeText, "text"), UIElement.PreviewDragOverEvent);
                h.Composer.Input.RaiseEvent(notFiles);
                Assert.False(notFiles.Handled);

                h.Composer.Input.RaiseEvent(Drag(new DataObject(DataFormats.FileDrop, new[] { code, good, garbage, huge }), UIElement.PreviewDropEvent));
                h.Composer.Input.RaiseEvent(Drag(new DataObject(DataFormats.FileDrop, new string[0]), UIElement.PreviewDropEvent));
                Pump(800);
                Assert.Single(AllNamed<Button>(h.Composer, "Remove a.cs"));
                Assert.Single(AllNamed<Button>(h.Composer, "Remove shot.png"));
                Assert.Contains("Warning: Image fake.png was not attached: it is not a readable image.", h.Notices);
                Assert.Contains("Warning: Image huge.jpg was not attached: larger than 10 MB.", h.Notices);
            });
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void A_dropped_image_that_cannot_be_read_is_reported() => Run(h =>
                                                                          {
                                                                              h.Composer.AddDroppedAsync(new[] { Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png") }).GetAwaiter();
                                                                              Pump(500);
                                                                              Assert.Contains("Reading the dropped image failed", h.Logged);
                                                                              Assert.Contains(h.Notices, notice => notice.StartsWith("Error: Reading the dropped image failed:", StringComparison.Ordinal));
                                                                          });
}
