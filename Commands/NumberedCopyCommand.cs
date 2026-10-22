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
using AutocadTools.Models;
using AutocadTools.Services;
using AutocadTools.Views;

namespace AutocadTools.Commands
{
    public class NumberedCopyCommand
    {
        [CommandMethod("ATNCOPY", CommandFlags.UsePickSet | CommandFlags.Modal)]
        [CommandMethod("АТНКОП", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void Execute()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor; var db = doc.Database;
            try
            {
                var implied = ed.SelectImplied();
                ObjectId id;
                if (implied.Status == PromptStatus.OK && implied.Value.Count == 1) id = implied.Value.GetObjectIds()[0];
                else
                {
                    var select = new PromptEntityOptions("\nВыберите текст, блок с атрибутами или мультивыноску: ");
                    select.SetRejectMessage("\nНужен текст, МТекст, блок или текстовая мультивыноска.");
                    select.AddAllowedClass(typeof(DBText), true); select.AddAllowedClass(typeof(MText), true);
                    select.AddAllowedClass(typeof(BlockReference), true); select.AddAllowedClass(typeof(MLeader), true);
                    var result = ed.GetEntity(select); if (result.Status != PromptStatus.OK) return; id = result.ObjectId;
                }
                ed.SetImpliedSelection(Array.Empty<ObjectId>());
                var target = NumberedCopyService.Read(db, id);
                var window = new NumberedCopyWindow(target.Sources, NumberedCopySettings.Load());
                if (Application.ShowModalWindow(window) != true) return;
                var options = window.Options; NumberedCopySettings.Save(options);
                if (options.Mode == NumberedCopyMode.Points) PlacePoints(ed, db, target, options);
                else PlaceCurve(ed, db, target, options);
            }
            catch (System.Exception ex) { ed.WriteMessage("\nОшибка копирования с нумерацией: " + ex.Message); }
        }

        internal static void PlacePoints(Editor ed, Database db, NumberedCopyTarget target, NumberedCopyOptions options)
        {
            var point = ed.GetPoint(new PromptPointOptions("\nБазовая точка <точка вставки исходника>: ") { AllowNone = true });
            if (point.Status != PromptStatus.OK && point.Status != PromptStatus.None) return;
            var basePoint = point.Status == PromptStatus.None ? target.Anchor : point.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            var created = new List<ObjectId>();
            while (created.Count < NumberedCopyOptions.MaximumCopies)
            {
                using (var jig = new NumberedCopyJig(db, target, options, created.Count + 1, basePoint))
                {
                    var result = ed.Drag(jig);
                    if (jig.UndoRequested)
                    {
                        if (created.Count == 0) { ed.WriteMessage("\nНет размещённых копий."); continue; }
                        using (var tr = db.TransactionManager.StartTransaction())
                        { tr.GetObject(created[created.Count - 1], OpenMode.ForWrite).Erase(); tr.Commit(); }
                        created.RemoveAt(created.Count - 1); continue;
                    }
                    if (result.Status != PromptStatus.OK) break;
                    var ids = NumberedCopyService.Copy(db, target, options,
                        new[] { Matrix3d.Displacement(jig.Point - basePoint) }, created.Count + 1);
                    created.Add(ids[0]);
                }
            }
            ed.WriteMessage($"\nСоздано копий: {created.Count}. Исходный объект сохранён.");
        }

        internal static void PlaceCurve(Editor ed, Database db, NumberedCopyTarget target, NumberedCopyOptions options)
        {
            var select = new PromptEntityOptions("\nВыберите кривую у начала обхода: ");
            select.SetRejectMessage("\nНужен отрезок, дуга, окружность, эллипс, полилиния или сплайн.");
            foreach (var type in new[] { typeof(Line), typeof(Arc), typeof(Circle), typeof(Ellipse), typeof(Autodesk.AutoCAD.DatabaseServices.Polyline),
                typeof(Polyline2d), typeof(Polyline3d), typeof(Spline) }) select.AddAllowedClass(type, true);
            var path = ed.GetEntity(select); if (path.Status != PromptStatus.OK) return;
            var transforms = NumberedCopyPath.Create(db, path.ObjectId, path.PickedPoint.TransformBy(ed.CurrentUserCoordinateSystem),
                target, options, ed.CurrentUserCoordinateSystem);
            if (transforms.Length == 0) { ed.WriteMessage("\nНа кривой нет места для копий с заданным шагом."); return; }
            int first = options.OnCurve ? 0 : 1;
            // Check the entire numeric range before asking for confirmation or modifying the database.
            NumberedCopyText.Format(target.Sources[options.SourceIndex], options, first + transforms.Length - 1);
            var confirm = new PromptKeywordOptions($"\nСоздать {transforms.Length} копий?") { AllowNone = true };
            confirm.Keywords.Add("Yes", "Да", "Да"); confirm.Keywords.Add("No", "Нет", "Нет"); confirm.Keywords.Default = "Yes";
            var answer = ed.GetKeywords(confirm);
            if (answer.Status != PromptStatus.None && (answer.Status != PromptStatus.OK || answer.StringResult != "Yes")) return;
            var created = NumberedCopyService.Copy(db, target, options, transforms, first);
            ed.WriteMessage($"\nСоздано копий: {created.Length}. Исходный объект сохранён.");
        }
    }

    public sealed class NumberedCopyJig : DrawJig, IDisposable
    {
        private readonly Entity[] _preview;
        private readonly Point3d _basePoint;
        public Point3d Point { get; private set; }
        public bool UndoRequested { get; private set; }
        public NumberedCopyJig(Database db, NumberedCopyTarget target, NumberedCopyOptions options, int index, Point3d basePoint)
        { _basePoint = Point = basePoint; _preview = NumberedCopyService.Preview(db, target, options, index); }
        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var options = new JigPromptPointOptions("\nТочка копии; Enter / Esc — завершить")
            {
                BasePoint = _basePoint, UseBasePoint = true, Cursor = CursorType.RubberBand,
                UserInputControls = UserInputControls.Accept3dCoordinates | UserInputControls.GovernedByOrthoMode | UserInputControls.NullResponseAccepted
            };
            options.Keywords.Add("Undo", "Отменить", "Отменить");
            var result = prompts.AcquirePoint(options);
            if (result.Status == PromptStatus.Keyword) { UndoRequested = result.StringResult == "Undo"; return SamplerStatus.Cancel; }
            if (result.Status != PromptStatus.OK) return SamplerStatus.Cancel;
            if (Point.DistanceTo(result.Value) < 1e-8) return SamplerStatus.NoChange;
            Point = result.Value; return SamplerStatus.OK;
        }
        protected override bool WorldDraw(WorldDraw draw)
        {
            draw.Geometry.PushModelTransform(Matrix3d.Displacement(Point - _basePoint));
            try { foreach (var entity in _preview) draw.Geometry.Draw(entity); }
            finally { draw.Geometry.PopModelTransform(); }
            return true;
        }
        public void Dispose() { foreach (var entity in _preview) entity.Dispose(); }
    }
}
