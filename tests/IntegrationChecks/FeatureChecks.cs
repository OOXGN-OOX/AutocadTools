// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AutocadTools.Models;
using AutocadTools.Services;

internal static class FeatureChecks
{
    internal static void Run(Editor editor)
    {
        Tables(editor);
        Frames(editor);
        Excel(Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument);
        Calculator(editor);
        TableSafetyChecks.Run(editor);
        TableSafetyChecks.Index(editor);
    }
    private static void Calculator(Editor editor)
    {
        using var model = new AutocadTools.ViewModels.SumLengthViewModel();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(AutocadTools.ViewModels.SumLengthViewModel).GetField("_rawTotalLength", flags).SetValue(model, 12000d);
        typeof(AutocadTools.ViewModels.SumLengthViewModel).GetField("_rawTotalArea", flags).SetValue(model, 8000000d);
        model.SelectedLengthUnit = LengthUnit.Meters; model.SelectedAreaUnit = AreaUnit.SquareMeters;
        model.CalculatorExpression = "L * 1,5 + A / 2";
        Assert(double.Parse(model.CalculatorResult) == 22, "Calculator view model units failed");
        model.ScaleNumerator = 2; model.ScaleDenominator = 1;
        Assert(double.Parse(model.CalculatorResult) == 52, "Calculator did not react to scale or area square factor");
        model.CalculatorExpression = "1 / 0"; model.CalculateCommand.Execute(null);
        Assert(model.CalculatorResult == "—" && model.CalculatorError.Length > 0 && !model.CopyCalculatorCommand.CanExecute(null), "Calculator invalid expression remained copyable");
        model.CalculatorExpression = "0";
        Assert(model.CopyCalculatorCommand.CanExecute(null), "Zero calculator result could not be copied");
        editor.WriteMessage("\nFEATURE_PASS: calculator units, scale, area square, invalid expressions and zero\n");
    }
    private static void Excel(Autodesk.AutoCAD.ApplicationServices.Document document)
    {
        var values = new string[3, 3]; values[0, 0] = "Заголовок"; values[1, 1] = "12,50"; values[2, 2] = "Последняя ячейка";
        var data = new TableWorkbookData(3, 3, values, new() { new ExcelMerge(0, 0, 0, 1) });
        var id = TableExcelService.CreateTable(document, data, new Point3d(5000, 5000, 0));
        using (var tr = document.Database.TransactionManager.StartTransaction())
        {
            var table = (Table)tr.GetObject(id, OpenMode.ForRead);
            var snapshot = TableExcelService.ExportSnapshot(table);
            Assert(snapshot.Rows == 3 && snapshot.Columns == 3 && snapshot.Values[2, 2] == "Последняя ячейка", "New table XLSX data lost");
            Assert(snapshot.Merges.Count == 1 && snapshot.Values[1, 1] == "12,50", "New table merges/formatted strings lost");
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "botplus-transfer-" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                ExcelWorkbookService.Save(path, snapshot);
                var restored = ExcelWorkbookService.Read(path);
                Assert(restored.Values[2, 2] == "Последняя ячейка" && restored.Merges.Count == 1, "Native XLSX export failed");
            }
            finally { System.IO.File.Delete(path); }
        }
        var replacement = new string[2, 2]; replacement[0, 0] = "Новый текст"; replacement[0, 1] = "Разъединено";
        TableExcelService.ImportWorkbook(document, id, new TableWorkbookData(2, 2, replacement, new()));
        using (var tr = document.Database.TransactionManager.StartTransaction())
        {
            var table = (Table)tr.GetObject(id, OpenMode.ForWrite);
            Assert(table.Cells[0, 1].TextString == "Разъединено" && table.Cells[2, 2].TextString == "Последняя ячейка", "Manual replace changed cells outside imported region");
            table.Erase(); tr.Commit();
        }
        document.Editor.WriteMessage("\nFEATURE_PASS: XLSX export, create TABLE with merges, replace imported region, preserve other cells\n");
    }
    private static void Assert(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
    private static void Tables(Editor editor)
    {
        using var db = new Database(true, true);
        var ids = new ObjectId[3];
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var space = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            for (int i = 0; i < ids.Length; i++)
            {
                var table = new Table { Position = new Point3d(i * 100, 0, 0), TableStyle = db.Tablestyle };
                table.SetSize(4, 2); table.SetRowHeight(10); table.SetColumnWidth(30);
                table.Cells[2, 1].TextString = "old"; table.Cells[3, 1].TextString = "preserve";
                ids[i] = space.AppendEntity(table); tr.AddNewlyCreatedDBObject(table, true); table.GenerateLayout();
            }
            tr.Commit();
        }
        var settings = new AutoNumSettings { Mode = AutoNumSettings.NumberingMode.Table, TableRow = 3, TableColumn = 2,
            Prefix = "T-", StartNumber = 7, Step = 2, Digits = 2, Order = AutoNumSettings.NumberingOrder.Geometric, Sorting = AutoNumSettings.SortPreset.HorizontalOnly };
        Assert(new NumberingService().Number(db, editor, ids.Reverse().Concat(ids.Take(1)), settings) == 3, "Table numbering count/duplicate handling failed");
        using (var tr = db.TransactionManager.StartTransaction())
        {
            for (int i = 0; i < ids.Length; i++)
            {
                var table = (Table)tr.GetObject(ids[i], OpenMode.ForRead);
                Assert(table.Cells[2, 1].TextString == "T-" + (7 + 2 * i).ToString("D2"), "Table numbering sorting/value failed");
                Assert(table.Cells[3, 1].TextString == "preserve", "Unrelated table cell changed");
            }
        }
        settings.TableRow = 99;
        try { new NumberingService().Number(db, editor, ids, settings); throw new Exception("Missing table cell accepted"); }
        catch (InvalidOperationException) { }
        settings.TableRow = 3; settings.StartNumber = int.MaxValue;
        try { new NumberingService().Number(db, editor, ids, settings); throw new Exception("Table overflow accepted"); }
        catch (OverflowException) { }
        using (var tr = db.TransactionManager.StartTransaction())
            Assert(((Table)tr.GetObject(ids[0], OpenMode.ForRead)).Cells[2, 1].TextString == "T-07", "Table overflow left partial changes");
        settings.StartNumber = 20;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var last = (Table)tr.GetObject(ids[2], OpenMode.ForWrite);
            last.Cells[2, 1].State = CellStates.ContentLocked;
            tr.Commit();
        }
        try { new NumberingService().Number(db, editor, ids, settings); throw new Exception("Locked cell accepted"); }
        catch (InvalidOperationException) { }
        using (var tr = db.TransactionManager.StartTransaction())
            Assert(((Table)tr.GetObject(ids[0], OpenMode.ForRead)).Cells[2, 1].TextString == "T-07", "Locked table left partial changes");
        editor.WriteMessage("\nFEATURE_PASS: same cell across tables, sorting, prefix/step/digits, duplicates, invalid cell and rollback\n");
    }
    private static void Frames(Editor editor)
    {
        using var db = new Database(true, true);
        var builder = new DatabaseSummaryInfoBuilder(db.SummaryInfo);
        builder.CustomPropertyTable.Add("Компания", "Тестовая компания");
        db.SummaryInfo = builder.ToDatabaseSummaryInfo();
        var definition = FormatInsertService.EnsureDefinition(db);
        var id = FormatInsertService.Insert(db, definition, Point3d.Origin, 100);
        builder = new DatabaseSummaryInfoBuilder(db.SummaryInfo);
        Assert(builder.CustomPropertyTable["Компания"]?.ToString() == "Тестовая компания", "Existing project property overwritten");
        foreach (var key in StampSchema.Keys)
            Assert(builder.CustomPropertyTable.Contains(key) && !string.IsNullOrEmpty(builder.CustomPropertyTable[key]?.ToString()), "Missing/empty stamp property " + key);
        Assert(builder.CustomPropertyTable["Фам.1"]?.ToString() == " ", "New stamp property is not a space");
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var block = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
            Assert(block.ScaleFactors.X == 100 && block.ScaleFactors.Y == 100, "Frame insertion scale failed");
            int fields = 0;
            foreach (ObjectId attributeId in block.AttributeCollection)
            {
                var attribute = (AttributeReference)tr.GetObject(attributeId, OpenMode.ForRead);
                if (!attribute.HasFields) continue;
                fields++;
                var field = (Field)tr.GetObject(attribute.GetField(), OpenMode.ForRead);
                Assert(!field.GetFieldCode(FieldCodeFlags.AddMarkers).Contains("?BlockRefId"), "Dynamic block placeholder was not rebound");
                Assert(field.EvaluationStatus.Status == FieldEvaluationStatus.Success, "Frame field evaluation failed: " + attribute.Tag);
                editor.WriteMessage("\nFIELD_RESULT: " + attribute.Tag + " = " + field.Value + "; " + field.EvaluationStatus.Status);
                if (attribute.Tag == "КОМПАНИЯ") Assert(field.Value?.ToString() == "Тестовая компания", "Project field was not evaluated at insertion");
                if (attribute.Tag == "ФИО1") Assert(string.IsNullOrWhiteSpace(field.Value?.ToString()), "Blank field displayed a dash/error");
            }
            Assert(fields >= 46, "Frame lost live field definitions");
        }
        builder = new DatabaseSummaryInfoBuilder(db.SummaryInfo);
        builder.CustomPropertyTable["Компания"] = "Обновлённая компания";
        db.SummaryInfo = builder.ToDatabaseSummaryInfo();
        db.EvaluateFields(FieldEvaluationContext.Demand);
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var block = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
            var company = block.AttributeCollection.Cast<ObjectId>().Select(a => (AttributeReference)tr.GetObject(a, OpenMode.ForRead)).Single(a => a.Tag == "КОМПАНИЯ");
            Assert(((Field)tr.GetObject(company.GetField(), OpenMode.ForRead)).Value?.ToString() == "Обновлённая компания", "Frame field became static text");
        }
        editor.WriteMessage("\nFEATURE_PASS: frame scale 100, project fields initialized, existing data preserved, live fields evaluated, blank spaces\n");
    }
}
