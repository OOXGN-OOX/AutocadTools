// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Commands;
using AutocadTools.Models;
using AutocadTools.Services;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

public sealed class NumberedCopyChecks
{
    private static string Log => Path.Combine(Environment.GetEnvironmentVariable("AUTOCADTOOLS_NUMCOPY_SCRATCH"), "results.log");
    private static ObjectId _manualSource;
    private int _count;
    private void Check(bool value, string message) { _count++; if (!value) throw new InvalidOperationException(message); }
    private void Near(Point3d actual, Point3d expected, string message) => Check(actual.DistanceTo(expected) < 1e-6, message + ": " + actual + " / " + expected);
    private void Reject(Action action, string message)
    { try { action(); } catch (System.Exception ex) when (ex is ArgumentException || ex is OverflowException || ex is Autodesk.AutoCAD.Runtime.Exception) { Check(true, message); return; } throw new InvalidOperationException(message); }
    private static ObjectId Add(Database db, Entity entity)
    {
        using (var tr = db.TransactionManager.StartTransaction())
        {
            entity.SetDatabaseDefaults(db);
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var id = space.AppendEntity(entity); tr.AddNewlyCreatedDBObject(entity, true); tr.Commit(); return id;
        }
    }
    private static int Count(Database db)
    { using (var tr = db.TransactionManager.StartTransaction()) return ((BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead)).Cast<ObjectId>().Count(id => !id.IsErased); }
    private static void Read<T>(Database db, ObjectId id, Action<T> action) where T : DBObject
    { using (var tr = db.TransactionManager.StartTransaction()) action((T)tr.GetObject(id, OpenMode.ForRead)); }

