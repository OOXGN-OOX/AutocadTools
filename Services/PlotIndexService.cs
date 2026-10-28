// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    internal static class PlotIndexService
    {
        internal static ObjectId Insert(Database database, IReadOnlyList<BatchPlotItem> items, Point3d point, int scale)
        {
            if (items == null || items.Count == 0) throw new ArgumentException("Выберите листы для таблицы.");
            if (items.Count > 10000) throw new ArgumentException("Для одной ведомости допустимо до 10000 листов.");
            if (scale != 1 && scale != 100) throw new ArgumentOutOfRangeException(nameof(scale));
            using var tr = database.TransactionManager.StartTransaction();
            var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(database), OpenMode.ForWrite);
            using var table = new Table { Position = point, TableStyle = database.Tablestyle };
            table.SetSize(items.Count + 2, 2);
            table.SuppressRegenerateTable(true);
            try
            {
                table.SetRowHeight(8 * scale); table.Columns[0].Width = 20 * scale; table.Columns[1].Width = 150 * scale;
                table.Cells[0, 0].TextString = "Ведомость чертежей";
                table.Cells[1, 0].TextString = "Лист"; table.Cells[1, 1].TextString = "Наименование";
                for (int i = 0; i < items.Count; i++)
                {
                    table.Cells[i + 2, 0].TextString = items[i].SheetNumber ?? "";
                    table.Cells[i + 2, 1].TextString = items[i].Name ?? "";
                }
                for (int row = 0; row < table.Rows.Count; row++)
                    for (int column = 0; column < 2; column++) table.Cells[row, column].TextHeight = 2.5 * scale;
                table.MergeCells(CellRange.Create(table, 0, 0, 0, 1));
            }
            finally { table.SuppressRegenerateTable(false); }
            table.GenerateLayout();
            var id = model.AppendEntity(table); tr.AddNewlyCreatedDBObject(table, true); tr.Commit();
            return id;
        }
    }
}
