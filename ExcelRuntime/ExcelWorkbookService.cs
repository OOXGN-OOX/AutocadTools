// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace AutocadTools.Services
{
    internal readonly record struct ExcelMerge(int TopRow, int LeftCol, int BottomRow, int RightCol);
    internal sealed record TableWorkbookData(int Rows, int Columns, string[,] Values, List<ExcelMerge> Merges);

    /// <summary>XLSX boundary; no AutoCAD objects cross into file processing.</summary>
    internal static class ExcelWorkbookService
    {
        private const long MaximumCells = 2_000_000;

        public static void Save(string path, TableWorkbookData data)
        {
            ValidateSize(data.Rows, data.Columns);
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Table");
            var metadata = workbook.Worksheets.Add("_meta");
            metadata.Cell(1, 1).SetValue(data.Rows);
            metadata.Cell(1, 2).SetValue(data.Columns);
            metadata.Hide();
            for (int row = 0; row < data.Rows; row++)
                for (int column = 0; column < data.Columns; column++)
                    sheet.Cell(row + 1, column + 1).SetValue(data.Values[row, column] ?? "");
            foreach (var merge in data.Merges)
                sheet.Range(merge.TopRow + 1, merge.LeftCol + 1, merge.BottomRow + 1, merge.RightCol + 1).Merge();
            sheet.SheetView.FreezeRows(1);
            workbook.SaveAs(path);
        }

        public static TableWorkbookData Read(string path, bool allowFirstWorksheet = false)
        {
            using var workbook = new XLWorkbook(path);
            if (!workbook.TryGetWorksheet("Table", out var sheet))
            {
                if (allowFirstWorksheet) sheet = workbook.Worksheets.FirstOrDefault(s => s.Visibility == XLWorksheetVisibility.Visible && s.Name != "_meta");
                if (sheet == null) throw new InvalidDataException("В книге отсутствует лист Table или доступный лист для импорта.");
            }
            int rows = 0, columns = 0;
            if (workbook.TryGetWorksheet("_meta", out var metadata))
            {
                metadata.Cell(1, 1).TryGetValue(out rows);
                metadata.Cell(1, 2).TryGetValue(out columns);
            }
            // Absolute last cell, not RangeUsed.RowCount: a lone value in C5 is 5x3.
            var used = sheet.RangeUsed(XLCellsUsedOptions.Contents);
            rows = Math.Max(rows, used?.RangeAddress.LastAddress.RowNumber ?? 0);
            columns = Math.Max(columns, used?.RangeAddress.LastAddress.ColumnNumber ?? 0);
            var merges = new List<ExcelMerge>();
            foreach (var range in sheet.MergedRanges)
            {
                var first = range.RangeAddress.FirstAddress;
                var last = range.RangeAddress.LastAddress;
                rows = Math.Max(rows, last.RowNumber);
                columns = Math.Max(columns, last.ColumnNumber);
                merges.Add(new ExcelMerge(first.RowNumber - 1, first.ColumnNumber - 1, last.RowNumber - 1, last.ColumnNumber - 1));
            }
            ValidateSize(rows, columns);
            var values = new string[rows, columns];
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                    values[row, column] = sheet.Cell(row + 1, column + 1).GetFormattedString();
            return new TableWorkbookData(rows, columns, values, merges);
        }

        private static void ValidateSize(int rows, int columns)
        {
            if (rows < 0 || columns < 0 || (long)rows * columns > MaximumCells || rows > 1_048_576 || columns > 16_384)
                throw new InvalidDataException("Таблица превышает допустимый размер импорта: 2 000 000 ячеек.");
        }
    }
}
