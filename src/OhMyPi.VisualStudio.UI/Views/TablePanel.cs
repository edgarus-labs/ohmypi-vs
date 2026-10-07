using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using OhMyPi.VisualStudio.UI.Model;

namespace OhMyPi.VisualStudio.UI.Views
{
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
                _widestWord = null;
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
                if (inline is Run run) yield return run;
                else if (inline is Span span)
                    foreach (var nested in Runs(span.Inlines)) yield return nested;
            }
        }
    }

    /// <summary>
    /// Lays <see cref="TableCell"/> children out in a grid of <see cref="Columns"/> columns, row-major. Columns take
    /// their natural width while the table fits the viewport; otherwise they share it as <see cref="TableColumns.Fit"/>
    /// decides and the cells wrap, so the table only overflows (and scrolls) when a word is wider than its column.
    /// </summary>
    internal sealed class TablePanel : Panel
    {
        private double _viewport = double.PositiveInfinity;
        private double[] _widths = Array.Empty<double>();
        private double[] _heights = Array.Empty<double>();

        public int Columns { get; set; } = 1;

        /// <summary>The width the enclosing scroller can show without scrolling; set by <see cref="TableScroller"/>.</summary>
        public double Viewport
        {
            get => _viewport;
            set
            {
                if (_viewport.Equals(value)) return;
                _viewport = value;
                InvalidateMeasure();
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var columns = Math.Max(1, Columns);
            var rows = (InternalChildren.Count + columns - 1) / columns;
            var natural = new double[columns];
            var minimum = new double[columns];
            for (var i = 0; i < InternalChildren.Count; i++)
            {
                var cell = (TableCell)InternalChildren[i];
                var column = i % columns;
                cell.Sizer.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var cellNatural = cell.Sizer.DesiredSize.Width;
                natural[column] = Math.Max(natural[column], cellNatural);
                minimum[column] = Math.Max(minimum[column], Math.Min(cell.MinimumWidth, cellNatural));
            }
            var available = double.IsInfinity(availableSize.Width) ? _viewport : availableSize.Width;
            _widths = TableColumns.Fit(natural, minimum, available);
            _heights = new double[rows];
            for (var i = 0; i < InternalChildren.Count; i++)
            {
                var cell = InternalChildren[i];
                cell.Measure(new Size(_widths[i % columns], double.PositiveInfinity));
                _heights[i / columns] = Math.Max(_heights[i / columns], cell.DesiredSize.Height);
            }
            double width = 0, height = 0;
            foreach (var w in _widths) width += w;
            foreach (var h in _heights) height += h;
            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var columns = Math.Max(1, Columns);
            double y = 0;
            for (var row = 0; row < _heights.Length; row++)
            {
                double x = 0;
                for (var column = 0; column < columns && row * columns + column < InternalChildren.Count; column++)
                {
                    InternalChildren[row * columns + column].Arrange(new Rect(x, y, _widths[column], _heights[row]));
                    x += _widths[column];
                }
                y += _heights[row];
            }
            return finalSize;
        }
    }

    /// <summary>
    /// The horizontal scroller around a table. A scroll viewer measures its content without a width limit, so this one
    /// first tells the <see cref="TablePanel"/> how wide the viewport is (less the frame chrome around the panel) and
    /// the table only scrolls when it cannot fit.
    /// </summary>
    internal sealed class TableScroller : ScrollViewer
    {
        private readonly TablePanel _panel;
        private readonly double _chrome;

        public TableScroller(TablePanel panel, double chrome)
        {
            _panel = panel;
            _chrome = chrome;
        }

        protected override Size MeasureOverride(Size constraint)
        {
            _panel.Viewport = Math.Max(0, constraint.Width - _chrome);
            return base.MeasureOverride(constraint);
        }
    }
}
