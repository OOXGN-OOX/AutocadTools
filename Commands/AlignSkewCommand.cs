// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System.Collections.Generic;
using AutocadTools.Services;

namespace AutocadTools.Commands
{
    public class AlignSkewCommand
    {
        private readonly TransformCalculator _transformCalculator;

        private bool _preserveTextProps = true;
        private bool _explodeDims = true;
        private bool _explodeLeaders = true;
        private int _blockMode = 1;
        private int _blockPositioningMode = 1;

        private ObjectIdCollection _textObjectsForCorrection;
        private ObjectIdCollection _blockObjectsForCorrection;

        public AlignSkewCommand()
        {
            _transformCalculator = new TransformCalculator();
        }

        [CommandMethod("Align_Den", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void ExecuteAlignSkew()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
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
                    pso.MessageForAdding = "\nВыбор объектов для наклона (Аксонометрия): ";
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
                    ed.WriteMessage("\nОбъекты не выбраны");
                    return;
                }

                PromptPointOptions ppo = new PromptPointOptions("\nБазовая точка для наклона на 45°: ");
                PromptPointResult ppr = ed.GetPoint(ppo);
                if (ppr.Status != PromptStatus.OK) return;

                Point3d basePoint = ppr.Value;

                double skewAngle = Math.PI / 4.0;
                double targetAngleY = (Math.PI / 2.0) - skewAngle;
                double dist = 10.0;

                Point3d z1 = basePoint;
                Point3d z2 = new Point3d(basePoint.X + dist, basePoint.Y, basePoint.Z);
                Point3d z3 = new Point3d(basePoint.X, basePoint.Y + dist, basePoint.Z);

                Point3d s1 = z1;
                Point3d s2 = z2;
                Point3d s3 = new Point3d(
                    basePoint.X + (dist * Math.Cos(targetAngleY)),
                    basePoint.Y + (dist * Math.Sin(targetAngleY)),
                    basePoint.Z
                );

                if (Math.Abs(z1.DistanceTo(z2) - z1.DistanceTo(z3)) < 1e-6)
                {
                    double dZ1Z2 = z1.DistanceTo(z2);
                    double dS1S2 = s1.DistanceTo(s2);

                    z2 = Polar(z1, GetAngle2d(z2, z1), dZ1Z2 * 2.0);
                    s2 = Polar(s1, GetAngle2d(s2, s1), dS1S2 * 2.0);
                }

                double angZ1Z2 = GetAngle2d(z1, z2);
                double angZ1Z3 = GetAngle2d(z1, z3);
                double cosB = Math.Cos(angZ1Z3 - angZ1Z2);

                double angS1S2 = GetAngle2d(s1, s2);
                double angS1S3 = GetAngle2d(s1, s3);
                double cosB1 = Math.Cos(angS1S3 - angS1S2);

                // ИСПРАВЛЕНИЕ: Тут мы просто проверяем условие и меняем точки,
                // переменная im больше не нужна.
                if (Math.Abs(cosB) < 0.01 || Math.Abs(cosB1) < 0.01)
                {
                    Point3d tempZ = z1; z1 = z3; z3 = tempZ;
                    Point3d tempS = s1; s1 = s3; s3 = tempS;
                }

                TransformCalculator.TransformationParams tParams =
                    _transformCalculator.CalculateTransformation(z1, z2, z3, s1, s2, s3);

                ApplyTransformation(db, selectedObjects, z1, s1, tParams);

                ed.WriteMessage("\nВыравнивание выполнено успешно.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nОшибка: {ex.Message}");
            }
        }

