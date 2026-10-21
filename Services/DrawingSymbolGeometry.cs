// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.Geometry;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    public sealed class SymbolStroke
    {
        public Point2d[] Points { get; }
        public double Width { get; }
        public bool Filled { get; }
        public double[] Bulges { get; set; }
        public SymbolStroke(double width, bool filled, params Point2d[] points)
        { Width = width; Filled = filled; Points = points; }
    }

    public sealed class SymbolLabel
    {
        public Point2d Position { get; }
        public string Tag { get; }
        public string Text { get; }
        public double HeightFactor { get; }
        public SymbolLabel(Point2d position, string tag, string text = null, double heightFactor = 1)
        { Position = position; Tag = tag; Text = text; HeightFactor = heightFactor; }
    }

    public sealed class SymbolGeometry
    {
        public List<SymbolStroke> Strokes { get; } = new List<SymbolStroke>();
        public List<SymbolLabel> Labels { get; } = new List<SymbolLabel>();
        public double TextHeight { get; set; }
    }

    public static class DrawingSymbolGeometry
    {
        public const int MaximumVertices = 64;
        public static SymbolGeometry Build(IReadOnlyList<Point2d> points, DrawingSymbolOptions options,
            double unitsPerMillimeter, int side = 1)
        {
            options.Validate();
            if (!double.IsFinite(unitsPerMillimeter) || unitsPerMillimeter <= 0)
                throw new ArgumentException("Некорректные единицы чертежа.");
            if (points == null || points.Count < 2 || points.Count > MaximumVertices ||
                points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
                throw new ArgumentException("Укажите от 2 до 64 точек обозначения.");
            double k = options.Scale * unitsPerMillimeter;
            var geometry = new SymbolGeometry { TextHeight = options.TextHeight * k };
            if (!options.IsSection && !options.IsBreak)
                return DrawingAnnotationGeometry.Build(points, options, k);
            if (options.IsSection)
            {
                double stroke = options.StrokeLength * k;
                var directions = new Vector2d[points.Count - 1];
                for (int i = 0; i < directions.Length; i++)
                {
                    var vector = points[i + 1] - points[i];
                    if (vector.Length < 2 * stroke + 3 * k)
                        throw new ArgumentException("Каждый участок линии разреза должен быть не короче " +
                            (2 * options.StrokeLength + 3) + " мм на бумаге. Увеличьте длину или уменьшите масштаб.");
                    directions[i] = vector.GetNormal();
                }
                if (options.Kind == DrawingSymbolKind.Section && points.Count != 2)
                    throw new ArgumentException("Для прямого разреза нужны две точки.");
                if (options.Kind == DrawingSymbolKind.SteppedSection)
                {
                    for (int i = 1; i < directions.Length; i++)
                        if (Math.Abs(directions[i].DotProduct(directions[i - 1])) > 1e-6)
                            throw new ArgumentException("Соседние участки ступенчатого разреза должны быть перпендикулярны.");
                    if (Math.Abs(directions[0].DotProduct(directions[directions.Length - 1])) < 1 - 1e-6)
                        throw new ArgumentException("Первый и последний участки ступенчатого разреза должны быть параллельны.");
                }
                geometry.Strokes.Add(new SymbolStroke(options.MainThickness * k, false, points[0], points[0] + directions[0] * stroke));
                int last = points.Count - 1;
                geometry.Strokes.Add(new SymbolStroke(options.MainThickness * k, false, points[last] - directions[last - 1] * stroke, points[last]));
                for (int i = 1; i < last; i++)
                    geometry.Strokes.Add(new SymbolStroke(options.MainThickness * k, false,
                        points[i] - directions[i - 1] * stroke, points[i], points[i] + directions[i] * stroke));
                var normal = directions[0].GetPerpendicularVector() * (side < 0 ? -1 : 1);
                AddArrow(geometry, points[0] + directions[0] * (2.5 * k), normal, k, "MARK_START");
                AddArrow(geometry, points[last] - directions[last - 1] * (2.5 * k), normal, k, "MARK_END");
                return geometry;
            }

            if (points.Count != 2) throw new ArgumentException("Для обрыва нужны две точки.");
            var delta = points[1] - points[0];
            double length = delta.Length;
            if (length < 12 * k) throw new ArgumentException("Длина обозначения должна быть не менее 12 мм на бумаге.");
            var xAxis = delta.GetNormal();
            var yAxis = xAxis.GetPerpendicularVector();
            Point2d At(double x, double y = 0) => points[0] + xAxis * x + yAxis * y;
            double middle = length / 2;
            if (options.Kind == DrawingSymbolKind.Zigzag)
            {
                geometry.Strokes.Add(new SymbolStroke(0, false, At(0), At(middle - 2 * k),
                    At(middle - k, 3 * k), At(middle + k, -3 * k), At(middle + 2 * k), At(length)));
                // Lettering on the zigzag is a user-selectable designation, not an additional GOST requirement.
                double offset = (4 + options.Mark.Length * options.TextHeight * 0.4) * k;
                if (2 * offset + geometry.TextHeight > length)
                    throw new ArgumentException("Для выбранного обозначения нужна более длинная линия с изломом.");
                geometry.Labels.Add(new SymbolLabel(At(middle - offset, geometry.TextHeight * 1.5), "MARK_START"));
                geometry.Labels.Add(new SymbolLabel(At(middle + offset, geometry.TextHeight * 1.5), "MARK_END"));
            }
            else if (options.Kind == DrawingSymbolKind.Wave)
            {
                int waves = (int)Math.Clamp(Math.Round(length / (10 * k)), 1, 128);
                int steps = Math.Min(4096, waves * 24);
                var vertices = new Point2d[steps + 1];
                for (int i = 0; i <= steps; i++)
                    vertices[i] = At(length * i / steps, 0.8 * k * Math.Sin(2 * Math.PI * waves * i / steps));
                geometry.Strokes.Add(new SymbolStroke(0, false, vertices));
                geometry.Labels.Add(new SymbolLabel(At(middle, geometry.TextHeight * 1.5), "MARK_START"));
            }
            else
            {
                double stub = 3 * k;
                geometry.Strokes.Add(new SymbolStroke(options.MainThickness * k, false, At(0), At(stub)));
                geometry.Strokes.Add(new SymbolStroke(options.MainThickness * k, false, At(length - stub), At(length)));
                int dots = (int)Math.Clamp(Math.Ceiling((length - 2 * stub) / (1.5 * k)), 2, 2000);
                for (int i = 1; i < dots; i++)
                {
                    double x = stub + (length - 2 * stub) * i / dots;
                    double r = options.MainThickness * k * 0.35;
                    geometry.Strokes.Add(new SymbolStroke(0, true,
                        At(x - r, -r), At(x + r, -r), At(x + r, r), At(x - r, r)));
                }
                geometry.Labels.Add(new SymbolLabel(At(stub, geometry.TextHeight * 1.5), "MARK_START"));
                geometry.Labels.Add(new SymbolLabel(At(length - stub, geometry.TextHeight * 1.5), "MARK_END"));
            }
            return geometry;
        }

        private static void AddArrow(SymbolGeometry geometry, Point2d head, Vector2d direction, double k, string tag)
        {
            var across = direction.GetPerpendicularVector();
            geometry.Strokes.Add(new SymbolStroke(0, false, head - direction * (8 * k), head));
            geometry.Strokes.Add(new SymbolStroke(0, true, head,
                head - direction * (3 * k) + across * (0.6 * k),
                head - direction * (3 * k) - across * (0.6 * k)));
            geometry.Labels.Add(new SymbolLabel(head - direction * (10 * k + geometry.TextHeight), tag));
        }
    }
}
