using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Omp.Core;

namespace OhMyPi.VisualStudio.UI.Views
{
    /// <summary>Expand state of collapsible sections by key, kept across re-renders of the same item.</summary>
    internal sealed class OpenState
    {
        private readonly Dictionary<string, bool> _open = new Dictionary<string, bool>(StringComparer.Ordinal);

        public bool? IsOpen(string key) => _open.TryGetValue(key, out var open) ? open : (bool?)null;

        public void SetOpen(string key, bool open) => _open[key] = open;

        /// <summary>Forgets every remembered state; item ids of another session must not inherit them.</summary>
        public void Clear() => _open.Clear();
    }

    /// <summary>Collapsible section whose body is built only once it is first opened.</summary>
    internal static class Collapsible
    {
        public static StackPanel Create(RenderContext ctx, string key, UIElement header, Func<UIElement> body, bool defaultOpen = false, Thickness? bodyMargin = null)
        {
            var chevron = Ui.Icon(Glyphs.ChevronRight, ThemeKeys.Muted, 9);
            chevron.Margin = new Thickness(0, 0, 6, 0);
            var headerRow = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(chevron, Dock.Left);
            headerRow.Children.Add(chevron);
            headerRow.Children.Add(header);
            var toggle = new ToggleButton { Content = headerRow }.Styled("Omp.ExpanderHeader");
            var name = System.Windows.Automation.AutomationProperties.GetName(header);
            if (string.IsNullOrEmpty(name) && header is TextBlock text) name = text.Text;
            if (!string.IsNullOrEmpty(name)) Ui.AutomationName(toggle, name);
            var panel = new StackPanel();
            panel.Children.Add(toggle);
            UIElement? built = null;

            void Apply(bool open)
            {
                chevron.Text = open ? Glyphs.ChevronDown : Glyphs.ChevronRight;
                if (open && built == null)
                {
                    built = SafeBody(ctx, key, body);
                    if (built is FrameworkElement fe) fe.Margin = bodyMargin ?? new Thickness(16, 2, 0, 4);
                    panel.Children.Add(built);
                }
                if (built != null) built.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            }

            var initiallyOpen = ctx.Open.IsOpen(key) ?? defaultOpen;
            toggle.IsChecked = initiallyOpen;
            Apply(initiallyOpen);
            toggle.Checked += (_, __) =>
            {
                ctx.Open.SetOpen(key, true);
                Apply(true);
            };
            toggle.Unchecked += (_, __) =>
            {
                ctx.Open.SetOpen(key, false);
                Apply(false);
            };
            return panel;
        }

        private static UIElement SafeBody(RenderContext ctx, string key, Func<UIElement> body)
        {
            try
            {
                return body();
            }
            catch (Exception error)
            {
                ctx.LogError($"Showing section {key} failed", error);
                return Notices.Line(NoticeLevel.Error, $"Could not show this section: {error.Message}");
            }
        }
    }

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
                if (toggle.IsChecked == true) return;
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var truncated = text.IndexOf('\n') >= 0 || label.DesiredSize.Width > label.ActualWidth + 0.5;
                toggle.Visibility = truncated ? Visibility.Visible : Visibility.Collapsed;
            }

            var initiallyOpen = key != null && open?.IsOpen(key) == true;
            toggle.IsChecked = initiallyOpen;
            toggle.Visibility = initiallyOpen || text.IndexOf('\n') >= 0 ? Visibility.Visible : Visibility.Collapsed;
            Apply(initiallyOpen);
            toggle.Checked += (_, __) =>
            {
                if (key != null) open?.SetOpen(key, true);
                Apply(true);
            };
            toggle.Unchecked += (_, __) =>
            {
                if (key != null) open?.SetOpen(key, false);
                Apply(false);
                UpdateToggle();
            };
            label.SizeChanged += (_, __) => UpdateToggle();
            label.MouseLeftButtonUp += (_, __) =>
            {
                if (toggle.Visibility == Visibility.Visible) toggle.IsChecked = true;
            };
            return row;
        }
    }
}
