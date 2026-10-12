// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;

namespace AutocadTools.Commands
{
    /// <summary>
    /// Команда мультикопирования объектов вдоль линии с заданным интервалом
    /// </summary>
    public class MultiCopyCommand
    {
        [CommandMethod("MULTICOPY", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void ExecuteMultiCopy()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                // 1. Получение выбранных объектов
                SelectionSet selectedObjects = null;
                PromptSelectionResult psr = ed.SelectImplied();

                if (psr.Status == PromptStatus.OK && psr.Value.Count > 0)
                {
                    selectedObjects = psr.Value;
                    ed.WriteMessage($"\nВыбрано объектов: {selectedObjects.Count}");
                }
                else
                {
                    PromptSelectionOptions pso = new PromptSelectionOptions();
                    pso.MessageForAdding = "\nВыберите объекты для мультикопирования: ";
                    psr = ed.GetSelection(pso);

                    if (psr.Status == PromptStatus.OK)
                    {
                        selectedObjects = psr.Value;
                    }
                    else
                    {
                        return;
                    }
                }

                if (selectedObjects == null || selectedObjects.Count == 0)
                {
                    ed.WriteMessage("\nОбъекты не выбраны.");
                    return;
                }

                // Сбросить implied selection
                ed.SetImpliedSelection(new ObjectId[0]);

                // 2. Запрос расстояния между копиями
                PromptDistanceOptions pdo = new PromptDistanceOptions("\nУкажите расстояние или ");
                pdo.Keywords.Add("Поделить");
                pdo.AllowNegative = false;
                pdo.AllowZero = false;
                pdo.DefaultValue = 1000.0;
                pdo.UseDefaultValue = true;

                bool useDivide = false;
                double spacing = 1000.0;
                int divideCount = 0;

                PromptDoubleResult pdrDist = ed.GetDistance(pdo);

                if (pdrDist.Status == PromptStatus.Keyword && pdrDist.StringResult == "Поделить")
                {
                    useDivide = true;
                    PromptIntegerOptions pio = new PromptIntegerOptions("\nВведите количество копий: ");
                    pio.AllowNegative = false;
                    pio.AllowZero = false;
                    pio.DefaultValue = 5;
                    pio.UseDefaultValue = true;

                    PromptIntegerResult pir = ed.GetInteger(pio);
                    if (pir.Status != PromptStatus.OK) return;
                    divideCount = pir.Value;
                }
                else if (pdrDist.Status == PromptStatus.OK)
                {
                    spacing = pdrDist.Value;
                }
                else
                {
                    return;
                }

                // 3. Запрос базовой точки
                PromptPointOptions ppoBase = new PromptPointOptions("\nУкажите базовую точку или:");
                ppoBase.Keywords.Add("Примитив");
                PromptPointResult pprBase = ed.GetPoint(ppoBase);

                Point3d basePoint;
                if (pprBase.Status == PromptStatus.Keyword && pprBase.StringResult == "Примитив")
                {
                    // Использовать центр bounding box выбранных объектов
                    basePoint = GetSelectionCenter(db, selectedObjects);
                    ed.WriteMessage($"\nИспользуется центр примитивов: {basePoint.X:F2}, {basePoint.Y:F2}");
                }
                else if (pprBase.Status == PromptStatus.OK)
                {
                    basePoint = pprBase.Value;
                }
                else
                {
                    return;
                }

                // 4. Запуск jig для интерактивного копирования
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Собираем клонируемые объекты
                    List<Entity> sourceEntities = new List<Entity>();
                    foreach (SelectedObject so in selectedObjects)
                    {
                        if (so.ObjectId.IsErased) continue;
                        Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                        if (ent != null)
                        {
                            sourceEntities.Add(ent);
                        }
                    }

                    if (sourceEntities.Count == 0)
                    {
                        ed.WriteMessage("\nНет объектов для копирования.");
                        tr.Abort();
                        return;
                    }

                    // Создаем и запускаем jig
                    MultiCopyJig jig = new MultiCopyJig(sourceEntities, basePoint, spacing, useDivide, divideCount);
                    PromptResult jigResult = ed.Drag(jig);

                    if (jigResult.Status == PromptStatus.OK)
                    {
                        // Создаем копии
                        BlockTableRecord currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                        Point3d endPoint = jig.EndPoint;

                        Vector3d direction = endPoint - basePoint;
                        double totalDistance = direction.Length;

                        if (totalDistance < 1e-6)
                        {
                            ed.WriteMessage("\nРасстояние слишком мало.");
                            tr.Abort();
                            return;
                        }

                        Vector3d unitDirection = direction.GetNormal();

                        int copyCount;
                        double actualSpacing;

                        if (useDivide)
                        {
                            copyCount = divideCount;
                            actualSpacing = totalDistance / (divideCount + 1);
                        }
                        else
                        {
                            copyCount = (int)Math.Floor(totalDistance / spacing);
                            actualSpacing = spacing;
                        }

                        int createdCount = 0;
                        for (int i = 1; i <= copyCount; i++)
                        {
                            Vector3d offset = unitDirection * (actualSpacing * i);
                            Matrix3d transform = Matrix3d.Displacement(offset);

                            foreach (Entity srcEnt in sourceEntities)
                            {
                                Entity clone = srcEnt.Clone() as Entity;
                                if (clone != null)
                                {
                                    clone.TransformBy(transform);
                                    currentSpace.AppendEntity(clone);
                                    tr.AddNewlyCreatedDBObject(clone, true);
                                    createdCount++;
                                }
                            }
                        }

                        tr.Commit();
                        ed.WriteMessage($"\nСоздано {createdCount} объект(ов) в {copyCount} копи(ях).");
                    }
                    else
                    {
                        tr.Abort();
                        ed.WriteMessage("\nОперация отменена.");
                    }
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nОшибка: {ex.Message}");
            }
        }

