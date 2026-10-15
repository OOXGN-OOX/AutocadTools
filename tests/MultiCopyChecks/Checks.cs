// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Services;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

public class Checks
{
    private static readonly string Log = Path.Combine(Path.GetTempPath(), "AutocadTools-MultiCopyChecks", "results.log");
    private static ObjectId _seedId;
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Near(Vector3d actual, Vector3d expected) { Assert((actual - expected).Length < 1e-6, "Wrong displacement"); }

    [CommandMethod("MULTICOPY_CHECKS")]
    public void Run()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Log));
        File.WriteAllText(Log, "Starting MultiCopy checks\n");
        try
        {
            var start = new Point3d(100000, -200000, 500);
            var lineOffsets = MultiCopyService.AlongLine(start, start + new Vector3d(30, 40, 0), 10, 3, false);
            Assert(lineOffsets.Length == 5, "Line spacing count");
            Near(lineOffsets.Last(), new Vector3d(30, 40, 0));
            var divide = MultiCopyService.AlongLine(start, start + new Vector3d(90, 0, 0), 10, 3, true);
            Near(divide[0], new Vector3d(30, 0, 0)); Near(divide[2], new Vector3d(90, 0, 0));
            using (var line = new Line(new Point3d(200, 0, 0), new Point3d(290, 0, 0)))
            {
                Near(MultiCopyService.AlongCurve(line, line.StartPoint, 10, 3, true)[2], new Vector3d(90, 0, 0));
                Near(MultiCopyService.AlongCurve(line, line.EndPoint, 10, 3, true)[2], new Vector3d(-90, 0, 0));
            }
            using (var polyline = new Polyline())
            {
                polyline.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                polyline.AddVertexAt(1, new Point2d(30, 0), 0, 0, 0);
                polyline.AddVertexAt(2, new Point2d(30, 60), 0, 0, 0);
                var offsets = MultiCopyService.AlongCurve(polyline, polyline.StartPoint, 10, 3, true);
                Near(offsets[0], new Vector3d(30, 0, 0)); Near(offsets[1], new Vector3d(30, 30, 0)); Near(offsets[2], new Vector3d(30, 60, 0));
                polyline.Closed = true;
                Assert(MultiCopyService.AlongCurve(polyline, polyline.StartPoint, 10, 3, true).Length == 2, "Closed duplicate");
            }
            using (var curved = new Polyline())
            {
                curved.AddVertexAt(0, new Point2d(-10, 0), 1, 0, 0);
                curved.AddVertexAt(1, new Point2d(10, 0), 0, 0, 0);
                Near(MultiCopyService.AlongCurve(curved, curved.StartPoint, 10, 2, true)[0], new Vector3d(10, -10, 0));
            }
            using (var spline = new Spline(new Point3dCollection(new[] { Point3d.Origin, new Point3d(30, 30, 0), new Point3d(60, -20, 0), new Point3d(100, 0, 0) }), 3, 0.0))
            {
                var offsets = MultiCopyService.AlongCurve(spline, spline.StartPoint, 10, 3, true);
                Assert(offsets.Length == 3, "Spline count");
                Near(offsets[2], spline.EndPoint - spline.StartPoint);
                double length = spline.GetDistanceAtParameter(spline.EndParam);
                Near(offsets[0], spline.GetPointAtDist(length / 3) - spline.StartPoint);
            }
            CheckCloning();
            File.AppendAllText(Log, "Service checks passed: line, divide, reverse, polyline, arc segment, closed path, spline, groups and attributes.\n");
        }
        catch (System.Exception ex) { File.AppendAllText(Log, "MULTICOPY_FAIL: " + ex + "\n"); }
    }

    private static void CheckCloning()
    {
        using (var db = new Database(true, true))
        {
            ObjectId circleId, blockId, groupId;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, 2);
                circleId = space.AppendEntity(circle); tr.AddNewlyCreatedDBObject(circle, true);
                var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                var definition = new BlockTableRecord { Name = "TestBlock" };
                table.Add(definition); tr.AddNewlyCreatedDBObject(definition, true);
                var att = new AttributeDefinition(Point3d.Origin, "Value", "TAG", "Prompt", db.Textstyle) { Height = 2 };
                definition.AppendEntity(att); tr.AddNewlyCreatedDBObject(att, true);
                var block = new BlockReference(new Point3d(10, 0, 0), definition.ObjectId);
                blockId = space.AppendEntity(block); tr.AddNewlyCreatedDBObject(block, true);
                var reference = new AttributeReference(); reference.SetAttributeFromBlock(att, block.BlockTransform); reference.TextString = "Копия";
                block.AttributeCollection.AppendAttribute(reference); tr.AddNewlyCreatedDBObject(reference, true);
                var dictionary = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
                var group = new Group("Original", true);
                groupId = dictionary.SetAt("Original", group); tr.AddNewlyCreatedDBObject(group, true);
                group.Append(new ObjectIdCollection(new[] { circleId, blockId }));
                tr.Commit();
            }
            Assert(MultiCopyService.Copy(db, new[] { circleId, blockId }, new[] { new Vector3d(0, 100, 0), new Vector3d(0, 200, 0) }) == 4, "Clone count");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                var blocks = space.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<BlockReference>().OrderBy(b => b.Position.Y).ToArray();
                Assert(blocks.Length == 3, "Block count");
                for (int i = 0; i < blocks.Length; i++)
                {
                    var block = blocks[i]; Assert(block.AttributeCollection.Count == 1, "Lost attribute");
                    var attribute = (AttributeReference)tr.GetObject(block.AttributeCollection[0], OpenMode.ForRead);
                    Assert(attribute.TextString == "Копия", "Lost attribute value");
                    Near(attribute.Position - Point3d.Origin, new Vector3d(10, i * 100, 0));
                }
                var dictionary = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
                Assert(dictionary.Count == 3, "Groups duplicated incorrectly");
                var original = (Group)tr.GetObject(groupId, OpenMode.ForRead);
                Assert(original.GetAllEntityIds().SequenceEqual(new[] { circleId, blockId }), "Original group modified");
                foreach (DBDictionaryEntry entry in dictionary)
                    Assert(((Group)tr.GetObject(entry.Value, OpenMode.ForRead)).GetAllEntityIds().Length == 2, "Copy group members");
            }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                ((LayerTableRecord)tr.GetObject(layers["0"], OpenMode.ForWrite)).IsLocked = true;
                tr.Commit();
            }
            bool rejected = false;
            try { MultiCopyService.Copy(db, new[] { circleId }, new[] { Vector3d.XAxis }); }
            catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, "Locked source accepted");
        }
    }

    [CommandMethod("MULTICOPY_SEED")]
    public void Seed()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        using (var tr = doc.Database.TransactionManager.StartTransaction())
        {
            var space = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);
            var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, 2);
            var id = space.AppendEntity(circle); tr.AddNewlyCreatedDBObject(circle, true);
            _seedId = id;
            var line = new Line(new Point3d(200, 0, 0), new Point3d(290, 0, 0));
            space.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
            tr.Commit();
            doc.Editor.Command("_ZOOM", "_Extents");
            doc.Editor.SetImpliedSelection(new[] { id });
        }
    }

    [CommandMethod("MULTICOPY_VERIFY_COMMAND")]
    public void VerifyCommand()
    {
        try
        {
            var db = AcadApp.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                var circles = space.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Circle>().OrderBy(c => c.Center.X).ToArray();
                Assert(circles.Length == 4, "Actual MULTICOPY command count");
                for (int i = 0; i < circles.Length; i++) Near(circles[i].Center - Point3d.Origin, new Vector3d(i * 30, 0, 0));
            }
            File.AppendAllText(Log, "Scripted Divide/Primitive workflow passed.\n");
        }
        catch (System.Exception ex) { File.AppendAllText(Log, "MULTICOPY_FAIL: " + ex + "\n"); }
    }

    [CommandMethod("MULTICOPY_PRESELECT")]
    public void Preselect() => AcadApp.DocumentManager.MdiActiveDocument.Editor.SetImpliedSelection(new[] { _seedId });

    [CommandMethod("MULTICOPY_VERIFY_POINTS")]
    public void VerifyPoints()
    {
        try
        {
            var db = AcadApp.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                var circles = space.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Circle>().OrderBy(c => c.Center.X).ToArray();
                Assert(circles.Length == 7, "Actual measure command count");
                var expected = new[] { 0, 30, 30, 60, 60, 90, 90 };
                for (int i = 0; i < circles.Length; i++) Near(circles[i].Center - Point3d.Origin, new Vector3d(expected[i], 0, 0));
            }
            File.AppendAllText(Log, "MULTICOPY_PASS: services; scripted Divide/Primitive and Measure/two-point workflows.\n");
        }
        catch (System.Exception ex) { File.AppendAllText(Log, "MULTICOPY_FAIL: " + ex + "\n"); }
    }

    private static Matrix3d _testUcs;
    private static Point3d _testBase;
    [CommandMethod("MULTICOPY_SEED_UCS")]
    public void SeedUcs()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        _testUcs = Matrix3d.Displacement(new Vector3d(100000, -200000, 500)) *
            Matrix3d.Rotation(Math.PI / 2, Vector3d.ZAxis, Point3d.Origin);
        doc.Editor.CurrentUserCoordinateSystem = _testUcs;
        AcadApp.SetSystemVariable("DYNMODE", 0);
        AcadApp.SetSystemVariable("OSMODE", 0);
        AcadApp.SetSystemVariable("ORTHOMODE", 0);
        _testBase = new Point3d(10, 20, 3).TransformBy(_testUcs);
        using (var tr = doc.Database.TransactionManager.StartTransaction())
        {
            var space = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);
            var circle = new Circle(_testBase, Vector3d.ZAxis, 2);
            _seedId = space.AppendEntity(circle); tr.AddNewlyCreatedDBObject(circle, true);
            tr.Commit();
        }
        doc.Editor.SetImpliedSelection(new[] { _seedId });
    }
    [CommandMethod("MULTICOPY_VERIFY_UCS")]
    public void VerifyUcs()
    {
        try
        {
            var db = AcadApp.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                var circles = space.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Circle>()
                    .Where(c => c.Center.DistanceTo(_testBase) < 1000).OrderBy(c => c.Center.Y).ToArray();
                Assert(circles.Length == 4, "Shifted/rotated UCS produced wrong copy count");
                for (int i = 0; i < 4; i++)
                    Near(circles[i].Center - _testBase, new Vector3d(i * 30, 0, 0).TransformBy(_testUcs));
            }
            File.AppendAllText(Log, "Shifted/rotated UCS command and elevation passed.\n");
        }
        catch (System.Exception ex) { File.AppendAllText(Log, "MULTICOPY_FAIL: " + ex + "\n"); }
    }
}
