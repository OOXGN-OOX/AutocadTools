// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    public static class DrawingAnnotationGeometry
    {
        public static SymbolGeometry Build(IReadOnlyList<Point2d> points, DrawingSymbolOptions options, double k)
        {
            var geometry = new SymbolGeometry { TextHeight = options.TextHeight * k };
            var origin = points[0];
            var delta = points[1] - origin;
            if (options.NeedsEndPoint && delta.Length < 4 * k)
                throw new ArgumentException("Укажите точки на расстоянии не менее 4 мм на бумаге.");
            Point2d At(double x, double y = 0) => origin + new Vector2d(x, y);
            double h = geometry.TextHeight;
            double TextWidth(string text, double height) => Math.Max(height, text.Length * height * 0.8);
            void Stroke(params Point2d[] vertices) => geometry.Strokes.Add(new SymbolStroke(0, false, vertices));
            void Label(Point2d position, string tag = "VALUE", string text = null, double factor = 1) =>
                geometry.Labels.Add(new SymbolLabel(position, tag, text, factor));
            void Circle(Point2d center, double radius)
            {
                geometry.Strokes.Add(new SymbolStroke(0, false, center + new Vector2d(-radius, 0), center + new Vector2d(radius, 0),
                    center + new Vector2d(-radius, 0)) { Bulges = new[] { 1.0, 1.0, 0.0 } });
            }
            void Arrow(Point2d head, Vector2d direction)
            {
                var normal = direction.GetPerpendicularVector();
                geometry.Strokes.Add(new SymbolStroke(0, true, head,
                    head - direction * (3 * k) + normal * (0.6 * k), head - direction * (3 * k) - normal * (0.6 * k)));
            }
            void Shelf(Point2d elbow)
            {
                double sign = delta.X < 0 ? -1 : 1;
                double width = Math.Max(12 * k, Math.Max(TextWidth(options.Designation, h), TextWidth(options.Note, 2.5 * k)) + 4 * k);
                var end = elbow + new Vector2d(sign * width, 0);
                Stroke(elbow, end);
                var center = elbow + new Vector2d(sign * width / 2, 0);
                Label(center + new Vector2d(0, h), "VALUE");
                if (!string.IsNullOrWhiteSpace(options.Note))
                    Label(center + new Vector2d(0, -3.5 * k), "NOTE", options.Note, 2.5 / options.TextHeight);
            }

            if (options.Kind == DrawingSymbolKind.DetailTitle)
            {
                double r = options.Diameter * k / 2;
                if (TextWidth(options.Designation, h) > 1.6 * r) throw new ArgumentException("Уменьшите высоту текста или длину обозначения узла.");
                Circle(origin, r);
                if (string.IsNullOrWhiteSpace(options.Note)) Label(origin);
                else
                {
                    if (TextWidth(options.Note, 2.5 * k) > 1.6 * r) throw new ArgumentException("Номер листа не помещается в обозначении узла.");
                    Stroke(At(-r), At(r));
                    Label(At(0, r / 2)); Label(At(0, -r / 2), "NOTE", options.Note, 2.5 / options.TextHeight);
                }
            }
            else if (options.Kind == DrawingSymbolKind.PlanElevation)
            {
                double width = TextWidth(options.Designation, h) + 4 * k;
                double height = h + 4 * k;
                Stroke(At(-width / 2, -height / 2), At(width / 2, -height / 2), At(width / 2, height / 2),
                    At(-width / 2, height / 2), At(-width / 2, -height / 2));
                Label(origin);
            }
            else if (options.Kind == DrawingSymbolKind.Elevation)
            {
                double side = delta.X < 0 ? -1 : 1;
                double width = Math.Max(Math.Abs(delta.X), TextWidth(options.Designation, h) + 4 * k);
                double rise = Math.Max(Math.Abs(delta.Y), 8 * k);
                double a = 3 * k / Math.Sqrt(2);
                Stroke(At(-a, a), origin, At(a, a));
                Stroke(origin, At(0, rise), At(side * width, rise));
                Label(At(side * width / 2, rise + h));
            }
            else if (options.Kind == DrawingSymbolKind.CoordinateAxis)
            {
                double r = options.Diameter * k / 2;
                if (TextWidth(options.Designation, h) > options.Diameter * k - 2 * k)
                    throw new ArgumentException("Обозначение не помещается в кружке оси. Уменьшите высоту текста.");
                if (delta.Length < r + 5 * k) throw new ArgumentException("Увеличьте длину осевой линии.");
                Circle(origin, r); Label(origin);
                var axis = delta.GetNormal();
                double cursor = r;
                while (cursor < delta.Length)
                {
                    double end = Math.Min(delta.Length, cursor + 8 * k);
                    Stroke(origin + axis * cursor, origin + axis * end);
                    cursor = end + 1.5 * k;
                    if (cursor < delta.Length)
                    {
                        end = Math.Min(delta.Length, cursor + 0.5 * k);
                        Stroke(origin + axis * cursor, origin + axis * end);
                        cursor = end + 1.5 * k;
                    }
                    if (geometry.Strokes.Count > 4096) throw new ArgumentException("Слишком длинная осевая линия для выбранного масштаба.");
                }
            }
            else if (options.Kind == DrawingSymbolKind.ViewDirection || options.IsSlope)
            {
                var axis = delta.GetNormal();
                var across = axis.GetPerpendicularVector();
                Stroke(origin, points[1]);
                if (options.Kind == DrawingSymbolKind.SlopeSign)
                    Stroke(origin, origin + across * (3 * k), origin + axis * (8 * k));
                else Arrow(points[1], axis);
                Label((options.Kind == DrawingSymbolKind.ViewDirection ? points[1] - axis * (5 * k) : origin + delta * 0.5) + across * (h * 1.5));
            }
            else if (options.Kind == DrawingSymbolKind.Fragment)
            {
                if (delta.Length < 15 * k) throw new ArgumentException("Для фигурной скобки нужна длина не менее 15 мм на бумаге.");
                var axis = delta.GetNormal(); var normal = axis.GetPerpendicularVector();
                Point2d Along(double x, double y) => origin + axis * x + normal * y;
                double length = delta.Length;
                geometry.Strokes.Add(new SymbolStroke(0, false, Along(0, 2 * k), Along(2 * k, 0), Along(length / 2 - 2 * k, 0),
                    Along(length / 2, -2 * k), Along(length / 2 + 2 * k, 0), Along(length - 2 * k, 0), Along(length, 2 * k))
                    { Bulges = new[] { 0.41421356, 0, -0.41421356, 0.41421356, 0, 0.41421356, 0 } });
                Label(Along(length / 2, -2 * k - h * 1.5));
            }
            else if (options.Kind == DrawingSymbolKind.ImageTitle) Label(origin);
            else if (options.Kind >= DrawingSymbolKind.DetailCircle && options.Kind <= DrawingSymbolKind.DetailRectangle || options.IsLeader)
            {
                var leaderStart = origin;
                if (options.HasOutline)
                {
                    double r = options.Diameter * k / 2;
                    if (delta.Length < r + 4 * k) throw new ArgumentException("Выведите полку за границу обозначения узла.");
                    if (options.Kind == DrawingSymbolKind.DetailCircle) Circle(origin, r);
                    else if (options.Kind == DrawingSymbolKind.DetailOval)
                    {
                        var ellipse = new Point2d[65];
                        for (int i = 0; i <= 64; i++) ellipse[i] = At(r * Math.Cos(i * Math.PI / 32), r * 0.65 * Math.Sin(i * Math.PI / 32));
                        Stroke(ellipse);
                    }
                    else
                    {
                        double ry = r * 0.65, c = Math.Min(k, r / 3);
                        geometry.Strokes.Add(new SymbolStroke(0, false, At(-r + c, -ry), At(r - c, -ry), At(r, -ry + c),
                            At(r, ry - c), At(r - c, ry), At(-r + c, ry), At(-r, ry - c), At(-r, -ry + c), At(-r + c, -ry))
                            { Bulges = new[] { 0.0, 0.41421356, 0, 0.41421356, 0, 0.41421356, 0, 0.41421356, 0 } });
                    }
                    double t = options.Kind == DrawingSymbolKind.DetailCircle ? r / delta.Length :
                        options.Kind == DrawingSymbolKind.DetailOval ? r / Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y / (0.65 * 0.65)) :
                        Math.Min(Math.Abs(delta.X) < 1e-12 ? double.MaxValue : r / Math.Abs(delta.X),
                            Math.Abs(delta.Y) < 1e-12 ? double.MaxValue : r * 0.65 / Math.Abs(delta.Y));
                    leaderStart = origin + delta * t;
                }
                Stroke(leaderStart, points[1]);
                if (options.Kind == DrawingSymbolKind.LeaderArrow) Arrow(origin, -delta.GetNormal());
                if (options.Kind == DrawingSymbolKind.LeaderDot || options.Kind == DrawingSymbolKind.MultiLeader)
                {
                    double r = 0.5 * k;
                    geometry.Strokes.Add(new SymbolStroke(0, true, At(-r, -r), At(r, -r), At(r, r), At(-r, r)));
                }
                if (options.Kind == DrawingSymbolKind.MultiLeader)
                {
                    var extra = points.Count > 2 ? points[2] : origin + new Vector2d(8 * k, -8 * k);
                    Stroke(extra, points[1]);
                    double r = 0.5 * k;
                    geometry.Strokes.Add(new SymbolStroke(0, true, extra + new Vector2d(-r, -r), extra + new Vector2d(r, -r),
                        extra + new Vector2d(r, r), extra + new Vector2d(-r, r)));
                }
                Shelf(points[1]);
            }
            else throw new ArgumentException("Неизвестный тип обозначения.");
            return geometry;
        }
    }
}