        private Point3d GetSelectionCenter(Database db, SelectionSet ss)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Point3d minPt = new Point3d(double.MaxValue, double.MaxValue, double.MaxValue);
                Point3d maxPt = new Point3d(double.MinValue, double.MinValue, double.MinValue);

                foreach (SelectedObject so in ss)
                {
                    if (so.ObjectId.IsErased) continue;
                    Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent != null)
                    {
                        try
                        {
                            Extents3d ext = ent.GeometricExtents;
                            minPt = new Point3d(
                                Math.Min(minPt.X, ext.MinPoint.X),
                                Math.Min(minPt.Y, ext.MinPoint.Y),
                                Math.Min(minPt.Z, ext.MinPoint.Z));
                            maxPt = new Point3d(
                                Math.Max(maxPt.X, ext.MaxPoint.X),
                                Math.Max(maxPt.Y, ext.MaxPoint.Y),
                                Math.Max(maxPt.Z, ext.MaxPoint.Z));
                        }
                        catch { }
                    }
                }

                tr.Commit();
                return new Point3d(
                    (minPt.X + maxPt.X) / 2.0,
                    (minPt.Y + maxPt.Y) / 2.0,
                    (minPt.Z + maxPt.Z) / 2.0);
            }
        }
    }

    /// <summary>
    /// Jig для интерактивного отображения копий при движении мыши
    /// </summary>
    public class MultiCopyJig : DrawJig
    {
        private readonly List<Entity> _sourceEntities;
        private readonly Point3d _basePoint;
        private readonly double _spacing;
        private readonly bool _useDivide;
        private readonly int _divideCount;

        private Point3d _currentPoint;
        private List<Entity> _previewEntities;

        public Point3d EndPoint => _currentPoint;

        public MultiCopyJig(List<Entity> sourceEntities, Point3d basePoint, double spacing, bool useDivide, int divideCount)
        {
            _sourceEntities = sourceEntities;
            _basePoint = basePoint;
            _spacing = spacing;
            _useDivide = useDivide;
            _divideCount = divideCount;
            _currentPoint = basePoint;
            _previewEntities = new List<Entity>();
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions jppo = new JigPromptPointOptions("\nУкажите вторую точку: ");
            jppo.BasePoint = _basePoint;
            jppo.UseBasePoint = true;
            jppo.Cursor = CursorType.RubberBand;

            PromptPointResult ppr = prompts.AcquirePoint(jppo);

            if (ppr.Status == PromptStatus.OK)
            {
                if (_currentPoint.DistanceTo(ppr.Value) < 1e-6)
                {
                    return SamplerStatus.NoChange;
                }
                _currentPoint = ppr.Value;
                return SamplerStatus.OK;
            }

            return SamplerStatus.Cancel;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            // Рисуем линию от базовой точки до текущей
            draw.Geometry.WorldLine(_basePoint, _currentPoint);

            // Вычисляем параметры копирования
            Vector3d direction = _currentPoint - _basePoint;
            double totalDistance = direction.Length;

            if (totalDistance < 1e-6) return true;

            Vector3d unitDirection = direction.GetNormal();

            int copyCount;
            double actualSpacing;

            if (_useDivide)
            {
                copyCount = _divideCount;
                actualSpacing = totalDistance / (_divideCount + 1);
            }
            else
            {
                copyCount = (int)Math.Floor(totalDistance / _spacing);
                actualSpacing = _spacing;
            }

            // Рисуем превью копий
            for (int i = 1; i <= copyCount; i++)
            {
                Vector3d offset = unitDirection * (actualSpacing * i);
                Matrix3d transform = Matrix3d.Displacement(offset);

                foreach (Entity srcEnt in _sourceEntities)
                {
                    try
                    {
                        Entity clone = srcEnt.Clone() as Entity;
                        if (clone != null)
                        {
                            clone.TransformBy(transform);
                            draw.Geometry.Draw(clone);
                            clone.Dispose();
                        }
                    }
                    catch { }
                }
            }

            return true;
        }
    }
}
