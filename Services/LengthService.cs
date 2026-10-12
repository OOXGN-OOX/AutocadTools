// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System.Collections.Generic;
using System.Linq;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    public class LengthService
    {
        public List<LengthGroup> CalculateLengths(Transaction tr, IEnumerable<ObjectId> ids)
        {
            var groups = new Dictionary<string, LengthGroup>();

            foreach (ObjectId id in ids)
            {
                if (id.IsErased) continue;

                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                double length = 0.0;
                double area = 0.0;
                bool isClosed = false;
                string typeName = "";
                Point3d startPt = Point3d.Origin;
                Point3d endPt = Point3d.Origin;

                // Определение типа и длины
                switch (ent)
                {
                    case Line line:
                        length = line.Length;
                        typeName = "Отрезок";
                        startPt = line.StartPoint;
                        endPt = line.EndPoint;
                        break;
                    case Polyline pl:
                        length = pl.Length;
                        typeName = "Полилиния";
                        startPt = pl.StartPoint;
                        endPt = pl.EndPoint;
                        isClosed = pl.Closed;
                        if (isClosed)
                        {
                            try { area = pl.Area; } catch { }
                        }
                        break;
                    case Polyline2d pl2d:
                        length = pl2d.Length;
                        typeName = "2D Полилиния";
                        startPt = pl2d.StartPoint;
                        endPt = pl2d.EndPoint;
                        isClosed = pl2d.Closed;
                        if (isClosed)
                        {
                            try { area = pl2d.Area; } catch { }
                        }
                        break;
                    case Polyline3d pl3d:
                        length = pl3d.Length;
                        typeName = "3D Полилиния";
                        startPt = pl3d.StartPoint;
                        endPt = pl3d.EndPoint;
                        isClosed = pl3d.Closed;
                        break;
                    case Arc arc:
                        length = arc.Length;
                        typeName = "Дуга";
                        startPt = arc.StartPoint;
                        endPt = arc.EndPoint;
                        break;
                    case Circle circle:
                        length = circle.Circumference;
                        area = circle.Area;
                        isClosed = true;
                        typeName = "Окружность";
                        startPt = circle.Center;
                        endPt = circle.Center;
                        break;
                    case Spline spline:
                        try { length = spline.GetDistanceAtParameter(spline.EndParam); } catch { }
                        typeName = "Сплайн";
                        startPt = spline.StartPoint;
                        endPt = spline.EndPoint;
                        isClosed = spline.Closed;
                        break;
                    default:
                        continue;
                }

                if (length <= 0) continue;

                if (!groups.ContainsKey(typeName))
                {
                    groups[typeName] = new LengthGroup { ObjectType = typeName };
                }

                var group = groups[typeName];
                group.Count++;
                group.RawTotalLength += length;
                group.RawTotalArea += area;
                group.Items.Add(new LengthItem
                {
                    ObjectId = id,
                    Handle = ent.Handle.ToString(),
                    RawLength = length,
                    RawArea = area,
                    IsClosed = isClosed,
                    StartPoint = startPt,
                    EndPoint = endPt
                });
            }

            return groups.Values.OrderBy(g => g.ObjectType).ToList();
        }
    }
}