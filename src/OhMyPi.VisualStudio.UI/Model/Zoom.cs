using System;

namespace OhMyPi.VisualStudio.UI.Model;

/// <summary>Ctrl+wheel zoom levels of the chat control, in the range of the VS text editor (20%–400%).</summary>
internal static class Zoom
{
    public const double Min = 0.2;
    public const double Max = 4.0;
    private const double Factor = 1.1;
    private const int Notch = 120;

    /// <summary>Level after a wheel turn of <paramref name="wheelDelta"/> (positive = zoom in), one 10% step per notch.</summary>
    public static double Step(double current, int wheelDelta)
    {
        if (wheelDelta == 0)
        {
            return current;
        }

        var next = current * Math.Pow(Factor, (double)wheelDelta / Notch);
        if (Math.Abs(next - 1.0) < 1e-9)
        {
            next = 1.0;
        }

        return Math.Max(Min, Math.Min(Max, next));
    }
}
