using OhMyPi.VisualStudio.UI.Model;
using Omp.Core;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static OhMyPi.VisualStudio.UI.Tests.Wpf;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>The prompt composer: what it promises while busy, attachment chips, paste and dropped images.</summary>
[Collection("wpf")]
public sealed class ComposerTests
{
    private static Views.Composer Composer(Window window) => Descendants(window).OfType<Views.Composer>().Single();

    private static string Png()
    {
        var bitmap = new RenderTargetBitmap(4, 4, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 4, 4));
        }

        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = new MemoryStream())
        {
            encoder.Save(stream);

            return Convert.ToBase64String(stream.ToArray());
        }
    }

    [Fact]
    public void While_busy_the_placeholder_describes_steering_follow_up_and_stop()
    {
        var service = new FakeService { Session = new SessionView { Phase = SessionPhase.Running } };
        RunSta((window, control) =>
        {
            var placeholder = Descendants(window).OfType<TextBlock>().Single(t => t.IsVisible && t.Text.Contains("follow-up"));
            Assert.Contains("Enter to steer", placeholder.Text);
            Assert.Contains("Alt+Enter", placeholder.Text);
            Assert.Contains("Esc to stop", placeholder.Text);
        }, service, new FakeHost());
    }

    [Fact]
    public void Adding_an_attachment_keeps_the_existing_chips() => RunSta((window, control) =>
                                                                        {
                                                                            var composer = Composer(window);
                                                                            var data = Png();
                                                                            composer.AddAttachments(new Attachment[] { new ImageAttachment("Image A", data, "image/png", 100) });
                                                                            Pump();
                                                                            var chip = Named<Button>(window, "Remove Image A");
                                                                            composer.AddAttachments(new Attachment[] { new FileAttachment("C:\\repo\\a.cs") });
                                                                            Pump();
                                                                            Assert.Same(chip, Named<Button>(window, "Remove Image A"));
                                                                        }, new FakeService(), new FakeHost());

    [Fact]
    public void A_clipboard_that_fails_during_paste_becomes_a_notice() => RunSta((window, control) =>
                                                                               {
                                                                                   var input = Named<TextBox>(window, "Prompt");
                                                                                   var args = new DataObjectPastingEventArgs(new BrokenClipboard(), false, DataFormats.Bitmap) { RoutedEvent = DataObject.PastingEvent };
                                                                                   input.RaiseEvent(args);
                                                                                   Pump();
                                                                                   Assert.True(HasText(window, "Pasting failed: clipboard busy"));
                                                                               }, new FakeService(), new FakeHost());

    [Fact]
    public void A_dropped_image_that_cannot_be_decoded_is_rejected_with_a_notice()
    {
        var path = Path.Combine(Path.GetTempPath(), "omp-ui-broken-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5]);
        try
        {
            RunSta((window, control) =>
            {
                var drop = Composer(window).AddDroppedAsync(new[] { path });
                while (!drop.IsCompleted)
                {
                    Pump(20);
                }

                Pump();
                Assert.True(HasText(window, $"Image {Path.GetFileName(path)} was not attached: it is not a readable image."), string.Join(" | ", Texts(window)));
                Assert.Empty(AllNamed<Button>(window, $"Remove {Path.GetFileName(path)}"));
            }, new FakeService(), new FakeHost());
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A clipboard that offers a bitmap but fails when it is read, like one held by another process.</summary>
    private sealed class BrokenClipboard : IDataObject
    {
        public object GetData(string format) => throw new COMException("clipboard busy");

        public object GetData(Type format) => throw new COMException("clipboard busy");

        public object GetData(string format, bool autoConvert) => throw new COMException("clipboard busy");

        public bool GetDataPresent(string format) => format == DataFormats.Bitmap;

        public bool GetDataPresent(Type format) => false;

        public bool GetDataPresent(string format, bool autoConvert) => format == DataFormats.Bitmap;

        public string[] GetFormats() => [DataFormats.Bitmap];

        public string[] GetFormats(bool autoConvert) => [DataFormats.Bitmap];

        public void SetData(object data) => throw new NotSupportedException();

        public void SetData(string format, object data) => throw new NotSupportedException();

        public void SetData(Type format, object data) => throw new NotSupportedException();

        public void SetData(string format, object data, bool autoConvert) => throw new NotSupportedException();
    }
}
