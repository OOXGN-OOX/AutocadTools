// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AutocadTools.Models;
using AutocadTools.Services;

internal static class TableSafetyChecks
{
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static void Run(Editor editor)
    {
        using var db = new Database(true, true);
        ObjectId id, fieldId;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var table = new Table { TableStyle = db.Tablestyle, Position = new Point3d(40, 50, 0) };
            table.SetSize(5, 3); table.SetRowHeight(12); table.SetColumnWidth(45);
            table.Cells[0, 0].TextString = "Заголовок";
            table.Cells[1, 1].TextString = "14"; table.Cells[2, 1].TextString = "6";
            table.Cells[2, 2].TextString = "old";
            table.Cells[2, 2].TextHeight = 3.2;
            table.Cells[2, 2].Alignment = CellAlignment.MiddleRight;
            table.Cells[2, 2].BackgroundColor = Autodesk.AutoCAD.Colors.Color.FromRgb(20, 70, 110);
            table.Cells[4, 0].TextString = "Сохранить стиль";
            table.Cells[3, 1].TextString = "0";
            table.Cells[3, 1].Contents[0].Formula = "=B2+B3";
            table.GenerateLayout(); id = model.AppendEntity(table); tr.AddNewlyCreatedDBObject(table, true);
            var field = new Field("%<\\AcVar Filename>%");
            fieldId = db.AddDBObject(field); tr.AddNewlyCreatedDBObject(field, true);
            table.Cells[1, 0].FieldId = fieldId;
            tr.Commit();
        }
        string before;
        using (var tr = db.TransactionManager.StartTransaction()) before = Snapshot((Table)tr.GetObject(id, OpenMode.ForRead), tr, 2, 2);
        var settings = new AutoNumSettings { Mode = AutoNumSettings.NumberingMode.Table, TableRow = 3, TableColumn = 3, StartNumber = 51 };
        Assert(new NumberingService().Number(db, editor, new[] { id }, settings) == 1, "Safe numbering failed");
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var table = (Table)tr.GetObject(id, OpenMode.ForRead);
            Assert(table.Cells[2, 2].TextString == "51", "Target text not changed");
            Assert(before == Snapshot(table, tr, 2, 2), "Numbering changed another cell, formula, field or formatting");
            Assert(table.Cells[1, 0].FieldId == fieldId, "Field identity changed");
        }
        foreach (var target in new[] { (2, 1), (4, 2) })
        {
            settings.TableRow = target.Item1; settings.TableColumn = target.Item2;
            try { new NumberingService().Number(db, editor, new[] { id }, settings); throw new Exception("Field/formula target was overwritten"); }
            catch (InvalidOperationException) { }
        }
        editor.WriteMessage("\nFEATURE_PASS: one-cell numbering preserves all other content, field IDs/codes, formulas, table/cell styles, widths/heights, borders and merges; field/formula targets rejected\n");
    }
    private static string Snapshot(Table table, Transaction tr, int targetRow, int targetColumn)
    {
        var text = new StringBuilder();
        text.Append(table.TableStyle).Append(table.Position).Append(table.LayerId).Append(table.Rows.Count).Append(table.Columns.Count);
        for (int row = 0; row < table.Rows.Count; row++)
        {
            text.Append(table.Rows[row].Height);
            for (int column = 0; column < table.Columns.Count; column++)
            {
                var cell = table.Cells[row, column];
                text.Append(table.Columns[column].Width).Append(cell.Style).Append(cell.Alignment).Append(cell.TextHeight).Append(cell.TextStyleId).Append(cell.BackgroundColor).Append(cell.IsMerged).Append(cell.State);
                foreach (var border in new[] { cell.Borders.Left, cell.Borders.Right, cell.Borders.Top, cell.Borders.Bottom })
                    text.Append(border.Color).Append(border.LineWeight).Append(border.IsVisible);
                if (row == targetRow && column == targetColumn) continue;
                text.Append(cell.TextString);
                foreach (CellContent content in cell.Contents)
                {
                    text.Append(content.FieldId).Append(content.HasFormula).Append(content.Formula).Append(content.TextString).Append(content.TextHeight).Append(content.TextStyleId).Append(content.ContentColor).Append(content.DataFormat);
                    if (!content.FieldId.IsNull) text.Append(((Field)tr.GetObject(content.FieldId, OpenMode.ForRead)).GetFieldCode(FieldCodeFlags.AddMarkers));
                }
            }
        }
        return text.ToString();
    }
    internal static void Index(Editor editor)
    {
        using var db = new Database(true, true);
        var items = new[] { new BatchPlotItem { SheetNumber = "05", Name = "План первого этажа" }, new BatchPlotItem { SheetNumber = "07-А", Name = "Схема" } };
        foreach (int scale in new[] { 1, 100 })
        {
            var id = PlotIndexService.Insert(db, items, new Point3d(50, 60, 0), scale);
            using var tr = db.TransactionManager.StartTransaction();
            var table = (Table)tr.GetObject(id, OpenMode.ForRead);
            Assert(table.OwnerId == SymbolUtilityServices.GetBlockModelSpaceId(db), "Index inserted outside model space");
            Assert(table.Rows.Count == 4 && table.Columns.Count == 2 && table.Cells[2, 0].TextString == "05" && table.Cells[3, 0].TextString == "07-А" && table.Cells[2, 1].TextString == items[0].Name, "Index values/order lost");
            Assert(Math.Abs(table.Columns[0].Width - 20 * scale) < 0.001 && table.Cells[2, 1].TextHeight == 2.5 * scale, "Index scale failed");
        }
        editor.WriteMessage("\nFEATURE_PASS: drawing index TABLE, model space, selected order, sheet numbers/names, scale 1 and 100\n");
    }
}
