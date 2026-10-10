using Omp.Core;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>Collapsible section whose body is built only once it is first opened.</summary>
internal static class Collapsible
{
    /// <summary>
    /// Creates a collapsible expander panel consisting of a toggleable header and a lazily-loaded body, managing its expanded state via the provided render context.
    /// </summary>
    /// <param name="ctx">The ctx.</param>
    /// <param name="key">The key.</param>
    /// <param name="header">The header.</param>
    /// <param name="body">The body.</param>
    /// <param name="defaultOpen">The default open.</param>
    /// <param name="bodyMargin">The body margin.</param>
    /// <returns>The stack panel result.</returns>
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
        if (string.IsNullOrEmpty(name) && header is TextBlock text)
        {
            name = text.Text;
        }

        if (!string.IsNullOrEmpty(name))
        {
            Ui.AutomationName(toggle, name);
        }

        var panel = new StackPanel();
        panel.Children.Add(toggle);
        UIElement? built = null;

        void Apply(bool open)
        {
            chevron.Text = open ? Glyphs.ChevronDown : Glyphs.ChevronRight;
            if (open && built is null)
            {
                built = SafeBody(ctx, key, body);
                if (built is FrameworkElement fe)
                {
                    fe.Margin = bodyMargin ?? new Thickness(16, 2, 0, 4);
                }

                panel.Children.Add(built);
            }
            if (built is not null)
            {
                built.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            }
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

    /// <summary>
    /// Executes the provided function to generate a UI element, catching any exceptions to log the error and return a fallback error notice.
    /// </summary>
    /// <param name="ctx">The ctx.</param>
    /// <param name="key">The key.</param>
    /// <param name="body">The body.</param>
    /// <returns>The uielement result.</returns>
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
