// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    public sealed class LengthService
    {
        public List<LengthGroup> CalculateLengths(Transaction transaction, IEnumerable<ObjectId> ids)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            var groups = new Dictionary<string, LengthGroup>(StringComparer.Ordinal);
            foreach (var id in ids)
            {
                if (id.IsNull || !id.IsValid || id.IsErased) continue;
                try
                {
                    if (transaction.GetObject(id, OpenMode.ForRead) is not Curve curve ||
                        !CurveGeometry.TryGetLength(curve, includeCircles: true, out var length)) continue;
                    var typeName = curve switch
                    {
                        Line => "Отрезок",
                        Polyline => "Полилиния",
                        Polyline2d => "2D Полилиния",
                        Polyline3d => "3D Полилиния",
                        Arc => "Дуга",
                        Circle => "Окружность",
                        Spline => "Сплайн",
                        _ => null
                    };
                    var area = CurveGeometry.GetArea(curve);
                    if (!groups.TryGetValue(typeName, out var group))
                        groups.Add(typeName, group = new LengthGroup { ObjectType = typeName });
                    group.Count++;
                    group.RawTotalLength += length;
                    group.RawTotalArea += area;
                    var circle = curve as Circle;
                    group.Items.Add(new LengthItem
                    {
                        ObjectId = id,
                        Handle = curve.Handle.ToString(),
                        RawLength = length,
                        RawArea = area,
                        IsClosed = curve.Closed,
                        StartPoint = circle?.Center ?? curve.StartPoint,
                        EndPoint = circle?.Center ?? curve.EndPoint
                    });
                }
                catch (Autodesk.AutoCAD.Runtime.Exception) { /* Skip unavailable native objects. */ }
            }
            return groups.Values.OrderBy(group => group.ObjectType).ToList();
        }
    }
}
