// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Models;
using AutocadTools.Services;
using AutocadTools.Views;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutocadTools.Commands
{
    public sealed class DrawingSymbolCommands
    {
        private static DrawingSymbolOptions _section = new DrawingSymbolOptions();
        private static DrawingSymbolOptions _break = new DrawingSymbolOptions { Kind = DrawingSymbolKind.Zigzag, Mark = "а" };
        private static readonly Dictionary<DrawingSymbolKind, DrawingSymbolOptions> Saved = new Dictionary<DrawingSymbolKind, DrawingSymbolOptions>();

        [CommandMethod("ATSECTION", CommandFlags.Modal)]
        [CommandMethod("АТРАЗРЕЗ", CommandFlags.Modal)]
        public void Section() => Run(true);

        internal static void Run(bool section, DrawingSymbolOptions initial = null)
        {
            var document = AcadApp.DocumentManager.MdiActiveDocument;
            if (document == null) return;
            var editor = document.Editor;
            var database = document.Database;
            try
            {
                var settings = initial == null ? (section ? _section : _break).Copy() :
                    Saved.TryGetValue(initial.Kind, out var saved) ? saved.Copy() : initial.Copy();
                if (database.CurrentSpaceId == SymbolUtilityServices.GetBlockPaperSpaceId(database)) settings.Scale = 1;
                var window = new DrawingSymbolWindow(settings);
                if (AcadApp.ShowModalWindow(window) != true) return;
                settings = window.Options.Copy();
                DrawingSymbolService.EnsureTextStyle(database);
                Saved[settings.Kind] = settings.Copy();
                if (settings.IsSection) _section = settings.Copy(); else if (settings.IsBreak) _break = settings.Copy();
                var first = editor.GetPoint("\nУкажите начало обозначения: ");
                if (first.Status != PromptStatus.OK) return;
                var ucs = editor.CurrentUserCoordinateSystem;
                var origin = first.Value;
                var placement = ucs * Matrix3d.Displacement(origin.GetAsVector());
                var localPoints = new List<Point2d> { Point2d.Origin };
                double units = DrawingSymbolService.UnitsPerMillimeter(database);
                bool stepped = settings.Kind == DrawingSymbolKind.SteppedSection;
                if (!settings.NeedsEndPoint) localPoints.Add(new Point2d(100 * settings.Scale * units, 0));
                while (settings.NeedsEndPoint)
                {
                    var options = new PromptPointOptions(stepped ? "\nСледующая точка перехода или конец [Готово/Назад]: " : "\nУкажите конец обозначения: ")
                    { UseBasePoint = true, BasePoint = new Point3d(localPoints.Last().X + origin.X, localPoints.Last().Y + origin.Y, origin.Z) };
                    if (stepped)
                    {
                        options.Keywords.Add("Done", "Готово", "Готово");
                        options.Keywords.Add("Undo", "Назад", "Назад");
                        options.AllowNone = true;
                    }
                    if (!stepped)
                    {
                        using (var jig = new DrawingSymbolJig(database, settings, units, placement, localPoints, false))
                        {
                            if (editor.Drag(jig).Status != PromptStatus.OK) return;
                            localPoints.Add(jig.Point);
                        }
                        try { DrawingSymbolGeometry.Build(localPoints, settings, units); }
                        catch (ArgumentException ex)
                        {
                            editor.WriteMessage("\n" + ex.Message);
                            localPoints.RemoveAt(localPoints.Count - 1);
                            continue;
                        }
                        break;
                    }
                    var next = editor.GetPoint(options);
                    if (next.Status == PromptStatus.Keyword && next.StringResult == "Undo")
                    { if (localPoints.Count > 1) localPoints.RemoveAt(localPoints.Count - 1); continue; }
                    if (next.Status == PromptStatus.None || next.Status == PromptStatus.Keyword && next.StringResult == "Done")
                    {
                        try { DrawingSymbolGeometry.Build(localPoints, settings, units); break; }
                        catch (ArgumentException ex) { editor.WriteMessage("\n" + ex.Message); continue; }
                    }
                    if (next.Status != PromptStatus.OK) return;
                    if (localPoints.Count >= DrawingSymbolGeometry.MaximumVertices)
                    { editor.WriteMessage("\nДостигнут предел 64 точек. Завершите обозначение или отмените последнюю точку."); continue; }
                    var point = new Point2d(next.Value.X - origin.X, next.Value.Y - origin.Y);
                    if (point.GetDistanceTo(localPoints.Last()) < (2 * settings.StrokeLength + 3) * settings.Scale * units)
                    { editor.WriteMessage("\nУчасток слишком короткий для штрихов выбранного масштаба."); continue; }
                    if (localPoints.Count > 1)
                    {
                        var previous = (localPoints.Last() - localPoints[localPoints.Count - 2]).GetNormal();
                        if (Math.Abs(previous.DotProduct((point - localPoints.Last()).GetNormal())) > 1e-6)
                        { editor.WriteMessage("\nСледующий участок должен быть перпендикулярен предыдущему; используйте ОРТО или объектные привязки."); continue; }
                    }
                    localPoints.Add(point);
                }
                if (settings.Kind == DrawingSymbolKind.MultiLeader)
                {
                    var extra = editor.GetPoint("\nУкажите вторую точку привязки общей выноски: ");
                    if (extra.Status != PromptStatus.OK) return;
                    localPoints.Add(new Point2d(extra.Value.X - origin.X, extra.Value.Y - origin.Y));
                }
                int side = 1;
                if (section)
                {
                    using (var jig = new DrawingSymbolJig(database, settings, units, placement, localPoints, true))
                    {
                        if (editor.Drag(jig).Status != PromptStatus.OK) return;
                        side = jig.Side;
                    }
                }
                var geometry = DrawingSymbolGeometry.Build(localPoints, settings, units, side);
                DrawingSymbolService.Insert(database, geometry, settings, placement);
                editor.Regen();
                editor.WriteMessage("\nОбозначение вставлено. Текст меняется в редакторе атрибутов блока.\n");
            }
            catch (System.Exception ex) { editor.WriteMessage("\n[Autocad Tools] Не удалось создать обозначение: " + ex.Message + "\n"); }
        }
    }

    internal sealed class DrawingSymbolJig : DrawJig, IDisposable
    {
        private readonly Database _database;
        private readonly DrawingSymbolOptions _options;
        private readonly double _units;
        private readonly Matrix3d _placement;
        private readonly List<Point2d> _points;
        private readonly bool _direction;
        private readonly ObjectId _textStyle;
        private readonly List<Entity> _preview = new List<Entity>();
        private Point3d _last;
        public Point2d Point { get; private set; }
        public int Side { get; private set; } = 1;

        public DrawingSymbolJig(Database database, DrawingSymbolOptions options, double units,
            Matrix3d placement, List<Point2d> points, bool direction)
        {
            _database = database; _options = options; _units = units; _placement = placement;
            _points = points; _direction = direction;
            _textStyle = DrawingSymbolService.EnsureTextStyle(database);
            _last = new Point3d(double.MaxValue, 0, 0);
            if (direction) UpdatePreview();
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var start = new Point3d(_points[0].X, _points[0].Y, 0).TransformBy(_placement);
            var input = new JigPromptPointOptions(_direction ? "\nУкажите сторону взгляда: " : "\nУкажите конец обозначения: ")
            {
                BasePoint = start, UseBasePoint = true, Cursor = CursorType.RubberBand,
                UserInputControls = UserInputControls.Accept3dCoordinates | UserInputControls.GovernedByOrthoMode
            };
            var result = prompts.AcquirePoint(input);
            if (result.Status != PromptStatus.OK) return SamplerStatus.Cancel;
            if (_last.DistanceTo(result.Value) < 1e-8) return SamplerStatus.NoChange;
            _last = result.Value;
            var local = result.Value.TransformBy(_placement.Inverse());
            Point = new Point2d(local.X, local.Y);
            if (_direction)
            {
                var axis = _points[1] - _points[0];
                var delta = Point - _points[0];
                Side = axis.X * delta.Y - axis.Y * delta.X < 0 ? -1 : 1;
            }
            UpdatePreview();
            return SamplerStatus.OK;
        }

        private void UpdatePreview()
        {
            Dispose();
            var points = new List<Point2d>(_points);
            if (!_direction) points.Add(Point);
            try
            {
                var geometry = DrawingSymbolGeometry.Build(points, _options, _units, Side);
                _preview.AddRange(DrawingSymbolService.CreateEntities(_database, geometry, _options, false, _textStyle));
            }
            catch (ArgumentException) { }
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            draw.Geometry.PushModelTransform(_placement);
            try { foreach (var entity in _preview) draw.Geometry.Draw(entity); }
            finally { draw.Geometry.PopModelTransform(); }
            return true;
        }

        public void Dispose() { foreach (var entity in _preview) entity.Dispose(); _preview.Clear(); }
    }
}
