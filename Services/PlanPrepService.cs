// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;
using System;
using System.Collections.Generic;
using System.Linq;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    public class PlanPrepService
    {
        private readonly PlanPrepOptions _options;
        private readonly Database _db;
        private readonly Transaction _tr;
        private readonly BlockTableRecord _currentSpace;

        public PlanPrepService(PlanPrepOptions options, Database db, Transaction tr)
        {
            _options = options;
            _db = db;
            _tr = tr;
            _currentSpace = (BlockTableRecord)_tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
        }

        public void Process(IEnumerable<ObjectId> initialSelection)
        {
            List<ObjectId> workingSet = initialSelection.ToList();

            // 1 & 2. Взрывание
            if (_options.ExplodeAllBlocks || _options.ExplodeHatches)
            {
                workingSet = RecursiveExplode(workingSet);
            }

            // 3. Удаление Solid штриховок
            if (_options.DeleteSolidHatches)
            {
                workingSet = DeleteSolids(workingSet);
            }

            // 4. Удаление Маскировок (НОВОЕ)
            if (_options.DeleteWipeouts)
            {
                workingSet = DeleteWipeouts(workingSet);
            }

            // 5. Удаление Точек (НОВОЕ)
            if (_options.DeletePoints)
            {
                workingSet = DeletePoints(workingSet);
            }

            // 6. Полилинии
            if (_options.JoinLines)
            {
                workingSet = JoinLinesToPolylines(workingSet);
            }

            // 7. Короткие сегменты
            if (_options.DeleteShortSegments)
            {
                workingSet = DeleteShortEntities(workingSet);
            }

            // 8. Цвет
            if (_options.SetGrayColor)
            {
                SetColorToGray(workingSet);
            }

            // 9. Блок
            if (_options.CreateBlock && workingSet.Count > 0)
            {
                CreateBlockFromEntities(workingSet);
            }
        }

        // --- 1. Взрывание ---
        private List<ObjectId> RecursiveExplode(List<ObjectId> inputIds)
        {
            var resultIds = new List<ObjectId>();
            var queue = new Queue<ObjectId>(inputIds);

            while (queue.Count > 0)
            {
                ObjectId id = queue.Dequeue();
                if (id.IsErased) continue;

                Entity ent = (Entity)_tr.GetObject(id, OpenMode.ForWrite);
                bool exploded = false;

                if (_options.ExplodeAllBlocks && ent is BlockReference blkRef)
                {
                    using (BlockTableRecord btrDef = (BlockTableRecord)_tr.GetObject(blkRef.BlockTableRecord, OpenMode.ForRead))
                    {
                        if (!btrDef.Explodable)
                        {
                            btrDef.UpgradeOpen();
                            btrDef.Explodable = true;
                            btrDef.DowngradeOpen();
                        }
                    }

                    DBObjectCollection fragments = new DBObjectCollection();
                    try
                    {
                        blkRef.Explode(fragments);
                        foreach (DBObject obj in fragments)
                        {
                            Entity newEnt = (Entity)obj;
                            _currentSpace.AppendEntity(newEnt);
                            _tr.AddNewlyCreatedDBObject(newEnt, true);
                            queue.Enqueue(newEnt.ObjectId);
                        }
                        ent.Erase();
                        exploded = true;
                    }
                    catch { }
                }
                else if (_options.ExplodeHatches && ent is Hatch hatch)
                {
                    if (_options.DeleteSolidHatches && IsSolid(hatch))
                    {
                        ent.Erase();
                        exploded = true;
                    }
                    else
                    {
                        DBObjectCollection fragments = new DBObjectCollection();
                        try
                        {
                            hatch.Explode(fragments);
                            foreach (DBObject obj in fragments)
                            {
                                Entity newEnt = (Entity)obj;
                                _currentSpace.AppendEntity(newEnt);
                                _tr.AddNewlyCreatedDBObject(newEnt, true);
                                resultIds.Add(newEnt.ObjectId);
                            }
                            ent.Erase();
                            exploded = true;
                        }
                        catch { }
                    }
                }

                if (!exploded)
                {
                    resultIds.Add(id);
                }
            }
            return resultIds;
        }

        // --- 3. Удаление Солидов ---
        private List<ObjectId> DeleteSolids(List<ObjectId> inputIds)
        {
            var validIds = new List<ObjectId>();
            foreach (var id in inputIds)
            {
                if (id.IsErased) continue;
                Entity ent = (Entity)_tr.GetObject(id, OpenMode.ForRead);

                bool isSolid = false;
                if (ent is Hatch h && IsSolid(h)) isSolid = true;
                if (ent is Solid) isSolid = true;

                if (isSolid)
                {
                    ent.UpgradeOpen();
                    ent.Erase();
                }
                else
                {
                    validIds.Add(id);
                }
            }
            return validIds;
        }

        private bool IsSolid(Hatch hatch)
        {
            return hatch.PatternName.Equals("SOLID", StringComparison.InvariantCultureIgnoreCase);
        }

        // --- 4. Удаление Маскировок (Wipeout) ---
        private List<ObjectId> DeleteWipeouts(List<ObjectId> inputIds)
        {
            var validIds = new List<ObjectId>();
            foreach (var id in inputIds)
            {
                if (id.IsErased) continue;
                Entity ent = (Entity)_tr.GetObject(id, OpenMode.ForRead);

                if (ent is Wipeout)
                {
                    ent.UpgradeOpen();
                    ent.Erase();
                }
                else
                {
                    validIds.Add(id);
                }
            }
            return validIds;
        }

        // --- 5. Удаление Точек (DBPoint) ---
        private List<ObjectId> DeletePoints(List<ObjectId> inputIds)
        {
            var validIds = new List<ObjectId>();
            foreach (var id in inputIds)
            {
                if (id.IsErased) continue;
                Entity ent = (Entity)_tr.GetObject(id, OpenMode.ForRead);

                if (ent is DBPoint)
                {
                    ent.UpgradeOpen();
                    ent.Erase();
                }
                else
                {
                    validIds.Add(id);
                }
            }
            return validIds;
        }

        // --- 6. Соединение линий (Join) ---
        private List<ObjectId> JoinLinesToPolylines(List<ObjectId> inputIds)
        {
            var polylinesToJoin = new List<Polyline>();
            var otherEntities = new List<ObjectId>();

            foreach (var id in inputIds)
            {
                if (id.IsErased) continue;
                Entity ent = (Entity)_tr.GetObject(id, OpenMode.ForWrite);

                if (ent is Line line)
                {
                    Polyline pl = new Polyline();
                    pl.SetDatabaseDefaults();
                    pl.AddVertexAt(0, new Point2d(line.StartPoint.X, line.StartPoint.Y), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(line.EndPoint.X, line.EndPoint.Y), 0, 0, 0);
                    pl.Elevation = line.StartPoint.Z;
                    pl.Layer = line.Layer;
                    pl.Linetype = line.Linetype;
                    pl.Color = line.Color;

                    _currentSpace.AppendEntity(pl);
                    _tr.AddNewlyCreatedDBObject(pl, true);
                    polylinesToJoin.Add(pl);

                    line.Erase();
                }
                else if (ent is Polyline pl)
                {
                    polylinesToJoin.Add(pl);
                }
                else
                {
                    otherEntities.Add(id);
                }
            }

            bool joinedSomething = true;
            while (joinedSomething && polylinesToJoin.Count > 0)
            {
                joinedSomething = false;
                var source = polylinesToJoin[0];
                polylinesToJoin.RemoveAt(0);

                for (int i = polylinesToJoin.Count - 1; i >= 0; i--)
                {
                    var candidate = polylinesToJoin[i];
                    try
                    {
                        if (CanJoin(source, candidate))
                        {
                            source.JoinEntity(candidate);
                            candidate.Erase();
                            polylinesToJoin.RemoveAt(i);
                            joinedSomething = true;
                        }
                    }
                    catch { }
                }
                otherEntities.Add(source.ObjectId);
            }

            foreach (var p in polylinesToJoin) otherEntities.Add(p.ObjectId);

            return otherEntities;
        }

        private bool CanJoin(Polyline p1, Polyline p2)
        {
            double tol = 0.01;
            return p1.StartPoint.DistanceTo(p2.StartPoint) < tol ||
                   p1.StartPoint.DistanceTo(p2.EndPoint) < tol ||
                   p1.EndPoint.DistanceTo(p2.StartPoint) < tol ||
                   p1.EndPoint.DistanceTo(p2.EndPoint) < tol;
        }

        // --- 7. Удаление коротких ---
        private List<ObjectId> DeleteShortEntities(List<ObjectId> inputIds)
        {
            var validIds = new List<ObjectId>();
            double limit = 0.01;

            foreach (var id in inputIds)
            {
                if (id.IsErased) continue;
                Entity ent = (Entity)_tr.GetObject(id, OpenMode.ForRead);

                double length = -1;
                if (ent is Curve crv)
                {
                    try
                    {
                        length = crv.GetDistanceAtParameter(crv.EndParam) - crv.GetDistanceAtParameter(crv.StartParam);
                    }
                    catch { }
                }

                if (length >= 0 && length < limit)
                {
                    ent.UpgradeOpen();
                    ent.Erase();
                }
                else
                {
                    validIds.Add(id);
                }
            }
            return validIds;
        }

        // --- 8. Цвет серый ---
        private void SetColorToGray(List<ObjectId> inputIds)
        {
            foreach (var id in inputIds)
            {
                if (id.IsErased) continue;
                Entity ent = (Entity)_tr.GetObject(id, OpenMode.ForWrite);
                ent.ColorIndex = 8;
            }
        }

        // --- 9. Создание блока ---
        private void CreateBlockFromEntities(List<ObjectId> inputIds)
        {
            Extents3d ext = new Extents3d();
            bool hasExt = false;

            var validIds = new ObjectIdCollection();
            foreach (var id in inputIds) if (!id.IsErased) validIds.Add(id);
            if (validIds.Count == 0) return;

            foreach (ObjectId id in validIds)
            {
                Entity ent = (Entity)_tr.GetObject(id, OpenMode.ForRead);
                try
                {
                    if (!hasExt) { ext = ent.GeometricExtents; hasExt = true; }
                    else ext.AddExtents(ent.GeometricExtents);
                }
                catch { }
            }

            Point3d center = hasExt ? ext.MinPoint + (ext.MaxPoint - ext.MinPoint) / 2.0 : Point3d.Origin;

            string blkName = "PLAN_PREP_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            BlockTable bt = (BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForWrite);

            using (BlockTableRecord btr = new BlockTableRecord())
            {
                btr.Name = blkName;
                btr.Origin = center;
                bt.Add(btr);
                _tr.AddNewlyCreatedDBObject(btr, true);

                IdMapping map = new IdMapping();
                _db.DeepCloneObjects(validIds, btr.ObjectId, map, false);

                foreach (ObjectId id in validIds)
                {
                    Entity ent = (Entity)_tr.GetObject(id, OpenMode.ForWrite);
                    ent.Erase();
                }

                using (BlockReference br = new BlockReference(center, btr.ObjectId))
                {
                    _currentSpace.AppendEntity(br);
                    _tr.AddNewlyCreatedDBObject(br, true);
                }
            }
        }
    }
}