        private void ApplyTransformation(Database db, SelectionSet ss, Point3d sourceBase, Point3d targetBase, TransformCalculator.TransformationParams tParams)
        {
            _textObjectsForCorrection = new ObjectIdCollection();
            _blockObjectsForCorrection = new ObjectIdCollection();
            ObjectIdCollection geometryObjects = new ObjectIdCollection();
            Matrix3d transformation = AlignSkewTransform.CreateMatrix(sourceBase, targetBase, tParams);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    BlockTableRecord currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    List<ObjectId> allIdsToProcess = new List<ObjectId>();

                    foreach (SelectedObject so in ss)
                    {
                        if (so.ObjectId.IsErased) continue;

                        Entity ent = (Entity)tr.GetObject(so.ObjectId, OpenMode.ForWrite);
                        bool exploded = false;

                        if (_explodeDims && ent is Dimension)
                        {
                            exploded = ExplodeEntity(ent, currentSpace, tr, allIdsToProcess);
                        }
                        else if (_explodeLeaders && (ent is Leader || ent is MLeader))
                        {
                            exploded = ExplodeEntity(ent, currentSpace, tr, allIdsToProcess);
                        }
                        else if (_blockMode == 1 && ent is BlockReference)
                        {
                            exploded = ExplodeEntity(ent, currentSpace, tr, allIdsToProcess);
                        }

                        if (!exploded)
                        {
                            allIdsToProcess.Add(ent.ObjectId);
                        }
                    }

                    foreach (ObjectId id in allIdsToProcess)
                    {
                        if (id.IsErased) continue;
                        Entity ent = (Entity)tr.GetObject(id, OpenMode.ForRead);

                        bool isText = _preserveTextProps && (ent is DBText || ent is MText);
                        bool isBlockToMove = (_blockMode == 2) && (ent is BlockReference);

                        if (isText)
                        {
                            _textObjectsForCorrection.Add(id);
                        }
                        else if (isBlockToMove)
                        {
                            _blockObjectsForCorrection.Add(id);
                        }
                        else
                        {
                            geometryObjects.Add(id);
                        }
                    }

                    if (geometryObjects.Count > 0)
                    {
                        ObjectId btrId = CreateTemporaryBlock(db, tr, geometryObjects, sourceBase);

                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);
                        Matrix3d rotMat = Matrix3d.Rotation(tParams.Rotation1, Vector3d.ZAxis, sourceBase);

                        foreach (ObjectId id in btr)
                        {
                            if (!id.IsErased)
                            {
                                Entity ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                                ent.TransformBy(rotMat);
                            }
                        }

                        using (BlockReference br = new BlockReference(targetBase, btrId))
                        {
                            br.ScaleFactors = new Scale3d(tParams.ScaleX, tParams.ScaleY, 1.0);
                            br.Rotation = tParams.Rotation2;

                            currentSpace.AppendEntity(br);
                            tr.AddNewlyCreatedDBObject(br, true);

                            DBObjectCollection exploded = new DBObjectCollection();
                            br.Explode(exploded);

                            foreach (DBObject obj in exploded)
                            {
                                Entity ent = obj as Entity;
                                if (ent != null)
                                {
                                    currentSpace.AppendEntity(ent);
                                    tr.AddNewlyCreatedDBObject(ent, true);
                                }
                            }

                            br.Erase();
                        }

                        btr.Erase();
                    }

                    if (_textObjectsForCorrection.Count > 0)
                    {
                        foreach (ObjectId id in _textObjectsForCorrection)
                        {
                            Entity text = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                            AlignSkewTransform.TransformText(text, transformation);
                        }
                    }

                    if (_blockObjectsForCorrection.Count > 0)
                    {
                        ApplyBlockCorrection(tr, transformation);
                    }

