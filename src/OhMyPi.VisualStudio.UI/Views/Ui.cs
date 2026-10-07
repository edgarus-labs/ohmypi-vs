using System;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>Small factory helpers so every view builds themed elements the same way.</summary>
internal static class Ui
{
    public const double SmallScale = 1.0;

    public static T Theme<T>(this T element, DependencyProperty property, object key) where T : FrameworkElement
    {
        element.SetResourceReference(property, key);

        return element;
    }

    public static T Styled<T>(this T element, string styleKey) where T : FrameworkElement
    {
        element.SetResourceReference(FrameworkElement.StyleProperty, styleKey);

        return element;
    }

    public static TextBlock Text(string text, object? brushKey = null, bool wrap = false, bool small = false, FontWeight? weight = null)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (brushKey is not null)
        {
            block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        }

        if (small)
        {
            Small(block);
        }

        if (weight.HasValue)
        {
            block.FontWeight = weight.Value;
        }

        return block;
    }

    public static TextBlock Muted(string text, bool wrap = false, bool small = true) => Text(text, ThemeKeys.Muted, wrap, small);

    public static TextBlock Subtle(string text, bool wrap = false, bool small = true) => Text(text, ThemeKeys.Subtle, wrap, small);

    private static readonly DependencyProperty FontScaleProperty =
        DependencyProperty.RegisterAttached("FontScale", typeof(double), typeof(Ui), new PropertyMetadata(1.0, OnScaledFontChanged));

    /// <summary>The VS environment font size, bound as a dynamic resource so a change in Tools &gt; Options rescales the text.</summary>
    private static readonly DependencyProperty EnvironmentFontSizeProperty =
        DependencyProperty.RegisterAttached("EnvironmentFontSize", typeof(double), typeof(Ui), new PropertyMetadata(double.NaN, OnScaledFontChanged));

    /// <summary>Sizes an element's font relative to the VS environment font, following later changes of that font.</summary>
    public static T Small<T>(T element, double scale = SmallScale) where T : FrameworkElement
    {
        element.SetValue(FontScaleProperty, scale);
        element.SetResourceReference(EnvironmentFontSizeProperty, ThemeKeys.FontSize);

        return element;
    }

    /// <summary>Sizes a run's font relative to the VS environment font, following later changes of that font.</summary>
    public static Run Small(Run run, double scale)
    {
        run.SetValue(FontScaleProperty, scale);
        run.SetResourceReference(EnvironmentFontSizeProperty, ThemeKeys.FontSize);

        return run;
    }

    private static void OnScaledFontChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        var size = (double)element.GetValue(EnvironmentFontSizeProperty);
        if (double.IsNaN(size))
        {
            return;
        }

        element.SetValue(TextElement.FontSizeProperty, Math.Round(size * (double)element.GetValue(FontScaleProperty), 1));
    }

    public static TextBlock Icon(string glyph, object? brushKey = null, double size = 12)
    {
        var block = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.FontFamilyProperty, "Omp.IconFont");
        if (brushKey is not null)
        {
            block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        }

        return block;
    }

    public static Button IconButton(string glyph, string tooltip, Action onClick)
    {
        var button = new Button { Content = glyph, ToolTip = tooltip }.Styled("Omp.IconButton");
        AutomationName(button, tooltip);
        button.Click += (_, e) =>
        {
            e.Handled = true;
            onClick();
        };

        return button;
    }

    public static Button Button(object content, Action onClick, string style = "Omp.SecondaryButton", string? tooltip = null)
    {
        var button = new Button { Content = content }.Styled(style);
        if (tooltip is not null)
        {
            button.ToolTip = tooltip;
        }

        if (content is string text)
        {
            AutomationName(button, text);
        }

        button.Click += (_, e) =>
        {
            e.Handled = true;
            onClick();
        };

        return button;
    }

    public static Button Link(string text, Action onClick, string? tooltip = null, bool mono = false)
    {
        var label = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis };
        if (mono)
        {
            label.SetResourceReference(TextBlock.FontFamilyProperty, "Omp.MonoFont");
        }

        var button = Button(label, onClick, "Omp.LinkButton", tooltip);
        AutomationName(button, text);

        return button;
    }

    public static void AutomationName(DependencyObject element, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(element, name);

    /// <summary>Tells screen readers that the live region <paramref name="element"/> (which has a live setting) changed.</summary>
    public static void Announce(UIElement element)
    {
        if (!AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
        {
            return;
        }

        var peer = UIElementAutomationPeer.FromElement(element) ?? UIElementAutomationPeer.CreatePeerForElement(element);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    public static Border Card(UIElement child, Thickness padding) => new Border { Child = child, Padding = padding }.Styled("Omp.Card");

    /// <summary>Selectable monospace text that never captures the mouse wheel of the transcript.</summary>
    public static TextBox Pre(string text, object? brushKey = null)
    {
        var box = new TextBox { Text = text }.Styled("Omp.ReadOnlyText");
        if (brushKey is not null)
        {
            box.SetResourceReference(Control.ForegroundProperty, brushKey);
        }

        BubbleWheel(box);

        return box;
    }

    /// <summary>
    /// Read-only prose the user can select and copy (proportional font, wrapping), filled by <paramref name="fill"/>.
    /// Hyperlinks inside it stay clickable.
    /// </summary>
    public static RichTextBox Prose(Action<InlineCollection> fill, object? brushKey = null, bool small = false)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        fill(paragraph.Inlines);
        var document = new FlowDocument(paragraph) { TextAlignment = TextAlignment.Left };
        var box = new ProseBox(document).Styled("Omp.Prose");
        if (brushKey is not null)
        {
            box.SetResourceReference(Control.ForegroundProperty, brushKey);
        }

        if (small)
        {
            Small(box);
        }

        BubbleWheel(box);

        return box;
    }

    /// <summary>Selectable prose of plain <paramref name="text"/>; new lines are kept.</summary>
    public static RichTextBox Prose(string text, object? brushKey = null, bool small = false) =>
        Prose(inlines => AddLines(inlines, text), brushKey, small);

    /// <summary>
    /// A rich text box whose document keeps no page padding: the template application puts 5px back on each
    /// side whenever the box (re)enters the tree, which would inset the text and make sized prose wrap early.
    /// </summary>
    private sealed class ProseBox : RichTextBox
    {
        public ProseBox(FlowDocument document) : base(document) { }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            Document.PagePadding = new Thickness(0);
        }
    }

    /// <summary>
    /// Selectable prose whose lines never break, sized to its content (horizontally scrolled diffs), which a rich
    /// text box alone does not: a hidden text block with the same inlines sets the size and the prose is laid over it.
    /// </summary>
    public static FrameworkElement SizedProse(Action<InlineCollection> fill)
    {
        var sizer = new TextBlock { TextWrapping = TextWrapping.NoWrap, Visibility = Visibility.Hidden, HorizontalAlignment = HorizontalAlignment.Left };
        fill(sizer.Inlines);
        var box = Prose(fill);
        box.Document.PageWidth = NoWrapPageWidth;
        box.HorizontalAlignment = HorizontalAlignment.Left;
        box.VerticalAlignment = VerticalAlignment.Top;
        box.SetBinding(FrameworkElement.WidthProperty, new System.Windows.Data.Binding(nameof(FrameworkElement.ActualWidth)) { Source = sizer, Converter = CaretSlack.Instance });
        var grid = new Grid();
        grid.Children.Add(sizer);
        grid.Children.Add(box);

        return grid;
    }

    /// <summary>A page wider than any line, so unwrapped prose keeps every line whole.</summary>
    private const double NoWrapPageWidth = 1_000_000;

    /// <summary>Widens the prose a little past its text so the caret's reserved width never forces an extra line break.</summary>
    private sealed class CaretSlack : System.Windows.Data.IValueConverter
    {
        public static readonly CaretSlack Instance = new CaretSlack();

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => (double)value + 4;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Appends <paramref name="text"/> as runs separated by line breaks.</summary>
    public static void AddLines(InlineCollection inlines, string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                inlines.Add(new LineBreak());
            }

            inlines.Add(new Run(lines[i]));
        }
    }

    /// <summary>Passes mouse-wheel scrolling to the enclosing scroll viewer.</summary>
    public static void BubbleWheel(UIElement element) => element.PreviewMouseWheel += (sender, e) =>
                                                              {
                                                                  if (e.Handled)
                                                                  {
                                                                      return;
                                                                  }

                                                                  e.Handled = true;
                                                                  var forwarded = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sender };
                                                                  ((sender as FrameworkElement)?.Parent as UIElement)?.RaiseEvent(forwarded);
                                                              };

    public static StackPanel Row(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return panel;
    }

    public static StackPanel Column(double spacing, params UIElement?[] children)
    {
        var panel = new StackPanel();
        foreach (var child in children)
        {
            if (child is null)
            {
                continue;
            }

            if (panel.Children.Count > 0 && child is FrameworkElement fe)
            {
                fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + spacing, fe.Margin.Right, fe.Margin.Bottom);
            }

            panel.Children.Add(child);
        }

        return panel;
    }

    /// <summary>Runs <paramref name="action"/> when Escape is pressed inside <paramref name="element"/>.</summary>
    public static void OnEscape(UIElement element, Action action) => element.PreviewKeyDown += (_, e) =>
                                                                          {
                                                                              if (e.Key != Key.Escape)
                                                                              {
                                                                                  return;
                                                                              }

                                                                              e.Handled = true;
                                                                              action();
                                                                          };

    public static Popup Popup(UIElement target, UIElement content)
    {
        var popup = new Popup
        {
            PlacementTarget = target,
            Placement = PlacementMode.Top,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = new Border { Child = content }.Styled("Omp.PopupBorder"),
        };
        KeyboardNavigation.SetTabNavigation(popup.Child, KeyboardNavigationMode.Cycle);
        popup.Opened += (_, __) =>
        {
            var owner = FindOwner(target);
            if (owner is null)
            {
                return;
            }

            var child = (FrameworkElement)popup.Child;
            child.LayoutTransform = owner.ZoomTransform;
            child.SetBinding(TextElement.FontFamilyProperty, new System.Windows.Data.Binding(nameof(Control.FontFamily)) { Source = owner });
            child.SetBinding(TextElement.FontSizeProperty, new System.Windows.Data.Binding(nameof(Control.FontSize)) { Source = owner });
        };
        OnEscape(popup.Child, () =>
        {
            popup.IsOpen = false;
            (target as UIElement)?.Focus();
        });

        return popup;
    }

    /// <summary>The zoom factor applied to <paramref name="element"/>'s chat control; 1 outside one.</summary>
    public static double ZoomOf(DependencyObject element) => FindOwner(element)?.ZoomTransform.ScaleX ?? 1;

    private static OmpChatControl? FindOwner(DependencyObject? element)
    {
        while (element is not null && element is not OmpChatControl)
        {
            element = element is Visual || element is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }

        return element as OmpChatControl;
    }
}
