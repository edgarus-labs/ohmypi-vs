using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>
/// A table cell: selectable prose laid over a hidden text block with the same inlines. The text block measures the
/// width (a rich text box alone reports no natural width); the prose stretches to the cell, wraps to its width and
/// sets the row height.
/// </summary>
internal sealed class TableCell : Grid
{
    /// <summary>Space the prose needs beyond its text so the caret's reserved width never adds a line break.</summary>
    private const double CaretSlack = 4;

    private double? _widestWord;

    public TableCell(TextBlock sizer, RichTextBox box, Border chrome, Thickness padding)
    {
        Sizer = sizer;
        Box = box;
        var inset = new Thickness(padding.Left + chrome.BorderThickness.Left, padding.Top + chrome.BorderThickness.Top, padding.Right + chrome.BorderThickness.Right, padding.Bottom + chrome.BorderThickness.Bottom);
        box.Margin = inset;
        sizer.Margin = new Thickness(inset.Left, inset.Top, inset.Right + CaretSlack, inset.Bottom);
        sizer.Visibility = Visibility.Hidden;
        sizer.HorizontalAlignment = HorizontalAlignment.Left;
        box.HorizontalAlignment = HorizontalAlignment.Stretch;
        box.VerticalAlignment = VerticalAlignment.Stretch;
        Children.Add(chrome);
        Children.Add(sizer);
        Children.Add(box);
    }

    public TextBlock Sizer { get; }

    public RichTextBox Box { get; }

    /// <summary>
    /// The narrowest the cell can be without breaking a word: the widest whitespace-separated word of its text plus
    /// the sizer's insets. A measurement clipped to a narrow constraint cannot tell this, so the words are measured
    /// one by one with the fonts they inherit.
    /// </summary>
    public double MinimumWidth => (_widestWord ?? (_widestWord = WidestWord()).Value) + Sizer.Margin.Left + Sizer.Margin.Right;

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == TextElement.FontSizeProperty || e.Property == TextElement.FontFamilyProperty || e.Property == TextElement.FontWeightProperty
            || e.Property == TextElement.FontStyleProperty || e.Property == TextElement.FontStretchProperty)
        {
            _widestWord = null;
        }
    }

    private double WidestWord()
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double widest = 0;
        foreach (var run in Runs(Sizer.Inlines))
        {
            var typeface = new Typeface(run.FontFamily, run.FontStyle, run.FontWeight, run.FontStretch);
            foreach (var word in run.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var text = new FormattedText(word, CultureInfo.CurrentUICulture, Sizer.FlowDirection, typeface, run.FontSize, Brushes.Black, pixelsPerDip);
                widest = Math.Max(widest, text.WidthIncludingTrailingWhitespace);
            }
        }

        return widest;
    }

    private static IEnumerable<Run> Runs(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run)
            {
                yield return run;
            }
            else if (inline is Span span)
            {
                foreach (var nested in Runs(span.Inlines))
                {
                    yield return nested;
                }
            }
        }
    }
}
