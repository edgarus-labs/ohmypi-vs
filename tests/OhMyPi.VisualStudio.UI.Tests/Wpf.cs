using Newtonsoft.Json.Linq;
using Omp.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OhMyPi.VisualStudio.UI.Tests;

/// <summary>Hosts WPF elements in an off-screen window on a dedicated STA thread and inspects their visual tree.</summary>
internal static class Wpf
{
    /// <summary>Runs <paramref name="body"/> against a real chat control bound to <paramref name="service"/> and <paramref name="host"/>.</summary>
    public static void RunSta(Action<Window, OmpChatControl> body, FakeService service, IOmpHost host) =>
        RunStaWindow(window =>
        {
            var control = new OmpChatControl();
            window.Content = control;
            control.Initialize(service, host);
            Pump();
            body(window, control);
        });

    /// <summary>Runs <paramref name="body"/> with a shown, empty off-screen window.</summary>
    public static void RunStaWindow(Action<Window> body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try
            {
                window = new Window
                {
                    Width = 520,
                    Height = 760,
                    Left = -20000,
                    Top = -20000,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    ShowActivated = false,
                };
                window.Show();
                body(window);
            }
            catch (Exception error)
            {
                failure = ExceptionDispatchInfo.Capture(error);
            }
            finally
            {
                window?.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "UI thread did not finish");
        failure?.Throw();
    }

    /// <summary>Processes queued dispatcher work, including the coalescing flush timer.</summary>
    public static void Pump(int milliseconds = 120)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        do
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
        while (DateTime.UtcNow < until);
    }

    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            yield return node;
            var count = node is Visual || node is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(node) : 0;
            for (var i = 0; i < count; i++)
            {
                queue.Enqueue(VisualTreeHelper.GetChild(node, i));
            }

            if (node is Popup popup && popup.Child is not null)
            {
                queue.Enqueue(popup.Child);
            }
        }
    }

    /// <summary>Visible text of text blocks, text boxes and rich text boxes under <paramref name="root"/>.</summary>
    public static IEnumerable<string> Texts(DependencyObject root) =>
        Descendants(root).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => string.IsNullOrEmpty(t.Text) ? new TextRange(t.ContentStart, t.ContentEnd).Text : t.Text)
            .Concat(Descendants(root).OfType<TextBox>().Where(t => t.IsVisible).Select(t => t.Text))
            .Concat(Descendants(root).OfType<RichTextBox>().Where(t => t.IsVisible).Select(t => new TextRange(t.Document.ContentStart, t.Document.ContentEnd).Text));

    public static bool HasText(DependencyObject root, string fragment) => Texts(root).Any(t => t.Contains(fragment));

    /// <summary>Hyperlinks inside visible text blocks and rich text boxes under <paramref name="root"/>.</summary>
    public static IEnumerable<Hyperlink> Hyperlinks(DependencyObject root) =>
        Descendants(root).OfType<TextBlock>().Where(t => t.IsVisible).SelectMany(t => Inlines(t.Inlines))
            .Concat(Descendants(root).OfType<RichTextBox>().Where(r => r.IsVisible).SelectMany(r => r.Document.Blocks.OfType<Paragraph>().SelectMany(p => Inlines(p.Inlines))))
            .OfType<Hyperlink>();

    private static IEnumerable<Inline> Inlines(InlineCollection inlines) =>
        inlines.SelectMany(inline => inline is Span span ? new[] { inline }.Concat(Inlines(span.Inlines)) : new[] { inline });

    public static T Named<T>(DependencyObject root, string automationName) where T : FrameworkElement =>
        Descendants(root).OfType<T>().First(e => e.IsVisible && System.Windows.Automation.AutomationProperties.GetName(e) == automationName);

    public static IEnumerable<T> AllNamed<T>(DependencyObject root, string automationName) where T : FrameworkElement =>
        Descendants(root).OfType<T>().Where(e => e.IsVisible && System.Windows.Automation.AutomationProperties.GetName(e) == automationName);

    public static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));

    /// <summary>Raises the preview and bubbling key-down events like a real key press.</summary>
    public static void Press(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual((Visual)target)!;
        var preview = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(preview);
        if (!preview.Handled)
        {
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
        }
    }

    public static ToolItem Tool(string id, string name, string args, ToolStatus status, ToolResultView? result = null) =>
        new ToolItem { Id = id, Name = name, Args = JToken.Parse(args), Status = status, StartedAt = 1000, EndedAt = status == ToolStatus.Running ? (long?)null : 1012, Result = result };
}
