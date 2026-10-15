// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Linq;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Commands;
using AutocadTools.Services;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

public class Checks
{
    private const double Tolerance = 1e-6;
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "AutocadTools-AlignSkewChecks", "results.log");

    [CommandMethod("ALIGN_SKEW_CHECKS")]
    public void Run()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
            File.WriteAllText(LogPath, "Starting AutoCAD geometry checks\n");
            int count = 0;
            foreach (var basePoint in new[] { Point3d.Origin, new Point3d(12345, -9876, 0), new Point3d(1000000, 2000000, 200) })
            foreach (double degrees in new[] { 0.0, 35.0, 90.0, 180.0, 270.0, -55.0 })
            {
                foreach (var mode in new[] { TextHorizontalMode.TextLeft, TextHorizontalMode.TextCenter, TextHorizontalMode.TextRight, TextHorizontalMode.TextFit, TextHorizontalMode.TextAlign })
                {
                    File.AppendAllText(LogPath, $"Case {count}: TEXT {basePoint} {degrees} {mode}\n");
                    CheckText(doc.Database, basePoint, degrees * Math.PI / 180, mode);
                    count++;
                }
                foreach (var attachment in new[] { AttachmentPoint.TopLeft, AttachmentPoint.MiddleCenter, AttachmentPoint.BottomRight })
                {
                    File.AppendAllText(LogPath, $"Case {count}: MTEXT {basePoint} {degrees} {attachment}\n");
                    CheckMText(doc.Database, basePoint, degrees * Math.PI / 180, attachment);
                    count++;
                }
            }
            CheckTextOnly(doc.Database);
            CheckAttribute(doc.Database);
            File.AppendAllText(LogPath, $"ALIGN_SKEW_PASS: {count} TEXT/MTEXT cases; text-only; moved block attribute.\n");
            doc.Editor.WriteMessage($"\nALIGN_SKEW_PASS: {count} TEXT/MTEXT cases; text-only; moved block attribute.\n");
        }
        catch (System.Exception ex)
        {
            File.AppendAllText(LogPath, "ALIGN_SKEW_FAIL: " + ex + "\n");
            doc.Editor.WriteMessage("\nALIGN_SKEW_FAIL: " + ex + "\n");
        }
    }

    private static TransformCalculator.TransformationParams Parameters(Point3d b, out Point3d source, out Point3d target)
    {
        // Matches the swapped three point pairs used by ATISO.
        source = b + new Vector3d(0, 10, 0);
        target = b + new Vector3d(10 / Math.Sqrt(2), 10 / Math.Sqrt(2), 0);
        return new TransformCalculator().CalculateTransformation(source, b + new Vector3d(-20, 0, 0), b,
            target, b + new Vector3d(-20, 0, 0), b);
    }

    private static ObjectId Add(Database db, Entity entity)
    {
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var model = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            ObjectId id = model.AppendEntity(entity);
            tr.AddNewlyCreatedDBObject(entity, true);
            tr.Commit();
            return id;
        }
    }

    private static void Transform(Database db, Point3d b, params ObjectId[] ids)
    {
        var parameters = Parameters(b, out var source, out var target);
        using (var selection = SelectionSet.FromObjectIds(ids))
        {
            typeof(AlignSkewCommand).GetMethod("ApplyTransformation", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(new AlignSkewCommand(), new object[] { db, selection, source, target, parameters });
        }
    }

    private static Line LastLine(Database db, Transaction tr)
    {
        var model = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
        return model.Cast<ObjectId>().Where(id => !id.IsErased)
            .Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Line>().Last();
    }

    private static void Near(Point3d actual, Point3d expected, string message)
    {
        if (actual.DistanceTo(expected) > Tolerance)
            throw new InvalidOperationException(message + ": " + actual + " != " + expected);
    }

    private static void Near(double actual, double expected, string message)
    {
        if (Math.Abs(actual - expected) > Tolerance)
            throw new InvalidOperationException(message + ": " + actual + " != " + expected);
    }

    private static void Direction(Vector3d actual, Vector3d expected)
    {
        if ((actual.GetNormal() - expected.GetNormal()).Length > Tolerance)
            throw new InvalidOperationException("Baseline direction changed incorrectly");
    }

    private static void CheckText(Database db, Point3d b, double angle, TextHorizontalMode mode)
    {
        var anchor = b + new Vector3d(500, 900, 0);
        var baseline = new Vector3d(Math.Cos(angle), Math.Sin(angle), 0);
        var text = new DBText();
        text.SetDatabaseDefaults(db);
        text.TextString = "Тест";
        text.Height = 12;
        text.WidthFactor = 0.8;
        text.Oblique = 0.15;
        text.Rotation = angle;
        text.Position = anchor;
        text.HorizontalMode = mode;
        if (mode != TextHorizontalMode.TextLeft) text.AlignmentPoint = anchor;
        if (mode == TextHorizontalMode.TextFit || mode == TextHorizontalMode.TextAlign)
            text.Position = anchor - baseline * 100;
        var id = Add(db, text);
        double height, width;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var original = (DBText)tr.GetObject(id, OpenMode.ForRead);
            height = original.Height;
            width = original.WidthFactor;
        }
        var lineId = Add(db, new Line(anchor, anchor + baseline * 100));
        Transform(db, b, lineId, id);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var result = (DBText)tr.GetObject(id, OpenMode.ForRead);
            var line = LastLine(db, tr);
            Near(result.IsDefaultAlignment ? result.Position : result.AlignmentPoint, line.StartPoint, "TEXT anchor");
            Direction(new Vector3d(Math.Cos(result.Rotation), Math.Sin(result.Rotation), 0), line.EndPoint - line.StartPoint);
            Near(result.Height, height, "TEXT height");
            Near(result.WidthFactor, width, "TEXT width");
            Near(result.Oblique, 0.15, "TEXT oblique");
            if (result.TextString != "Тест" || result.HorizontalMode != mode) throw new InvalidOperationException("TEXT properties lost");
        }
    }

    private static void CheckMText(Database db, Point3d b, double angle, AttachmentPoint attachment)
    {
        var anchor = b + new Vector3d(-300, 700, 0);
        var baseline = new Vector3d(Math.Cos(angle), Math.Sin(angle), 0);
        var text = new MText();
        text.SetDatabaseDefaults(db);
        text.Contents = "{\\C1;Строка}\\PВторая строка";
        text.TextHeight = 10;
        text.Width = 150;
        text.Attachment = attachment;
        text.Direction = baseline;
        text.Location = anchor;
        string contents = text.Contents;
        var id = Add(db, text);
        var lineId = Add(db, new Line(anchor, anchor + baseline * 100));
        Transform(db, b, lineId, id);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var result = (MText)tr.GetObject(id, OpenMode.ForRead);
            var line = LastLine(db, tr);
            Near(result.Location, line.StartPoint, "MTEXT anchor");
            Direction(result.Direction, line.EndPoint - line.StartPoint);
            Near(result.TextHeight, 10, "MTEXT height");
            Near(result.Width, 150, "MTEXT width");
            if (result.Contents != contents || result.Attachment != attachment) throw new InvalidOperationException("MTEXT formatting lost");
        }
    }

    private static void CheckTextOnly(Database db)
    {
        var anchor = new Point3d(1000, 2500, 0);
        var text = new DBText();
        text.SetDatabaseDefaults(db);
        text.Position = anchor;
        text.Height = 10;
        text.TextString = "Only text";
        var id = Add(db, text);
        var parameters = Parameters(Point3d.Origin, out var source, out var target);
        var expected = anchor.TransformBy(AlignSkewTransform.CreateMatrix(source, target, parameters));
        Transform(db, Point3d.Origin, id);
        using (var tr = db.TransactionManager.StartTransaction())
            Near(((DBText)tr.GetObject(id, OpenMode.ForRead)).Position, expected, "Text-only anchor");
    }

    private static void CheckAttribute(Database db)
    {
        ObjectId blockId;
        var anchor = new Point3d(3000, 4000, 0);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
            var definition = new BlockTableRecord { Name = "AxoAttribute_" + Guid.NewGuid().ToString("N") };
            table.Add(definition);
            tr.AddNewlyCreatedDBObject(definition, true);
            var attribute = new AttributeDefinition(new Point3d(2, 3, 0), "Default", "TAG", "Prompt", db.Textstyle);
            attribute.Height = 10;
            definition.AppendEntity(attribute);
            tr.AddNewlyCreatedDBObject(attribute, true);
            var reference = new BlockReference(new Point3d(200, 500, 0), definition.ObjectId);
            var model = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            blockId = model.AppendEntity(reference);
            tr.AddNewlyCreatedDBObject(reference, true);
            var value = new AttributeReference();
            value.SetAttributeFromBlock(attribute, reference.BlockTransform);
            value.TextString = "Moved attribute";
            value.Position = anchor;
            value.Oblique = 0.2;
            value.WidthFactor = 0.7;
            value.IsMirroredInX = true;
            reference.AttributeCollection.AppendAttribute(value);
            tr.AddNewlyCreatedDBObject(value, true);
            tr.Commit();
        }
        var lineId = Add(db, new Line(anchor, anchor + Vector3d.XAxis * 100));
        Transform(db, Point3d.Origin, lineId, blockId);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var model = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            var result = model.Cast<ObjectId>().Where(id => !id.IsErased).Select(id => tr.GetObject(id, OpenMode.ForRead))
                .OfType<DBText>().Single(text => text.TextString == "Moved attribute");
            Near(result.Position, LastLine(db, tr).StartPoint, "Block attribute anchor");
            Near(result.Oblique, 0.2, "Block attribute oblique");
            Near(result.WidthFactor, 0.7, "Block attribute width");
            if (!result.IsMirroredInX) throw new InvalidOperationException("Block attribute mirror lost");
        }
    }
}
