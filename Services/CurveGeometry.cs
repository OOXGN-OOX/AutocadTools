// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using Autodesk.AutoCAD.DatabaseServices;

namespace AutocadTools.Services
{
    internal static class CurveGeometry
    {
        public static bool TryGetLength(Entity entity, bool includeCircles, out double length)
        {
            length = 0;
            try
            {
                length = entity switch
                {
                    Line line => line.Length,
                    Polyline polyline => polyline.Length,
                    Polyline2d polyline => polyline.Length,
                    Polyline3d polyline => polyline.Length,
                    Arc arc => arc.Length,
                    Circle circle when includeCircles => circle.Circumference,
                    Spline spline => spline.GetDistanceAtParameter(spline.EndParam) - spline.GetDistanceAtParameter(spline.StartParam),
                    _ => 0
                };
                return double.IsFinite(length) && length > 0;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return false; }
        }

        public static double GetArea(Curve curve)
        {
            if (!curve.Closed || curve is not (Polyline or Polyline2d or Circle)) return 0;
            try
            {
                var area = Math.Abs(curve.Area);
                return double.IsFinite(area) ? area : 0;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return 0; }
        }
    }
}
