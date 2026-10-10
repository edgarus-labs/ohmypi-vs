using OhMyPi.VisualStudio.UI.Model;
using System;
using System.Windows;
using System.Windows.Controls;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>
/// Lays <see cref="TableCell"/> children out in a grid of <see cref="Columns"/> columns, row-major. Columns take
/// their natural width while the table fits the viewport; otherwise they share it as <see cref="TableColumns.Fit"/>
/// decides and the cells wrap, so the table only overflows (and scrolls) when a word is wider than its column.
/// </summary>
internal sealed class TablePanel : Panel
{
    private double _viewport = double.PositiveInfinity;
    private double[] _widths = [];
    private double[] _heights = [];

    /// <summary>
    /// Gets or sets the columns.
    /// </summary>
    public int Columns { get; set; } = 1;

    /// <summary>The width the enclosing scroller can show without scrolling; set by <see cref="TableScroller"/>.</summary>
    public double Viewport
    {
        get => _viewport;
        set
        {
            if (_viewport.Equals(value))
            {
                return;
            }

            _viewport = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// Calculates the desired size of the table by measuring child cells and distributing available width across columns based on their natural and minimum size requirements.
    /// </summary>
    /// <param name="availableSize">The available size.</param>
    /// <returns>The size result.</returns>
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
        foreach (var w in _widths)
        {
            width += w;
        }

        foreach (var h in _heights)
        {
            height += h;
        }

        return new Size(width, height);
    }

    /// <summary>
    /// Arranges the child elements in a grid layout based on the specified column count and the predefined width and height dimensions.
    /// </summary>
    /// <param name="finalSize">The final size.</param>
    /// <returns>The size result.</returns>
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
