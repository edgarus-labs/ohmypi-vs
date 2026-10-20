using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OhMyPi.VisualStudio.UI.Views;

/// <summary>The mark of a slash command: a speech bubble holding "/_" on a dark rounded tile, drawn on a 100 x 100 grid.</summary>
internal static class SlashIcon
{
    private const double Extent = 100;

    private static readonly Brush Tile = Frozen(new SolidColorBrush(Color.FromRgb(0x1F, 0x24, 0x33)));
    private static readonly Brush Glyph = Frozen(new SolidColorBrush(Color.FromRgb(0xE3, 0xEA, 0xF8)));
    private static readonly Brush Bubble = Frozen(new LinearGradientBrush(Color.FromRgb(0x2D, 0xD4, 0xFF), Color.FromRgb(0x7C, 0x6C, 0xFF), new Point(0, 0), new Point(1, 1)));

    /// <summary>The bubble's outline with its tail at the bottom left, as one closed path.</summary>
    private static readonly Geometry Outline = Frozen(Geometry.Parse(
        "M 43.5 71.2 L 72 71.2 A 11.4 11.4 0 0 0 83.4 59.8 L 83.4 31.6 A 11.4 11.4 0 0 0 72 20.2 L 27.9 20.2 " +
        "A 11.4 11.4 0 0 0 16.5 31.6 L 16.5 59.8 A 12.4 12.4 0 0 0 28.9 71.2 L 28.9 80.5 Z"));

    /// <summary>A new icon <paramref name="size"/> device-independent pixels square.</summary>
    public static FrameworkElement Create(double size)
    {
        var canvas = new Canvas { Width = Extent, Height = Extent };
        canvas.Children.Add(new Rectangle { Width = Extent, Height = Extent, RadiusX = 22, RadiusY = 22, Fill = Tile });
        canvas.Children.Add(new Path
        {
            Data = Outline,
            Stroke = Bubble,
            StrokeThickness = 4.8,
            StrokeLineJoin = PenLineJoin.Round,
        });
        canvas.Children.Add(Stroke(31.5, 57, 43.9, 34, 5));
        canvas.Children.Add(Stroke(50, 54.8, 66.5, 54.8, 4.6));
        var icon = new Viewbox { Width = size, Height = size, Child = canvas, Focusable = false };
        AutomationProperties.SetName(icon, "Slash command");

        return icon;
    }

    private static Line Stroke(double x1, double y1, double x2, double y2, double thickness) => new Line
    {
        X1 = x1,
        Y1 = y1,
        X2 = x2,
        Y2 = y2,
        Stroke = Glyph,
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();

        return freezable;
    }
}
