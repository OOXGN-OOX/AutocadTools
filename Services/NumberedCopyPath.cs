// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    public static class NumberedCopyPath
    {
        public static Matrix3d[] Create(Database db, ObjectId pathId, Point3d pick, NumberedCopyTarget target,
            NumberedCopyOptions options, Matrix3d ucs)
        {
            options.Validate();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var curve = tr.GetObject(pathId, OpenMode.ForRead) as Curve;
                if (curve == null || curve is Xline || curve is Ray) throw new ArgumentException("Выберите конечную кривую.");
                double length = curve.GetDistanceAtParameter(curve.EndParam) - curve.GetDistanceAtParameter(curve.StartParam);
                if (length < 1e-9 || double.IsNaN(length) || double.IsInfinity(length)) throw new ArgumentException("У кривой нет конечной ненулевой длины.");
                bool reverse = !curve.Closed && pick.DistanceTo(curve.EndPoint) < pick.DistanceTo(curve.StartPoint);
                double origin = curve.Closed ? curve.GetDistAtPoint(curve.GetClosestPointTo(pick, false)) : reverse ? length : 0;
                double[] stations;
                if (options.Vertices)
                {
                    var vertices = VertexDistances(curve, tr);
                    if (curve.Closed) origin = vertices.OrderBy(d => curve.GetPointAtDist(d).DistanceTo(pick)).First();
                    stations = vertices.Select(d => curve.Closed ? Wrap(d - origin, length) : reverse ? length - d : d)
                        .Where(d => options.OnCurve || d > 1e-8).Distinct().OrderBy(d => d).ToArray();
                    if (stations.Length > NumberedCopyOptions.MaximumCopies) throw new ArgumentException("Более 10000 вершин полилинии.");
                }
                else stations = Stations(length, curve.Closed, options);
                var axes = ucs.CoordinateSystem3d;
                var shift = axes.Xaxis.GetNormal() * options.OffsetX + axes.Yaxis.GetNormal() * options.OffsetY;
                var relative = options.OnCurve ? new Vector3d() : target.Anchor - curve.GetPointAtDist(origin);
                var result = new Matrix3d[stations.Length];
                for (int i = 0; i < stations.Length; i++)
                {
                    double distance = origin + (reverse ? -1 : 1) * stations[i];
                    if (curve.Closed) distance = Wrap(distance, length);
                    distance = Math.Max(0, Math.Min(length, distance));
                    var point = curve.GetPointAtDist(distance);
                    var destination = point + relative + shift;
                    if (options.Orientation == NumberedCopyOrientation.Original)
                    { result[i] = Matrix3d.Displacement(destination - target.Anchor); continue; }
                    var tangent = curve.GetFirstDerivative(point) * (reverse ? -1 : 1);
                    if (tangent.Length < 1e-9) throw new ArgumentException("Не удалось определить касательную к кривой.");
                    var x = tangent.GetNormal();
                    var normal = axes.Zaxis - x * axes.Zaxis.DotProduct(x);
                    if (normal.Length < 1e-8) normal = axes.Yaxis - x * axes.Yaxis.DotProduct(x);
                    normal = normal.GetNormal();
                    if (options.Orientation == NumberedCopyOrientation.Left) x = normal.CrossProduct(x).GetNormal();
                    if (options.Orientation == NumberedCopyOrientation.Right) x = x.CrossProduct(normal).GetNormal();
                    var y = normal.CrossProduct(x).GetNormal();
                    result[i] = Matrix3d.AlignCoordinateSystem(target.Anchor, target.AxisX,
                        target.Normal.CrossProduct(target.AxisX).GetNormal(), target.Normal, destination, x, y, normal);
                }
                return result;
            }
        }

        private static double[] Stations(double length, bool closed, NumberedCopyOptions options)
        {
            double first, step; int count;
            if (options.LimitCount && options.Divide)
            {
                count = options.Count;
                step = options.OnCurve
                    ? closed ? length / count : count == 1 ? 0 : length / (count - 1)
                    : length / (closed ? count + 1 : count);
                first = options.OnCurve ? 0 : step;
            }
            else
            {
                step = options.Distance; first = options.OnCurve ? 0 : step;
                double available = Math.Floor((length - first) / step + 1e-9) + 1;
                if (closed && available > 0 && first + (available - 1) * step >= length - length * 1e-9) available--;
                if (options.LimitCount) available = Math.Min(available, options.Count);
                if (available > NumberedCopyOptions.MaximumCopies) throw new ArgumentException("Более 10000 копий. Увеличьте шаг или задайте количество.");
                count = (int)Math.Max(0, available);
            }
            return Enumerable.Range(0, count).Select(i => Math.Min(length, first + i * step)).ToArray();
        }

        private static double[] VertexDistances(Curve curve, Transaction tr)
        {
            int count;
            if (curve is Polyline polyline)
                count = polyline.NumberOfVertices;
            else if (curve is Polyline2d polyline2d)
            {
                if (polyline2d.PolyType != Poly2dType.SimplePoly) throw new ArgumentException("Для сглаженной полилинии выберите размещение по длине.");
                count = polyline2d.Cast<ObjectId>().Count();
            }
            else if (curve is Polyline3d polyline3d)
            {
                if (polyline3d.PolyType != Poly3dType.SimplePoly) throw new ArgumentException("Для сглаженной полилинии выберите размещение по длине.");
                count = polyline3d.Cast<ObjectId>().Count();
            }
            else throw new ArgumentException("Для размещения в вершинах выберите 2D- или 3D-полилинию.");
            // Parameters retain the correct station at repeated / self-intersecting vertices.
            return Enumerable.Range(0, count).Select(i => curve.GetDistanceAtParameter(curve.StartParam + i)).Distinct().ToArray();
        }

        private static double Wrap(double value, double length)
        { double result = value % length; return result < 0 ? result + length : result; }
    }
}
