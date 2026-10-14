// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Services;

namespace AutocadTools.Commands
{
    public class MultiCopyCommand
    {
        private static double _spacing = 1000;
        private static int _segments = 5;
        private static bool _divide;

        [CommandMethod("MULTICOPY", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void ExecuteMultiCopy()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;
            try
            {
                var selection = ed.SelectImplied();
                if (selection.Status != PromptStatus.OK || selection.Value.Count == 0)
                    selection = ed.GetSelection(new PromptSelectionOptions
                    {
                        MessageForAdding = "\nВыберите объекты для мультикопирования: ",
                        RejectObjectsOnLockedLayers = true
                    });
                if (selection.Status != PromptStatus.OK) return;
                var ids = selection.Value.GetObjectIds();
                ed.SetImpliedSelection(Array.Empty<ObjectId>());
                if (!ReadParameters(ed)) return;
                var options = new PromptPointOptions("\nУкажите базовую точку или: ");
                options.Keywords.Add("Primitive", "Примитив", "Примитив");
                var point = ed.GetPoint(options);
                Vector3d[] offsets;
                if (point.Status == PromptStatus.Keyword)
                {
                    var pathOptions = new PromptEntityOptions("\nВыберите отрезок, полилинию или сплайн: ");
                    pathOptions.SetRejectMessage("\nДопустимы отрезок, полилиния или сплайн.");
                    pathOptions.AddAllowedClass(typeof(Line), true);
                    pathOptions.AddAllowedClass(typeof(Autodesk.AutoCAD.DatabaseServices.Polyline), true);
                    pathOptions.AddAllowedClass(typeof(Polyline2d), true);
                    pathOptions.AddAllowedClass(typeof(Polyline3d), true);
                    pathOptions.AddAllowedClass(typeof(Spline), true);
                    var path = ed.GetEntity(pathOptions);
                    if (path.Status != PromptStatus.OK) return;
                    using (var tr = db.TransactionManager.StartTransaction())
                        offsets = MultiCopyService.AlongCurve((Curve)tr.GetObject(path.ObjectId, OpenMode.ForRead),
                            path.PickedPoint, _spacing, _segments, _divide);
                }
                else if (point.Status == PromptStatus.OK)
                {
                    using (var jig = new MultiCopyJig(db, ids, point.Value, _spacing, _segments, _divide))
                    {
                        if (ed.Drag(jig).Status != PromptStatus.OK) return;
                        offsets = MultiCopyService.AlongLine(point.Value, jig.EndPoint, _spacing, _segments, _divide);
                    }
                }
                else return;
                if (offsets.Length == 0) { ed.WriteMessage("\nНа указанной длине нет места для копий."); return; }
                int created = MultiCopyService.Copy(db, ids, offsets);
                ed.WriteMessage($"\nСоздано {created} объектов в {offsets.Length} копиях.");
            }
            catch (System.Exception ex) { ed.WriteMessage("\nОшибка мультикопирования: " + ex.Message); }
        }

        private static bool ReadParameters(Editor ed)
        {
            while (true)
            {
                if (_divide)
                {
                    var options = new PromptIntegerOptions("\nВведите число сегментов или: ")
                    {
                        AllowNegative = false, AllowZero = false,
                        LowerLimit = 1, UpperLimit = MultiCopyLayout.MaximumCopies,
                        DefaultValue = _segments, UseDefaultValue = true
                    };
                    options.Keywords.Add("Measure", "Разметить", "Разметить");
                    var result = ed.GetInteger(options);
                    if (result.Status == PromptStatus.Keyword) { _divide = false; continue; }
                    if (result.Status != PromptStatus.OK) return false;
                    _segments = result.Value;
                }
                else
                {
                    var options = new PromptDistanceOptions("\nУкажите расстояние или: ")
                    {
                        AllowNegative = false, AllowZero = false,
                        DefaultValue = _spacing, UseDefaultValue = true
                    };
                    options.Keywords.Add("Divide", "Поделить", "Поделить");
                    var result = ed.GetDistance(options);
                    if (result.Status == PromptStatus.Keyword) { _divide = true; continue; }
                    if (result.Status != PromptStatus.OK) return false;
                    _spacing = result.Value;
                }
                return true;
            }
        }
    }

    public sealed class MultiCopyJig : DrawJig, IDisposable
    {
        private readonly List<Entity> _preview = new List<Entity>();
        private readonly Point3d _basePoint;
        private readonly double _spacing;
        private readonly int _segments;
        private readonly bool _divide;
        private Point3d _currentPoint;
        private Vector3d[] _offsets = Array.Empty<Vector3d>();
        public Point3d EndPoint => _currentPoint;

        public MultiCopyJig(Database db, ObjectId[] ids, Point3d basePoint, double spacing, int segments, bool divide)
        {
            _basePoint = _currentPoint = basePoint;
            _spacing = spacing; _segments = segments; _divide = divide;
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                    foreach (var id in ids)
                        if (!id.IsErased) _preview.Add((Entity)tr.GetObject(id, OpenMode.ForRead).Clone());
            }
            catch { Dispose(); throw; }
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var options = new JigPromptPointOptions("\nУкажите вторую точку: ")
            {
                BasePoint = _basePoint, UseBasePoint = true, Cursor = CursorType.RubberBand,
                UserInputControls = UserInputControls.Accept3dCoordinates | UserInputControls.GovernedByOrthoMode
            };
            var result = prompts.AcquirePoint(options);
            if (result.Status != PromptStatus.OK) return SamplerStatus.Cancel;
            if (_currentPoint.DistanceTo(result.Value) < 1e-6) return SamplerStatus.NoChange;
            _currentPoint = result.Value;
            try { _offsets = MultiCopyService.AlongLine(_basePoint, _currentPoint, _spacing, _segments, _divide); }
            catch (InvalidOperationException) { _offsets = Array.Empty<Vector3d>(); }
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            draw.Geometry.WorldLine(_basePoint, _currentPoint);
            if (_offsets.Length > 100) return true;
            foreach (var offset in _offsets)
            {
                draw.Geometry.PushModelTransform(Matrix3d.Displacement(offset));
                try { foreach (var entity in _preview) draw.Geometry.Draw(entity); }
                finally { draw.Geometry.PopModelTransform(); }
            }
            return true;
        }

        public void Dispose()
        {
            foreach (var entity in _preview) entity.Dispose();
            _preview.Clear();
        }
    }
}
