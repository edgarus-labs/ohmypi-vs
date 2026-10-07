using Omp.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace OhMyPi.VisualStudio.UI.Views;

internal static class Notices
{
    public static object BrushKey(NoticeLevel level) =>
        level == NoticeLevel.Error ? ThemeKeys.Error : level == NoticeLevel.Warning ? ThemeKeys.Warning : ThemeKeys.Muted;

    public static string Glyph(NoticeLevel level) =>
        level == NoticeLevel.Error ? Glyphs.Error : level == NoticeLevel.Warning ? Glyphs.Warning : Glyphs.Info;

    /// <summary>
    /// Icon + text notice on one line. A notice that does not fit gets a focusable toggle (the line itself also
    /// toggles on click) that shows the full text as selectable prose; with <paramref name="open"/> the choice is
    /// remembered under <paramref name="key"/>.
    /// </summary>
    public static FrameworkElement Line(NoticeLevel level, string text, OpenState? open = null, string? key = null)
    {
        var brush = level == NoticeLevel.Info ? ThemeKeys.Muted : ThemeKeys.Foreground;
        var icon = Ui.Icon(Glyph(level), BrushKey(level), 12);
        icon.Margin = new Thickness(0, 2, 6, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        var label = Ui.Text(text, brush, small: true);
        var full = Ui.Prose(text, brush, small: true);
        var toggle = new ToggleButton { Content = Glyphs.ChevronRight, Width = 20, Height = 18, MinHeight = 18, FontSize = 9, VerticalAlignment = VerticalAlignment.Top }.Styled("Omp.ToggleButton");
        var content = new Grid();
        content.Children.Add(label);
        content.Children.Add(full);
        var row = new DockPanel { Background = System.Windows.Media.Brushes.Transparent, ToolTip = text };
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(toggle, Dock.Right);
        row.Children.Add(icon);
        row.Children.Add(toggle);
        row.Children.Add(content);

        void Apply(bool expanded)
        {
            label.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
            full.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            toggle.Content = expanded ? Glyphs.ChevronDown : Glyphs.ChevronRight;
            Ui.AutomationName(toggle, expanded ? "Collapse message" : "Show the full message");
            row.ToolTip = expanded ? null : text;
        }

        void UpdateToggle()
        {
            if (toggle.IsChecked == true)
            {
                return;
            }

            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var truncated = text.IndexOf('\n') >= 0 || label.DesiredSize.Width > label.ActualWidth + 0.5;
            toggle.Visibility = truncated ? Visibility.Visible : Visibility.Collapsed;
        }

        var initiallyOpen = key is not null && open?.IsOpen(key) == true;
        toggle.IsChecked = initiallyOpen;
        toggle.Visibility = initiallyOpen || text.IndexOf('\n') >= 0 ? Visibility.Visible : Visibility.Collapsed;
        Apply(initiallyOpen);
        toggle.Checked += (_, __) =>
        {
            if (key is not null)
            {
                open?.SetOpen(key, true);
            }

            Apply(true);
        };
        toggle.Unchecked += (_, __) =>
        {
            if (key is not null)
            {
                open?.SetOpen(key, false);
            }

            Apply(false);
            UpdateToggle();
        };
        label.SizeChanged += (_, __) => UpdateToggle();
        label.MouseLeftButtonUp += (_, __) =>
        {
            if (toggle.Visibility == Visibility.Visible)
            {
                toggle.IsChecked = true;
            }
        };

        return row;
    }
}