    [CommandMethod("NUMBERED_COPY_CHECKS")]
    public void Run()
    {
        var previous = HostApplicationServices.WorkingDatabase;
        try
        {
            using (var db = new Database(true, true))
            {
                HostApplicationServices.WorkingDatabase = db;
                var position = new Point3d(100, 200, 30);
                var source = Add(db, new DBText { Position = position, TextString = "П1-007 DN50", Height = 2.5, Rotation = 0.2 });
                var target = NumberedCopyService.Read(db, source);
                var options = new NumberedCopyOptions { SourceIndex = 0, NumberIndex = 1, Start = 7, Digits = 3 };
                var saved = Newtonsoft.Json.JsonConvert.DeserializeObject<NumberedCopyOptions>(NumberedCopySettings.Serialize(options));
                Check(NumberedCopyText.Format(target.Sources[0], saved, 1) == "П1-008 DN50", "Settings round trip does not introduce decimal places");
                var shift = Matrix3d.Displacement(new Vector3d(20, 40, 60));
                var copies = NumberedCopyService.Copy(db, target, options, new[] { shift, shift * shift });
                Read<DBText>(db, copies[0], t => { Check(t.TextString == "П1-008 DN50", "Selected occurrence renumbered"); Near(t.Position, position.TransformBy(shift), "Manual copy displacement"); Check(Math.Abs(t.Rotation - 0.2) < 1e-8, "Source rotation preserved"); });
                Read<DBText>(db, copies[1], t => Check(t.TextString == "П1-009 DN50", "Sequential numbers"));
                Read<DBText>(db, source, t => { Check(t.TextString == "П1-007 DN50", "Original text unchanged"); Near(t.Position, position, "Original position unchanged"); });
                var preview = NumberedCopyService.Preview(db, target, options, 3);
                try { Check(((DBText)preview.Single()).TextString == "П1-010 DN50", "Preview shows next number"); Check(preview.All(e => e.ObjectId.IsNull), "Preview does not insert objects"); }
                finally { foreach (var entity in preview) entity.Dispose(); }

                string formatted = @"{\C1;\H0.7x;В-007}\PДу50";
                var mtext = Add(db, new MText { Location = position, Contents = formatted, TextHeight = 2.5, Width = 40 });
                var mtarget = NumberedCopyService.Read(db, mtext);
                options.NumberIndex = 0;
                var mcopy = NumberedCopyService.Copy(db, mtarget, options, new[] { shift })[0];
                Read<MText>(db, mcopy, t => { Check(t.Contents.Contains("008") && t.Contents.Contains(@"\H0.7x;") && t.Contents.Contains("Ду50"), "MText formatting and other number preserved"); Near(t.Location, position.TransformBy(shift), "MText anchor transformed"); });

                using (var content = new MText { Location = position, Contents = @"{\C3;П-007}\PДу50", TextHeight = 2.5 })
                {
                    var leader = new MLeader { ContentType = ContentType.MTextContent, MText = content };
                    var leaderId = Add(db, leader); var ltarget = NumberedCopyService.Read(db, leaderId);
                    var lcopy = NumberedCopyService.Copy(db, ltarget, options, new[] { shift })[0];
                    Read<MLeader>(db, lcopy, l => { using (var t = l.MText) { Check(t.Text.Contains("П-008") && t.Text.Contains("Ду50"), "MLeader text renumbered"); Check(t.Contents.Contains(@"\C3;"), "MLeader inline format retained"); Near(t.Location, ltarget.Anchor.TransformBy(shift), "MLeader location transformed"); } });
                }

                ObjectId blockId;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                    var definition = new BlockTableRecord { Name = "NUMBERED_COPY_FIXTURE" };
                    table.Add(definition); tr.AddNewlyCreatedDBObject(definition, true);
                    var line = new Line(Point3d.Origin, new Point3d(10, 0, 0)); definition.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    var block = new BlockReference(position, definition.ObjectId) { Rotation = 0.4, ScaleFactors = new Scale3d(2, 3, 1) };
                    blockId = space.AppendEntity(block); tr.AddNewlyCreatedDBObject(block, true);
                    foreach (var tag in new[] { "MARK", "NOTE" })
                    {
                        var def = new AttributeDefinition(Point3d.Origin, "", tag, tag, db.Textstyle) { Height = 2.5 };
                        definition.AppendEntity(def); tr.AddNewlyCreatedDBObject(def, true);
                        var attribute = new AttributeReference(); attribute.SetAttributeFromBlock(def, block.BlockTransform);
                        attribute.TextString = tag == "MARK" ? "В-007" : "Ду50";
                        block.AttributeCollection.AppendAttribute(attribute); tr.AddNewlyCreatedDBObject(attribute, true);
                        if (tag == "MARK")
                        {
                            attribute.IsMTextAttribute = true; attribute.UpdateMTextAttribute();
                            using (var text = attribute.MTextAttribute) { text.Contents = @"{\C2;В-007}"; attribute.MTextAttribute = text; }
                            attribute.UpdateMTextAttribute();
                        }
                    }
                    tr.Commit();
                }
                var btarget = NumberedCopyService.Read(db, blockId);
                Check(btarget.Sources.Count == 2, "Choose between multiple block attributes");
                var bcopy = NumberedCopyService.Copy(db, btarget, options, new[] { shift })[0];
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var block = (BlockReference)tr.GetObject(bcopy, OpenMode.ForRead);
                    Near(block.Position, position.TransformBy(shift), "Block insertion moved");
                    Check(block.ScaleFactors.IsEqualTo(new Scale3d(2, 3, 1)) && Math.Abs(block.Rotation - 0.4) < 1e-8, "Block scale and rotation preserved");
                    Check(block.AttributeCollection.Count == 2, "Other block attributes preserved");
                    var mark = (AttributeReference)tr.GetObject(block.AttributeCollection[0], OpenMode.ForRead);
                    Check(mark.IsMTextAttribute && mark.TextString.Contains("008"), "Multiline attribute updated");
                    using (var text = mark.MTextAttribute) Check(text.Contents.Contains(@"\C2;"), "Multiline attribute formatting preserved");
                    Near(mark.Position, position.TransformBy(shift), "Block attribute transformed with block");
                    var note = (AttributeReference)tr.GetObject(block.AttributeCollection[1], OpenMode.ForRead);
                    Check(note.TextString == "Ду50", "Unselected attribute unchanged");
                }
                var bpreview = NumberedCopyService.Preview(db, btarget, options, 2);
                try { Check(bpreview.Length == 3 && bpreview.OfType<AttributeReference>().Any(a => a.TextString.Contains("009")), "Block preview includes renumbered attributes"); }
                finally { foreach (var entity in bpreview) entity.Dispose(); }

                int before = Count(db);
                var overflow = options.Copy(); overflow.Start = decimal.MaxValue - 1;
                Reject(() => NumberedCopyService.Copy(db, mtarget, overflow, new[] { shift, shift }), "Overflow aborts the array");
                Check(Count(db) == before, "Array transaction rolls back earlier clones on failure: " + Count(db) + " / " + before);
                var stretch = Matrix3d.AlignCoordinateSystem(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                    Point3d.Origin, Vector3d.XAxis * 2, Vector3d.YAxis, Vector3d.ZAxis);
                Reject(() => NumberedCopyService.Copy(db, btarget, options, new[] { shift, stretch }), "Geometry error aborts array");
                Check(Count(db) == before, "Geometry failure removes all deep-cloned entities");
                PathChecks(db, target);
                DynamicAndAlignmentChecks(db);
                int protectedCount = Count(db);

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    ((LayerTableRecord)tr.GetObject(layers["0"], OpenMode.ForWrite)).IsLocked = true; tr.Commit();
                }
                Reject(() => NumberedCopyService.Read(db, source), "Locked source rejected");
                Reject(() => NumberedCopyService.Copy(db, target, options, new[] { shift }), "Source locked after settings rejected");
                Check(Count(db) == protectedCount, "Rejected copy does not add objects");
            }
            File.WriteAllText(Log, $"NUMBERED_COPY_PASS: {_count} native database/path checks.\n");
        }
        catch (System.Exception ex) { File.WriteAllText(Log, "NUMBERED_COPY_FAIL: " + ex); }
        finally { HostApplicationServices.WorkingDatabase = previous; }
    }

    private void DynamicAndAlignmentChecks(Database db)
    {
        var point = new Point3d(10, 20, 30);
        var centered = new DBText { TextString = "007", Height = 2.5, Justify = AttachmentPoint.MiddleCenter, AlignmentPoint = point };
        var centeredId = Add(db, centered);
        using (var tr = db.TransactionManager.StartTransaction()) { ((DBText)tr.GetObject(centeredId, OpenMode.ForWrite)).AdjustAlignment(db); tr.Commit(); }
        var centeredTarget = NumberedCopyService.Read(db, centeredId);
        Near(centeredTarget.Anchor, point, "Centered text uses alignment anchor");
        var shift = Matrix3d.Displacement(new Vector3d(30, 40, 50));
        var options = new NumberedCopyOptions { Start = 7, Digits = 3 };
        var centeredCopy = NumberedCopyService.Copy(db, centeredTarget, options, new[] { shift })[0];
        Read<DBText>(db, centeredCopy, t => { Near(t.AlignmentPoint, point.TransformBy(shift), "Center alignment retained after renumbering"); Check(t.Justify == AttachmentPoint.MiddleCenter, "Text attachment retained"); });

        ObjectId definitionId;
        using (var source = new Database(false, true))
        {
            source.ReadDwgFile(Path.Combine(Path.GetDirectoryName(Log), "WM_FORMAT.dwg"), FileOpenMode.OpenForReadAndAllShare, true, null);
            source.CloseInput(true);
            using (var tr = source.TransactionManager.StartTransaction())
                definitionId = ((BlockTable)tr.GetObject(source.BlockTableId, OpenMode.ForRead))["WM_FORMAT"];
            using (var mapping = new IdMapping())
            { source.WblockCloneObjects(new ObjectIdCollection(new[] { definitionId }), db.BlockTableId, mapping, DuplicateRecordCloning.Ignore, false); definitionId = mapping[definitionId].Value; }
        }
        ObjectId blockId;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var block = new BlockReference(point, definitionId); blockId = space.AppendEntity(block); tr.AddNewlyCreatedDBObject(block, true);
            Check(block.IsDynamicBlock, "Fixture is a dynamic block");
            var definition = (BlockTableRecord)tr.GetObject(definitionId, OpenMode.ForRead);
            bool first = true;
            foreach (ObjectId id in definition)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition attribute) || attribute.Constant) continue;
                var reference = new AttributeReference(); reference.SetAttributeFromBlock(attribute, block.BlockTransform);
                reference.TextString = first ? "П-007" : "Без номера"; first = false;
                block.AttributeCollection.AppendAttribute(reference); tr.AddNewlyCreatedDBObject(reference, true);
            }
            foreach (DynamicBlockReferenceProperty property in block.DynamicBlockReferencePropertyCollection)
            {
                var values = property.GetAllowedValues();
                if (!property.ReadOnly && values.Length > 1) { property.Value = values[1]; break; }
            }
            tr.Commit();
        }
        var target = NumberedCopyService.Read(db, blockId);
        var copy = NumberedCopyService.Copy(db, target, options, new[] { shift })[0];
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var source = (BlockReference)tr.GetObject(blockId, OpenMode.ForRead);
            var clone = (BlockReference)tr.GetObject(copy, OpenMode.ForRead);
            Check(clone.IsDynamicBlock, "Dynamic block stays dynamic");
            Check(clone.DynamicBlockTableRecord == source.DynamicBlockTableRecord, "Dynamic definition preserved");
            var properties = source.DynamicBlockReferencePropertyCollection.Cast<DynamicBlockReferenceProperty>().ToArray();
            var copied = clone.DynamicBlockReferencePropertyCollection.Cast<DynamicBlockReferenceProperty>().ToArray();
            Check(properties.Length > 0 && properties.Length == copied.Length, "Dynamic property set preserved");
            for (int i = 0; i < properties.Length; i++) Check(Equals(properties[i].Value, copied[i].Value), "Dynamic property value preserved: " + properties[i].PropertyName);
            Check(clone.AttributeCollection.Count == source.AttributeCollection.Count, "Dynamic attributes preserved");
            var text = (AttributeReference)tr.GetObject(clone.AttributeCollection[0], OpenMode.ForRead);
            Check(text.TextString.Contains("008"), "Chosen dynamic block attribute renumbered");
        }
    }

    [CommandMethod("NUMBERED_COPY_CHECK_PAPER")]
    public void Paper()
    {
        var db = AcadApp.DocumentManager.MdiActiveDocument.Database;
        ObjectId modelId = ObjectId.Null;
        try
        {
            modelId = Add(db, new DBText { TextString = "007", Height = 2.5, Position = Point3d.Origin });
            LayoutManager.Current.CreateLayout("NUMBERED_COPY_PAPER"); db.TileMode = false;
            LayoutManager.Current.CurrentLayout = "NUMBERED_COPY_PAPER";
            var paperId = Add(db, new MText { Contents = "В-007", Location = Point3d.Origin, TextHeight = 2.5 });
            var target = NumberedCopyService.Read(db, paperId);
            var copy = NumberedCopyService.Copy(db, target, new NumberedCopyOptions { Start = 7, Digits = 3 },
                new[] { Matrix3d.Displacement(new Vector3d(30, 40, 0)) })[0];
            Read<MText>(db, copy, t => Check(t.OwnerId == db.CurrentSpaceId && t.Text == "В-008", "Paper-space text copied into paper space"));
            Reject(() => NumberedCopyService.Read(db, modelId), "Reject source from a different space");
            File.AppendAllText(Log, "PAPER_PASS: paper-space copies and cross-space selection rejection.\n");
        }
        catch (System.Exception ex) { File.AppendAllText(Log, "NUMBERED_COPY_FAIL: " + ex); }
        finally
        {
            LayoutManager.Current.CurrentLayout = "Model"; db.TileMode = true;
            if (!modelId.IsNull) using (var tr = db.TransactionManager.StartTransaction()) { tr.GetObject(modelId, OpenMode.ForWrite).Erase(); tr.Commit(); }
        }
    }

    private void PathChecks(Database db, NumberedCopyTarget target)
    {
        var start = new Point3d(1000, 300, 20); var end = start + new Vector3d(100, 0, 0);
        var line = Add(db, new Line(start, end));
        var options = new NumberedCopyOptions { Mode = NumberedCopyMode.Curve, Distance = 30 };
        Matrix3d[] Path(ObjectId path, Point3d pick, Matrix3d? ucs = null) => NumberedCopyPath.Create(db, path, pick, target, options, ucs ?? Matrix3d.Identity);
        var placements = Path(line, start);
        Check(placements.Length == 4, "On-curve fixed step includes start");
        Near(target.Anchor.TransformBy(placements[3]), start + new Vector3d(90, 0, 0), "Fixed step end");
        placements = Path(line, end);
        Near(target.Anchor.TransformBy(placements[1]), end - new Vector3d(30, 0, 0), "Pick near endpoint reverses path");
        options.OnCurve = false;
        placements = Path(line, start);
        Check(placements.Length == 3, "Relative mode skips origin");
        Near(target.Anchor.TransformBy(placements[0]), target.Anchor + new Vector3d(30, 0, 0), "Relative mode preserves source offset");
        options.OnCurve = true; options.LimitCount = options.Divide = true; options.Count = 5;
        placements = Path(line, start);
        Check(placements.Length == 5, "Equal division uses number of copies");
        Near(target.Anchor.TransformBy(placements.Last()), end, "Open equal division includes endpoint");
        options.Count = 1;
        Near(target.Anchor.TransformBy(Path(line, start)[0]), start, "Single equal-division copy");
        options.Count = 5; options.Orientation = NumberedCopyOrientation.Tangent;
        placements = Path(line, end);
        Check(target.AxisX.TransformBy(placements[0]).IsEqualTo(-Vector3d.XAxis), "Orientation follows reversed tangent");
        options.Orientation = NumberedCopyOrientation.Left;
        Check(target.AxisX.TransformBy(Path(line, start)[0]).IsEqualTo(Vector3d.YAxis), "Left perpendicular orientation");
        options.Orientation = NumberedCopyOrientation.Right;
        Check(target.AxisX.TransformBy(Path(line, start)[0]).IsEqualTo(-Vector3d.YAxis), "Right perpendicular orientation");
        var ucs = Matrix3d.Rotation(Math.PI / 2, Vector3d.ZAxis, Point3d.Origin);
        options.OffsetX = 10; options.OffsetY = 20;
        Near(target.Anchor.TransformBy(Path(line, start, ucs)[0]), start + new Vector3d(-20, 10, 0), "Offsets use drawing UCS axes");
        options.OffsetX = options.OffsetY = 0; options.Orientation = NumberedCopyOrientation.Original;
        var circle = Add(db, new Circle(Point3d.Origin, Vector3d.ZAxis, 10));
        options.Count = 4;
        placements = Path(circle, new Point3d(10, 0, 0));
        Check(placements.Length == 4, "Closed division count");
        Near(target.Anchor.TransformBy(placements[1]), new Point3d(0, 10, 0), "Closed curve starts at pick point");
        Check(target.Anchor.TransformBy(placements.Last()).DistanceTo(target.Anchor.TransformBy(placements[0])) > 1, "No duplicate closure");
        options.LimitCount = options.Divide = false; options.Distance = Math.PI * 5;
        Check(Path(circle, new Point3d(10, 0, 0)).Length == 4, "Closed fixed-step seam is not duplicated");

        var polyline = new Polyline { Elevation = 30 };
        foreach (var p in new[] { new Point2d(0, 0), new Point2d(10, 0), new Point2d(10, 20), new Point2d(30, 20) })
            polyline.AddVertexAt(polyline.NumberOfVertices, p, 0, 0, 0);
        var polylineId = Add(db, polyline); options.Vertices = true;
        placements = Path(polylineId, new Point3d(0, 0, 30));
        Check(placements.Length == 4, "Polyline vertex count");
        Near(target.Anchor.TransformBy(placements[2]), new Point3d(10, 20, 30), "Elevated vertex position");
        options.OnCurve = false;
        Check(Path(polylineId, new Point3d(0, 0, 30)).Length == 3, "Relative vertex mode skips start");
        options.OnCurve = true;
        using (var tr = db.TransactionManager.StartTransaction()) { ((Polyline)tr.GetObject(polylineId, OpenMode.ForWrite)).Closed = true; tr.Commit(); }
        Near(target.Anchor.TransformBy(Path(polylineId, new Point3d(10, 20, 30))[0]), new Point3d(10, 20, 30), "Closed vertex mode starts at nearest vertex");
        Reject(() => Path(line, start), "Vertex mode rejects non-polyline");
        var polyline3d = Add(db, new Polyline3d(Poly3dType.SimplePoly, new Point3dCollection(new[] { Point3d.Origin, new Point3d(0, 0, 10), new Point3d(10, 0, 10) }), false));
        options.Orientation = NumberedCopyOrientation.Tangent;
        placements = Path(polyline3d, Point3d.Origin);
        Check(placements.Length == 3, "3D polyline vertex count");
        Check(target.AxisX.TransformBy(placements[0]).IsEqualTo(Vector3d.ZAxis), "Vertical tangent has a stable orientation basis");
        options.Vertices = false; options.Distance = 1e-10;
        Reject(() => Path(line, start), "Reject arrays exceeding 10000 copies");
        var zero = Add(db, new Line(Point3d.Origin, Point3d.Origin));
        Reject(() => Path(zero, Point3d.Origin), "Reject zero-length curve");
    }

    [CommandMethod("NUMBERED_COPY_SEED_MANUAL")]
    public void SeedManual()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        var ucs = Matrix3d.Displacement(new Vector3d(100000, -200000, 500)) * Matrix3d.Rotation(0.6, Vector3d.ZAxis, Point3d.Origin);
        doc.Editor.CurrentUserCoordinateSystem = ucs;
        _manualSource = Add(doc.Database, new DBText { Position = new Point3d(10, 20, 3).TransformBy(ucs), Height = 2.5, TextString = "П-007" });
    }
    [CommandMethod("NUMBERED_COPY_MANUAL")]
    public void Manual()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        NumberedCopyCommand.PlacePoints(doc.Editor, doc.Database, NumberedCopyService.Read(doc.Database, _manualSource),
            new NumberedCopyOptions { Start = 7, Digits = 3 });
    }
    [CommandMethod("NUMBERED_COPY_VERIFY_MANUAL", CommandFlags.NoUndoMarker)]
    public void VerifyManual()
    {
        try
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var texts = ((BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForRead))
                    .Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<DBText>().OrderBy(t => t.TextString).ToArray();
                Check(texts.Length == 3, "Manual copy count after Undo and Enter");
                Check(texts[0].TextString == "П-007" && texts[1].TextString == "П-008" && texts[2].TextString == "П-009", "Undo returns next number");
                Near(texts[1].Position, new Point3d(40, 20, 3).TransformBy(doc.Editor.CurrentUserCoordinateSystem), "Jig displacement in rotated UCS");
                Near(texts[2].Position, new Point3d(100, 20, 3).TransformBy(doc.Editor.CurrentUserCoordinateSystem), "Undo placement replaced in shifted/elevated UCS");
            }
            File.AppendAllText(Log, "MANUAL_PASS: native jig, Undo, Enter, rotated/shifted/elevated UCS.\n");
        }
        catch (System.Exception ex) { File.AppendAllText(Log, "NUMBERED_COPY_FAIL: " + ex); }
    }

    [CommandMethod("NUMBERED_COPY_SEED_CURVE")]
    public void SeedCurve()
    { Add(AcadApp.DocumentManager.MdiActiveDocument.Database, new Line(new Point3d(1000, 0, 0), new Point3d(1100, 0, 0))); }
    [CommandMethod("NUMBERED_COPY_CURVE")]
    public void Curve()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        NumberedCopyCommand.PlaceCurve(doc.Editor, doc.Database, NumberedCopyService.Read(doc.Database, _manualSource),
            new NumberedCopyOptions { Mode = NumberedCopyMode.Curve, Distance = 30, Start = 7, Digits = 3, Orientation = NumberedCopyOrientation.Tangent });
    }
    [CommandMethod("NUMBERED_COPY_VERIFY_CANCEL", CommandFlags.NoUndoMarker)]
    public void VerifyCancel() => VerifyCount(4, "CANCEL_PASS: path confirmation cancelled without copies.");
    [CommandMethod("NUMBERED_COPY_VERIFY_CURVE", CommandFlags.NoUndoMarker)]
    public void VerifyCurve()
    {
        try
        {
            var db = AcadApp.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var copies = ((BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead)).Cast<ObjectId>()
                    .Where(id => !id.IsErased).Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<DBText>()
                    .Where(t => Math.Abs(t.Position.Y) < 1e-6).OrderBy(t => t.Position.X).ToArray();
                Check(copies.Length == 4, "Confirmed curve array count");
                Near(copies[0].Position, new Point3d(1000, 0, 0), "On-curve array begins at endpoint");
                Near(copies[3].Position, new Point3d(1090, 0, 0), "On-curve array ends at last fitting step");
                Check(copies[0].TextString == "П-007" && copies[3].TextString == "П-010", "On-curve numbering starts with original value");
            }
            File.AppendAllText(Log, "CURVE_PASS: native selection and confirmation, 4 numbered copies.\n");
        }
        catch (System.Exception ex) { File.AppendAllText(Log, "NUMBERED_COPY_FAIL: " + ex); }
    }
    [CommandMethod("NUMBERED_COPY_VERIFY_UNDO", CommandFlags.NoUndoMarker)]
    public void VerifyUndo() => VerifyCount(4, "UNDO_PASS: standard Undo removes complete curve array.");
    private void VerifyCount(int count, string message)
    {
        try { Check(Count(AcadApp.DocumentManager.MdiActiveDocument.Database) == count, message); File.AppendAllText(Log, message + "\n"); }
        catch (System.Exception ex) { File.AppendAllText(Log, "NUMBERED_COPY_FAIL: " + ex); }
    }
}
