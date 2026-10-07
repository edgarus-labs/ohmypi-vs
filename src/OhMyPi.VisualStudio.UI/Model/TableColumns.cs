using System;
using System.Collections.Generic;

namespace OhMyPi.VisualStudio.UI.Model
{
    /// <summary>Distributes the width available to a table over its columns.</summary>
    internal static class TableColumns
    {
        /// <summary>
        /// Column widths for <paramref name="available"/> space: the <paramref name="natural"/> widths when they fit (or
        /// the space is unbounded); otherwise the columns shrink by a common factor, except that no column drops below its
        /// <paramref name="minimum"/> (the longest unbreakable word), so the total equals the space. When even the
        /// minimums do not fit they are returned as they are and the table overflows.
        /// </summary>
        public static double[] Fit(IReadOnlyList<double> natural, IReadOnlyList<double> minimum, double available)
        {
            var count = natural.Count;
            var result = new double[count];
            double naturalTotal = 0, minimumTotal = 0;
            for (var i = 0; i < count; i++)
            {
                naturalTotal += natural[i];
                minimumTotal += minimum[i];
            }
            if (double.IsNaN(available) || double.IsInfinity(available) || naturalTotal <= available)
            {
                for (var i = 0; i < count; i++) result[i] = natural[i];
                return result;
            }
            if (minimumTotal >= available)
            {
                for (var i = 0; i < count; i++) result[i] = minimum[i];
                return result;
            }
            var floored = new bool[count];
            while (true)
            {
                double flexible = 0, fixedTotal = 0;
                for (var i = 0; i < count; i++)
                {
                    if (floored[i]) fixedTotal += minimum[i];
                    else flexible += natural[i];
                }
                var factor = flexible > 0 ? (available - fixedTotal) / flexible : 0;
                var changed = false;
                for (var i = 0; i < count; i++)
                {
                    if (floored[i] || natural[i] * factor >= minimum[i]) continue;
                    floored[i] = true;
                    changed = true;
                }
                if (changed) continue;
                for (var i = 0; i < count; i++) result[i] = floored[i] ? minimum[i] : natural[i] * factor;
                return result;
            }
        }
    }
}
