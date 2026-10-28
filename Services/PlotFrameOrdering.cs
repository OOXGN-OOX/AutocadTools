// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AutocadTools.Services
{
    public enum PlotSearchOrder { TopToBottom, BottomToTop, LeftToRight, RightToLeft }

    public static class PlotFrameOrdering
    {
        // Group aligned lower-left corners into rows or columns. Compare to the
        // first coordinate in each group so small offsets cannot chain across rows.
        public static List<T> Sort<T>(IEnumerable<T> items, Func<T, double> x, Func<T, double> y,
            PlotSearchOrder order, double tolerance)
        {
            bool rows = order == PlotSearchOrder.TopToBottom || order == PlotSearchOrder.BottomToTop;
            bool descending = order == PlotSearchOrder.TopToBottom || order == PlotSearchOrder.RightToLeft;
            Func<T, double> primary = rows ? y : x;
            var sorted = descending ? items.OrderByDescending(primary) : items.OrderBy(primary);
            var result = new List<T>();
            var group = new List<T>();
            double anchor = 0;
            void Flush()
            {
                result.AddRange(rows ? group.OrderBy(x) : group.OrderByDescending(y));
                group.Clear();
            }
            foreach (var item in sorted)
            {
                if (group.Count > 0 && Math.Abs(primary(item) - anchor) > tolerance) Flush();
                if (group.Count == 0) anchor = primary(item);
                group.Add(item);
            }
            Flush();
            return result;
        }
    }
}
