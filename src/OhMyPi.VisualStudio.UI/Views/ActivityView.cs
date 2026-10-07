using OhMyPi.VisualStudio.UI.Model;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>
/// The row under the conversation while a prompt is in flight: a pulsing marker in the accent color, what the
/// agent is doing and how long it has been at it. The timer runs only while the row is on screen.
/// </summary>
internal sealed class ActivityView : StackPanel
{
    private static readonly string[] Frames = ["·", "∙", "•", "●", "•", "∙"];
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(140);

    private readonly ActivityItem _item;
    private readonly TextBlock _marker;
    private readonly TextBlock _elapsed;
    private readonly DispatcherTimer _timer;
    private int _frame;

    public ActivityView(ActivityItem item)
    {
        _item = item;
        Orientation = Orientation.Horizontal;
        _marker = Ui.Text(Frames[0], ThemeKeys.Accent, small: false);
        _marker.Width = 14;
        _marker.FontWeight = FontWeights.Bold;
        _marker.TextAlignment = TextAlignment.Center;
        var label = Ui.Text(item.Label + "…", ThemeKeys.Muted, small: false);
        label.Margin = new Thickness(4, 0, 0, 0);
        _elapsed = Ui.Subtle("");
        _elapsed.Margin = new Thickness(8, 0, 0, 0);
        _elapsed.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_marker);
        Children.Add(label);
        Children.Add(_elapsed);
        Ui.AutomationName(this, item.Label);
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Tick };
        _timer.Tick += (_, __) => Advance();
        Loaded += (_, __) =>
        {
            Advance();
            _timer.Start();
        };
        Unloaded += (_, __) => _timer.Stop();
    }

    private void Advance()
    {
        _frame = (_frame + 1) % Frames.Length;
        _marker.Text = Frames[_frame];
        _elapsed.Text = Chrome.ElapsedText(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _item.StartedAt);
    }
}
