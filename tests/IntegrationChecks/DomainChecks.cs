// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AutocadTools.Models;
using AutocadTools.Services;

internal static class DomainChecks
{
    internal static void Run(Document document)
    {
        RunCase(document.Editor, "selection filters", Selection);
        RunCase(document.Editor, "length and closed area", Length);
        RunCase(document.Editor, "numbering text/order/overflow rollback", () => Numbering(document.Editor));
        RunCase(document.Editor, "clean model and preserve layer locks", CleanUnselected);
        RunCase(document.Editor, "clean points and empty text", CleanDrawing);
        RunCase(document.Editor, "plan preparation", PreparePlan);
        RunCase(document.Editor, "customer copy filenames and source preservation", () => CustomerCopy(document));
        RunCase(document.Editor, "copy block with attributes", CopyBlock);
        RunCase(document.Editor, "transform validation", Transform);
        RunCase(document.Editor, "native table/XLSX round trip and unmerge", () => TableRoundTrip(document));

        RunCase(document.Editor, "print selection notifications", PrintSelection);
    }

    private static void RunCase(Editor editor, string name, Action test)
    {
        test();
        editor.WriteMessage("\nDOMAIN_PASS: " + name + "\n");
    }

    private static BlockTableRecord Model(Database db, Transaction transaction) =>
        (BlockTableRecord)transaction.GetObject(((BlockTable)transaction.GetObject(db.BlockTableId, OpenMode.ForRead))[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
    private static ObjectId Add(Transaction tr, BlockTableRecord space, Entity entity)
    {
        var id = space.AppendEntity(entity); tr.AddNewlyCreatedDBObject(entity, true); return id;
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static void Selection()
    {
        using var db = new Database(true, true);
        using var tr = db.TransactionManager.StartTransaction();
        var space = Model(db, tr);
        var a = new Line(Point3d.Origin, new Point3d(10, 0, 0)); Add(tr, space, a);
        var b = new Line(Point3d.Origin, new Point3d(10, 0, 0)); Add(tr, space, b);
        var c = new Line(Point3d.Origin, new Point3d(20, 0, 0)); Add(tr, space, c);
        var text = new DBText { TextString = "test", Position = Point3d.Origin }; Add(tr, space, text);
        var options = new SelectSimilarOptions { LineLength = true, AddSourceObjects = false };
        var matches = new SelectSimilarService(options).FindSimilar(tr, space, new() { a });
        Assert(matches.SequenceEqual(new[] { b.ObjectId }), "Length/type/source exclusion failed");
        options.AddSourceObjects = true;
        Assert(new SelectSimilarService(options).FindSimilar(tr, space, new() { a }).Count == 2, "Source inclusion failed");
        options.MatchType = false; options.LineLength = false;
        Assert(new SelectSimilarService(options).FindSimilar(tr, space, new() { a }).Count == 4, "Optional type filter became mandatory");
        Assert(new SelectSimilarService(options).FindSimilar(tr, space, new()).Count == 0, "Empty sources selected objects");
        var dimension = new AlignedDimension(Point3d.Origin, new Point3d(10, 0, 0), new Point3d(0, 2, 0), "", db.Dimstyle);
        Add(tr, space, dimension);
        var rotated = new AlignedDimension(Point3d.Origin, new Point3d(10, 0, 0), new Point3d(0, 2, 0), "", db.Dimstyle) { TextRotation = Math.PI / 4 };
        Add(tr, space, rotated);
        var dimensionOptions = new SelectSimilarOptions { DimTextRotation = true, DimValue = true, AddSourceObjects = false };
        Assert(new SelectSimilarService(dimensionOptions).FindSimilar(tr, space, new() { dimension }).Count == 0, "Dimension text rotation filter was ignored");
    }

    private static void Length()
    {
        using var db = new Database(true, true);
        using var tr = db.TransactionManager.StartTransaction();
        var space = Model(db, tr);
        var polygon = new Polyline();
        polygon.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0); polygon.AddVertexAt(1, new Point2d(100, 0), 0, 0, 0);
        polygon.AddVertexAt(2, new Point2d(100, 100), 0, 0, 0); polygon.AddVertexAt(3, new Point2d(0, 100), 0, 0, 0); polygon.Closed = true;
        var id = Add(tr, space, polygon);
        var circle = Add(tr, space, new Circle(Point3d.Origin, Vector3d.ZAxis, 10));
        var erased = new Line(Point3d.Origin, new Point3d(20, 0, 0)); var erasedId = Add(tr, space, erased); erased.Erase();
        var groups = new LengthService().CalculateLengths(tr, new[] { id, circle, ObjectId.Null, erasedId });
        Assert(groups.Sum(group => group.Count) == 2, "Invalid object handling failed");
        Assert(Math.Abs(groups.Sum(group => group.RawTotalArea) - 10000 - Math.PI * 100) < 0.001, "Closed area incorrect");
        Assert(Math.Abs(SelectionLengthService.Calculate(tr, new[] { id, circle }) - 400) < 0.001, "Status circle exclusion failed");
    }

    private static void Numbering(Editor editor)
    {
        using var db = new Database(true, true);
        ObjectId first, second;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var space = Model(db, tr);
            first = Add(tr, space, new DBText { TextString = "first", Position = new Point3d(20, 0, 0) });
            second = Add(tr, space, new MText { Contents = "second", Location = new Point3d(0, 0, 0) });
            tr.Commit();
        }
        var settings = new AutoNumSettings { Order = AutoNumSettings.NumberingOrder.Geometric,
            Sorting = AutoNumSettings.SortPreset.HorizontalOnly, Prefix = "P-", Digits = 2, StartNumber = 3, Step = 2 };
        Assert(new NumberingService().Number(db, editor, new[] { first, second }, settings) == 2, "Text numbering count failed");
        using (var tr = db.TransactionManager.StartTransaction())
        {
            Assert(((DBText)tr.GetObject(first, OpenMode.ForRead)).TextString == "P-05", "Geometric order failed");
            Assert(((MText)tr.GetObject(second, OpenMode.ForRead)).Contents == "P-03", "MText numbering failed");
        }
        settings.StartNumber = int.MaxValue; settings.Step = 1;
        try { new NumberingService().Number(db, editor, new[] { first, second }, settings); throw new InvalidOperationException("Overflow accepted"); }
        catch (OverflowException) { }
        using var check = db.TransactionManager.StartTransaction();
        Assert(((DBText)check.GetObject(first, OpenMode.ForRead)).TextString == "P-05", "Overflow did not roll back");
    }

    private static void CleanUnselected()
    {
        using var db = new Database(true, true);
        using var tr = db.TransactionManager.StartTransaction();
        var space = Model(db, tr);
        var keep = Add(tr, space, new Line(Point3d.Origin, new Point3d(10, 0, 0)));
        var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
        var layer = new LayerTableRecord { Name = "LOCK_TEST" }; var layerId = layers.Add(layer); tr.AddNewlyCreatedDBObject(layer, true);
        var remove = new Line(Point3d.Origin, new Point3d(20, 0, 0)) { LayerId = layerId }; Add(tr, space, remove);
        layer.IsLocked = true;
        Assert(new CleanUnselectedService(db, tr).Process(new HashSet<ObjectId> { keep }, false) == 1, "Model was skipped when paper layouts disabled");
        Assert(remove.IsErased && layer.IsLocked && !keep.IsErased, "Layer lock or retained selection changed");
    }

    private static void CleanDrawing()
    {
        using var db = new Database(true, true);
        using var tr = db.TransactionManager.StartTransaction();
        var space = Model(db, tr);
        var point = new DBPoint(Point3d.Origin); Add(tr, space, point);
        var blank = new DBText { TextString = " ", Position = Point3d.Origin }; Add(tr, space, blank);
        var keep = new DBText { TextString = "Preserve", Position = Point3d.Origin }; Add(tr, space, keep);
        var options = new CleanDwgOptions();
        foreach (var property in typeof(CleanDwgOptions).GetProperties().Where(p => p.PropertyType == typeof(bool))) property.SetValue(options, false);
        options.Points = options.BlankText = true;
        new CleanDwgService(options, db, tr).Process();
        Assert(point.IsErased && blank.IsErased && !keep.IsErased, "Cleaning categories changed unrelated objects");
    }

    private static void PreparePlan()
    {
        using var db = new Database(true, true);
        using var tr = db.TransactionManager.StartTransaction();
        var space = Model(db, tr);
        var point = new DBPoint(Point3d.Origin); var id = Add(tr, space, point);
        var options = new PlanPrepOptions();
        foreach (var property in typeof(PlanPrepOptions).GetProperties()) property.SetValue(options, false);
        options.DeletePoints = true;
        new PlanPrepService(options, db, tr).Process(new[] { id });
        Assert(point.IsErased, "Plan preparation did not remove selected point");
    }

    private static void CustomerCopy(Document document)
    {
        var directory = Path.GetDirectoryName(document.Name);
        var prefix = Path.GetFileNameWithoutExtension(document.Name) + "_" + DateTime.Now.ToString("yy.MM.dd");
        var before = Directory.GetFiles(directory, prefix + "*.dwg").ToHashSet();
        var sourceName = document.Name;
        var source = ReadShared(sourceName);
        var options = new CustomerOrderOptions { CreateNewFile = true };
        var service = new CustomerOrderService();
        service.Execute(document, options);
        service.Execute(document, options);
        var after = Directory.GetFiles(directory, prefix + "*.dwg").Where(path => !before.Contains(path)).ToArray();
        Assert(after.Length == 2 && ReadShared(sourceName).SequenceEqual(source) && document.Name == sourceName, "Customer copies overwrote a file or source");
    }

    private static byte[] ReadShared(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray();
    }

    private static void PrintSelection()
    {
        using var model = new AutocadTools.ViewModels.BatchPlotViewModel();
        var item = new BatchPlotItem { IsSelected = true };
        model.Items = new System.Collections.ObjectModel.ObservableCollection<BatchPlotItem> { item };
        Assert(model.IsAllSelected == true, "Select-all initial state failed");
        item.IsSelected = false;
        Assert(model.IsAllSelected == false, "Per-sheet toggle did not update select-all");
    }

    private static void CopyBlock()
    {
        using var db = new Database(true, true);
        var definition = FormatInsertService.EnsureDefinition(db);
        var id = FormatInsertService.Insert(db, definition, new Point3d(100, 200, 0));
        using var tr = db.TransactionManager.StartTransaction();
        var source = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
        var oldIds = Model(db, tr).Cast<ObjectId>().ToHashSet();
        var sourceAttribute = (AttributeReference)tr.GetObject(source.AttributeCollection[0], OpenMode.ForRead);
        var position = sourceAttribute.Position;
        var offset = new Vector3d(200, 500, 0);
        Assert(CopyService.Copy(tr, Model(db, tr), new[] { source }, offset) == 1, "Block copy count failed");
        var clonedId = Model(db, tr).Cast<ObjectId>().Single(candidate => !oldIds.Contains(candidate));
        var clone = (BlockReference)tr.GetObject(clonedId, OpenMode.ForRead);
        Assert(clone.AttributeCollection.Count == source.AttributeCollection.Count && clone.IsDynamicBlock, "Copy lost owned attributes or dynamic data");
        var attribute = (AttributeReference)tr.GetObject(clone.AttributeCollection[0], OpenMode.ForRead);
        Assert(attribute.Position.DistanceTo(position + offset) < 0.001, "Copied attribute displaced incorrectly");
        Assert(sourceAttribute.Position.DistanceTo(position) < 0.001, "Source was modified during copy");
    }

    private static void Transform()
    {
        var calculator = new TransformCalculator();
        var result = calculator.CalculateTransformation(Point3d.Origin, new Point3d(10, 0, 0), new Point3d(0, 10, 0),
            Point3d.Origin, new Point3d(10, 0, 0), new Point3d(5, 5, 0));
        Assert(double.IsFinite(result.ScaleX) && double.IsFinite(result.ScaleY), "Transform scale is not finite");
        try
        {
            calculator.CalculateTransformation(Point3d.Origin, new Point3d(10, 0, 0), new Point3d(0, 10, 0), Point3d.Origin, Point3d.Origin, Point3d.Origin);
            throw new InvalidOperationException("Degenerate target accepted");
        }
        catch (ArgumentException) { }
    }

    private static void TableRoundTrip(Document document)
    {
        ObjectId tableId;
        using (document.LockDocument())
        using (var tr = document.TransactionManager.StartTransaction())
        {
            var table = new Table { TableStyle = document.Database.Tablestyle };
            table.SetSize(3, 3); table.SetRowHeight(10); table.SetColumnWidth(40);
            table.Cells[0, 0].TextString = "Header";
            table.MergeCells(CellRange.Create(table, 0, 0, 0, 1));
            tableId = Add(tr, Model(document.Database, tr), table);
            var snapshot = TableExcelService.ExportSnapshot(table);
            Assert(snapshot.Merges.Count > 0 && snapshot.Values[0, 0] == "Header", "Native export snapshot lost merge/content");
            tr.Commit();
        }
        var data = new string[4, 4]; data[0, 0] = "A"; data[0, 1] = "B"; data[3, 3] = "D4";
        TableExcelService.ImportWorkbook(document, tableId, new TableWorkbookData(4, 4, data, new()));
        using var verify = document.TransactionManager.StartTransaction();
        var result = (Table)verify.GetObject(tableId, OpenMode.ForRead);
        Assert(result.Rows.Count == 4 && result.Columns.Count == 4 && result.Cells[3, 3].TextString == "D4", "Native import did not expand table");
        Assert(!result.Cells[0, 0].IsMerged.GetValueOrDefault(false) && result.Cells[0, 1].TextString == "B", "Excel unmerge did not reach AutoCAD");
    }
}