                    tr.Commit();
                }
                catch
                {
                    tr.Abort();
                    throw;
                }
            }
        }

        private bool ExplodeEntity(Entity ent, BlockTableRecord ownerSpace, Transaction tr, List<ObjectId> collector)
        {
            try
            {
                if (ent is BlockReference br)
                {
                    return ExplodeBlockWithProperties(br, ownerSpace, tr, collector);
                }

                DBObjectCollection explodedObjs = new DBObjectCollection();
                ent.Explode(explodedObjs);

                foreach (DBObject obj in explodedObjs)
                {
                    Entity newEnt = obj as Entity;
                    if (newEnt != null)
                    {
                        ownerSpace.AppendEntity(newEnt);
                        tr.AddNewlyCreatedDBObject(newEnt, true);
                        collector.Add(newEnt.ObjectId);
                    }
                    else
                    {
                        obj.Dispose();
                    }
                }

                ent.Erase();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool ExplodeBlockWithProperties(BlockReference br, BlockTableRecord ownerSpace, Transaction tr, List<ObjectId> collector)
        {
            try
            {
                Database db = br.Database;
                var blkColor = br.Color;
                var blkLinetypeId = br.LinetypeId;
                var blkLineweight = br.LineWeight;

                Dictionary<string, AttributeReference> atts = new Dictionary<string, AttributeReference>();
                foreach (ObjectId attId in br.AttributeCollection)
                {
                    if (!attId.IsErased)
                    {
                        var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                        if (!string.IsNullOrEmpty(att.Tag) && !atts.ContainsKey(att.Tag))
                            atts[att.Tag] = att;
                    }
                }

                DBObjectCollection exploded = new DBObjectCollection();
                br.Explode(exploded);

                foreach (DBObject obj in exploded)
                {
                    Entity ent = obj as Entity;
                    if (ent == null) { obj.Dispose(); continue; }

                    if (ent.ColorIndex == 0) ent.Color = blkColor;

                    LinetypeTable ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                    if (ent.LinetypeId == ltt["ByBlock"]) ent.LinetypeId = blkLinetypeId;

                    if (ent.LineWeight == LineWeight.ByBlock) ent.LineWeight = blkLineweight;

                    if (ent is AttributeDefinition attDef)
                    {
                        if (atts.ContainsKey(attDef.Tag))
                        {
                            var originalAtt = atts[attDef.Tag];
                            DBText text = new DBText();
                            text.SetDatabaseDefaults();
                            text.Normal = originalAtt.Normal;
                            text.Position = originalAtt.Position;
                            text.TextString = originalAtt.TextString;
                            text.Height = originalAtt.Height;
                            text.Rotation = originalAtt.Rotation;
                            text.HorizontalMode = originalAtt.HorizontalMode;
                            text.VerticalMode = originalAtt.VerticalMode;
                            if (originalAtt.HorizontalMode != TextHorizontalMode.TextLeft || originalAtt.VerticalMode != TextVerticalMode.TextBase)
                            {
                                text.AlignmentPoint = originalAtt.AlignmentPoint;
                            }

                            text.LayerId = originalAtt.LayerId;
                            text.Color = originalAtt.Color;
                            text.LinetypeId = originalAtt.LinetypeId;
                            text.LineWeight = originalAtt.LineWeight;
                            text.TextStyleId = originalAtt.TextStyleId;
                            text.WidthFactor = originalAtt.WidthFactor;
                            text.Oblique = originalAtt.Oblique;
                            text.IsMirroredInX = originalAtt.IsMirroredInX;
                            text.IsMirroredInY = originalAtt.IsMirroredInY;

                            ownerSpace.AppendEntity(text);
                            tr.AddNewlyCreatedDBObject(text, true);
                            collector.Add(text.ObjectId);

                            ent.Dispose();
                            continue;
                        }
                        else
                        {
                            ent.Dispose();
                            continue;
                        }
                    }

                    ownerSpace.AppendEntity(ent);
                    tr.AddNewlyCreatedDBObject(ent, true);
                    collector.Add(ent.ObjectId);
                }

                br.Erase();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private ObjectId CreateTemporaryBlock(Database db, Transaction tr, ObjectIdCollection ids, Point3d basePoint)
        {
            string blockName = "AlignSkewTemp_" + DateTime.Now.Ticks;
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);

            using (BlockTableRecord btr = new BlockTableRecord())
            {
                btr.Name = blockName;
                btr.Origin = basePoint;

                ObjectId btrId = bt.Add(btr);
                tr.AddNewlyCreatedDBObject(btr, true);

                IdMapping mapping = new IdMapping();
                db.DeepCloneObjects(ids, btrId, mapping, false);

                foreach (ObjectId id in ids)
                {
                    if (!id.IsErased)
                    {
                        Entity ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                        ent.Erase();
                    }
                }
                return btrId;
            }
        }

        private double GetAngle2d(Point3d p1, Point3d p2)
        {
            return Math.Atan2(p2.Y - p1.Y, p2.X - p1.X);
        }

        private Point3d Polar(Point3d basePt, double angle, double dist)
        {
            return new Point3d(basePt.X + dist * Math.Cos(angle), basePt.Y + dist * Math.Sin(angle), basePt.Z);
        }

        private void ApplyBlockCorrection(Transaction tr, Matrix3d transformation)
        {
            foreach (ObjectId objId in _blockObjectsForCorrection)
            {
                BlockReference br = (BlockReference)tr.GetObject(objId, OpenMode.ForWrite);
                Point3d blockPosition;

                if (_blockPositioningMode == 1)
                {
                    Extents3d? ext = br.GeometricExtents;
                    if (ext.HasValue)
                    {
                        Point3d minPt = ext.Value.MinPoint;
                        Point3d maxPt = ext.Value.MaxPoint;
                        blockPosition = new Point3d(
                            (minPt.X + maxPt.X) / 2.0,
                            (minPt.Y + maxPt.Y) / 2.0,
                            (minPt.Z + maxPt.Z) / 2.0
                        );
                    }
                    else
                    {
                        blockPosition = br.Position;
                    }
                }
                else
                {
                    blockPosition = br.Position;
                }

                Vector3d moveVector = blockPosition.TransformBy(transformation) - blockPosition;
                Matrix3d moveMatrix = Matrix3d.Displacement(moveVector);
                br.TransformBy(moveMatrix);
            }
        }
    }
}
