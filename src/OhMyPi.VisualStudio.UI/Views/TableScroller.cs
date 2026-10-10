using System;
using System.Windows;
using System.Windows.Controls;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>
/// The horizontal scroller around a table. A scroll viewer measures its content without a width limit, so this one
/// first tells the <see cref="TablePanel"/> how wide the viewport is (less the frame chrome around the panel) and
/// the table only scrolls when it cannot fit.
/// </summary>
internal sealed class TableScroller : ScrollViewer
{
    private readonly TablePanel _panel;
    private readonly double _chrome;

    /// <summary>
    /// Initializes a new instance of the TableScroller class with the specified table panel and chrome thickness.
    /// </summary>
    /// <param name="panel">The panel.</param>
    /// <param name="chrome">The chrome.</param>
    public TableScroller(TablePanel panel, double chrome)
    {
        _panel = panel;
        _chrome = chrome;
    }

    /// <summary>
    /// Calculates the available viewport size based on the provided constraints and chrome offset before invoking the base measurement logic.
    /// </summary>
    /// <param name="constraint">The constraint.</param>
    /// <returns>The size result.</returns>
    protected override Size MeasureOverride(Size constraint)
    {
        _panel.Viewport = Math.Max(0, constraint.Width - _chrome);

        return base.MeasureOverride(constraint);
    }
}
