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
