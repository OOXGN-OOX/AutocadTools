// Copyright (c) 2026 OOXGN (GitHub: OOXGN-OOX). All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

using ClosedXML.Excel;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using AcadCoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutocadTools.Services
{
    public sealed class TableExcelService
    {
        private static readonly Dictionary<Document, PendingImport> _pending = new();
        private static bool _idleHooked;
        private static bool _docDestroyHooked;

        public void ExportOpenAndAutoImport(Document doc, ObjectId tableId)
        {
            if (doc == null) return;
            var ed = doc.Editor;

            using (doc.LockDocument())
            using (var tr = doc.TransactionManager.StartTransaction())
            {
                var table = tr.GetObject(tableId, OpenMode.ForRead, false) as Table;
                if (table == null)
                    throw new InvalidOperationException("Выбранный объект не является Table.");

                var session = ExportTableToXlsx(doc, table);

                tr.Commit();

                StartPending(doc, session);

                ed.WriteMessage($"\n[Autocad Tools TableExcel] Excel-файл создан: {session.FilePath}\n");
                ed.WriteMessage("\n[Autocad Tools TableExcel] Откройте/отредактируйте файл в Excel, затем ЗАКРОЙТЕ Excel — импорт выполнится автоматически.\n");

                Process.Start(new ProcessStartInfo
                {
                    FileName = session.FilePath,
                    UseShellExecute = true
                });
            }
        }

        // =========================================================================
        // EXPORT
        // =========================================================================
        private static PendingImport ExportTableToXlsx(Document doc, Table table)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "Autocad Tools", "TableExcel");
            Directory.CreateDirectory(tempDir);

            var safeName = MakeSafeFileName(Path.GetFileNameWithoutExtension(doc.Name));
            var stamp = DateTime.Now.ToString("yy.MM.dd_HH.mm.ss");
            var filePath = Path.Combine(tempDir, $"{safeName}_table_{table.Handle}_{stamp}.xlsx");

            int rows = table.Rows.Count;
            int cols = table.Columns.Count;

            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Table");

                // meta: чтобы при пустых значениях RangeUsed() не дал 0x0
                var meta = wb.Worksheets.Add("_meta");
                meta.Cell(1, 1).SetValue(rows);
                meta.Cell(1, 2).SetValue(cols);
                meta.Hide();

                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        string text = GetAcadCellText(table, r, c);
                        ws.Cell(r + 1, c + 1).SetValue(text ?? "");
                    }
                }

                // Экспорт объединённых ячеек из AutoCAD в Excel
                ApplyAcadMergesToExcel(table, ws, rows, cols);

                ws.SheetView.FreezeRows(1);
                wb.SaveAs(filePath);
            }

            return new PendingImport
            {
                TableId = table.ObjectId,
                FilePath = filePath,
                CreatedUtc = DateTime.UtcNow,
                SeenLocked = false
            };
        }

        private static string GetAcadCellText(Table table, int row, int col)
        {
            try
            {
                // Предпочтительный путь: Contents[i].TextString (вместо устаревших Table.GetTextString)
                var cell = table.Cells[row, col];
                if (cell != null && cell.Contents != null && cell.Contents.Count > 0 && cell.Contents[0] != null)
                    return cell.Contents[0].TextString ?? "";

                return cell?.TextString ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static void ApplyAcadMergesToExcel(Table table, IXLWorksheet ws, int rows, int cols)
        {
            var visited = new bool[rows, cols];

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    bool isMerged;
                    try
                    {
                        // IsMerged у TableCell — bool? (nullable)
                        isMerged = table.Cells[r, c].IsMerged.GetValueOrDefault(false);
                    }
                    catch
                    {
                        isMerged = false;
                    }

                    if (!isMerged) continue;
                    if (visited[r, c]) continue;

                    CellRange cr;
                    try
                    {
                        // Новый API: Cells[row,col].GetMergeRange()
                        cr = table.Cells[r, c].GetMergeRange();
                    }
                    catch
                    {
                        continue;
                    }

                    int r1 = cr.TopRow;
                    int r2 = cr.BottomRow;
                    int c1 = cr.LeftColumn;
                    int c2 = cr.RightColumn;

                    // защитимся от мусорных диапазонов
                    if (r1 < 0 || c1 < 0 || r2 < 0 || c2 < 0) continue;
                    if (r2 >= rows || c2 >= cols) continue;
                    if (r1 == r2 && c1 == c2) continue;

                    for (int rr = r1; rr <= r2; rr++)
                        for (int cc = c1; cc <= c2; cc++)
                            visited[rr, cc] = true;

                    ws.Range(r1 + 1, c1 + 1, r2 + 1, c2 + 1).Merge();
                }
            }
        }

        // =========================================================================
        // PENDING / IDLE
        // =========================================================================
        private static void StartPending(Document doc, PendingImport session)
        {
            _pending[doc] = session;

            if (!_idleHooked)
            {
                _idleHooked = true;
                AcadCoreApp.Idle += OnIdle;
            }

            if (!_docDestroyHooked)
            {
                _docDestroyHooked = true;
                AcadCoreApp.DocumentManager.DocumentToBeDestroyed += OnDocToBeDestroyed;
            }
        }

        private static void OnIdle(object sender, EventArgs e)
        {
            var docs = new List<Document>(_pending.Keys);

            foreach (var doc in docs)
            {
                if (!_pending.TryGetValue(doc, out var session))
                    continue;

                bool locked = IsFileLocked(session.FilePath);

                // Ключевой UX-фикс:
                // ждём, пока Excel реально откроет файл (появится lock), иначе импорт может отработать сразу же.
                if (!session.SeenLocked)
                {
                    if (locked)
                        session.SeenLocked = true;

                    continue;
                }

                // Excel уже открывал файл -> ждём его закрытия (lock снят)
                if (locked)
                    continue;

                try
                {
                    ImportXlsxToTable(doc, session);
                }
                catch (System.Exception ex)
                {
                    doc.Editor.WriteMessage($"\n[Autocad Tools TableExcel] Ошибка импорта: {ex}\n");
                }
                finally
                {
                    _pending.Remove(doc);
                }
            }

            if (_pending.Count == 0 && _idleHooked)
            {
                AcadCoreApp.Idle -= OnIdle;
                _idleHooked = false;
            }
        }

        // =========================================================================
        // IMPORT
        // =========================================================================
        private static void ImportXlsxToTable(Document doc, PendingImport session)
        {
            var ed = doc.Editor;

            using (doc.LockDocument())
            using (var tr = doc.TransactionManager.StartTransaction())
            {
                var table = tr.GetObject(session.TableId, OpenMode.ForWrite, false) as Table;
                if (table == null)
                    throw new InvalidOperationException("Таблица не найдена (возможно удалена/заменена).");

                (int usedRows, int usedCols, string[,] data, List<ExcelMerge> merges) = ReadXlsx(session.FilePath);

                int oldRows = table.Rows.Count;
                int oldCols = table.Columns.Count;

                // расширяем только вверх, удаления не трогаем
                int targetRows = Math.Max(oldRows, usedRows);
                int targetCols = Math.Max(oldCols, usedCols);

                if (targetRows > oldRows)
                {
                    double h = oldRows > 0 ? table.Rows[oldRows - 1].Height : 3.0;
                    table.InsertRows(oldRows, h, targetRows - oldRows);

                    // копируем оформление новых строк из соседней (последней старой)
                    ApplyFormattingForNewRows(table, firstNewRow: oldRows, lastNewRow: targetRows - 1, templateRow: Math.Max(oldRows - 1, 0));
                }

                if (targetCols > oldCols)
                {
                    double w = oldCols > 0 ? table.Columns[oldCols - 1].Width : 30.0;
                    table.InsertColumns(oldCols, w, targetCols - oldCols);

                    // копируем оформление новых колонок из соседней (последней старой)
                    ApplyFormattingForNewColumns(table, firstNewCol: oldCols, lastNewCol: targetCols - 1, templateCol: Math.Max(oldCols - 1, 0));
                }

                // пишем значения только в область Excel
                int rMaxWrite = Math.Min(usedRows, table.Rows.Count);
                int cMaxWrite = Math.Min(usedCols, table.Columns.Count);

                for (int r = 0; r < rMaxWrite; r++)
                {
                    for (int c = 0; c < cMaxWrite; c++)
                    {
                        // merged: пишем только в top-left
                        if (IsAcadCellMergedAndNotTopLeft(table, r, c))
                            continue;

                        table.Cells[r, c].TextString = data[r, c] ?? "";
                    }
                }

                // перенос объединений Excel -> AutoCAD (только добавление merge)
                int mergesApplied = ApplyExcelMergesToAcad(table, merges);

                table.GenerateLayout();
                tr.Commit();

                ed.WriteMessage($"\n[Autocad Tools TableExcel] Импорт выполнен. Rows={usedRows}, Cols={usedCols}, MergesApplied={mergesApplied}\n");
            }
        }

        private static (int rows, int cols, string[,] data, List<ExcelMerge> merges) ReadXlsx(string filePath)
        {
            using (var wb = new XLWorkbook(filePath))
            {
                var ws = wb.Worksheet("Table");

                // meta (если есть)
                int metaRows = 0, metaCols = 0;
                try
                {
                    var meta = wb.Worksheet("_meta");
                    metaRows = meta.Cell(1, 1).GetValue<int>();
                    metaCols = meta.Cell(1, 2).GetValue<int>();
                }
                catch { }

                // used range (может быть null)
                var used = ws.RangeUsed();
                int usedRows = used?.RowCount() ?? 0;
                int usedCols = used?.ColumnCount() ?? 0;

                // merges
                var merges = new List<ExcelMerge>();
                int maxMergeRow = 0;
                int maxMergeCol = 0;

                foreach (var mr in ws.MergedRanges)
                {
                    int r1 = mr.RangeAddress.FirstAddress.RowNumber - 1;
                    int c1 = mr.RangeAddress.FirstAddress.ColumnNumber - 1;
                    int r2 = mr.RangeAddress.LastAddress.RowNumber - 1;
                    int c2 = mr.RangeAddress.LastAddress.ColumnNumber - 1;

                    if (r1 < 0 || c1 < 0 || r2 < 0 || c2 < 0) continue;
                    if (r2 < r1 || c2 < c1) continue;
                    if (r1 == r2 && c1 == c2) continue;

                    merges.Add(new ExcelMerge(r1, c1, r2, c2));

                    if (r2 + 1 > maxMergeRow) maxMergeRow = r2 + 1;
                    if (c2 + 1 > maxMergeCol) maxMergeCol = c2 + 1;
                }

                int rows = Math.Max(Math.Max(metaRows, usedRows), maxMergeRow);
                int cols = Math.Max(Math.Max(metaCols, usedCols), maxMergeCol);

                if (rows <= 0 || cols <= 0)
                    return (0, 0, new string[0, 0], merges);

                var data = new string[rows, cols];

                for (int r = 1; r <= rows; r++)
                    for (int c = 1; c <= cols; c++)
                        data[r - 1, c - 1] = ws.Cell(r, c).GetString();

                return (rows, cols, data, merges);
            }
        }

        private static int ApplyExcelMergesToAcad(Table table, List<ExcelMerge> merges)
        {
            if (merges == null || merges.Count == 0) return 0;

            int applied = 0;

            foreach (var m in merges)
            {
                if (m.TopRow < 0 || m.LeftCol < 0) continue;
                if (m.BottomRow >= table.Rows.Count) continue;
                if (m.RightCol >= table.Columns.Count) continue;
                if (m.BottomRow < m.TopRow || m.RightCol < m.LeftCol) continue;
                if (m.BottomRow == m.TopRow && m.RightCol == m.LeftCol) continue;

                try
                {
                    var cell = table.Cells[m.TopRow, m.LeftCol];

                    bool alreadyMerged = false;
                    try { alreadyMerged = cell.IsMerged.GetValueOrDefault(false); } catch { }

                    if (alreadyMerged)
                    {
                        try
                        {
                            var cr0 = cell.GetMergeRange();
                            if (cr0.TopRow == m.TopRow && cr0.LeftColumn == m.LeftCol &&
                                cr0.BottomRow == m.BottomRow && cr0.RightColumn == m.RightCol)
                                continue;
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    // FIX: CellRange нельзя создавать через new(...), используем CellRange.Create(table, ...) [web:307]
                    var cr = CellRange.Create(table, m.TopRow, m.LeftCol, m.BottomRow, m.RightCol);
                    table.MergeCells(cr);
                    applied++;
                }
                catch
                {
                    // merge может конфликтовать с существующими — пропускаем, не валим импорт
                }
            }

            return applied;
        }

        private static bool IsAcadCellMergedAndNotTopLeft(Table table, int row, int col)
        {
            bool merged;
            try { merged = table.Cells[row, col].IsMerged.GetValueOrDefault(false); }
            catch { return true; }

            if (!merged) return false;

            try
            {
                var cr = table.Cells[row, col].GetMergeRange();
                return !(row == cr.TopRow && col == cr.LeftColumn);
            }
            catch
            {
                return true;
            }
        }

        // =========================================================================
        // FORMAT COPY (NEW ROWS/COLS)
        // =========================================================================
        private static void ApplyFormattingForNewRows(Table table, int firstNewRow, int lastNewRow, int templateRow)
        {
            for (int r = firstNewRow; r <= lastNewRow; r++)
                for (int c = 0; c < table.Columns.Count; c++)
                    CopyCellFormatting(table, templateRow, c, r, c);
        }

        private static void ApplyFormattingForNewColumns(Table table, int firstNewCol, int lastNewCol, int templateCol)
        {
            for (int c = firstNewCol; c <= lastNewCol; c++)
                for (int r = 0; r < table.Rows.Count; r++)
                    CopyCellFormatting(table, r, templateCol, r, c);
        }

        private static void CopyCellFormatting(Table table, int srcRow, int srcCol, int dstRow, int dstCol)
        {
            if (srcRow < 0 || srcCol < 0) return;
            if (dstRow < 0 || dstCol < 0) return;
            if (srcRow >= table.Rows.Count || srcCol >= table.Columns.Count) return;
            if (dstRow >= table.Rows.Count || dstCol >= table.Columns.Count) return;

            Cell src = null;
            Cell dst = null;

            try { src = table.Cells[srcRow, srcCol]; } catch { }
            try { dst = table.Cells[dstRow, dstCol]; } catch { }

            if (src == null || dst == null) return;

            // 1) стиль ячейки
            try { dst.Style = src.Style; } catch { }

            // 2) границы
            try { CopyBorders(src.Borders, dst.Borders); } catch { }
        }

        private static void CopyBorders(CellBorders src, CellBorders dst)
        {
            if (src == null || dst == null) return;

            CopyBorder(src.Left, dst.Left);
            CopyBorder(src.Right, dst.Right);
            CopyBorder(src.Top, dst.Top);
            CopyBorder(src.Bottom, dst.Bottom);
            CopyBorder(src.Horizontal, dst.Horizontal);
            CopyBorder(src.Vertical, dst.Vertical);
        }

        private static void CopyBorder(CellBorder src, CellBorder dst)
        {
            if (src == null || dst == null) return;

            try { dst.Color = src.Color; } catch { }
            try { dst.Linetype = src.Linetype; } catch { }
            try { dst.LineWeight = src.LineWeight; } catch { }
            try { dst.LineStyle = src.LineStyle; } catch { }
            try { dst.Overrides = src.Overrides; } catch { }
            try { dst.Margin = src.Margin; } catch { }
        }

        // =========================================================================
        // FILE LOCK / CLEANUP
        // =========================================================================
        private static bool IsFileLocked(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static void OnDocToBeDestroyed(object sender, DocumentCollectionEventArgs e)
        {
            try
            {
                var doc = e?.Document;
                if (doc == null) return;

                if (_pending.ContainsKey(doc))
                    _pending.Remove(doc);
            }
            catch { }
        }

        private static string MakeSafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "drawing";

            foreach (var ch in Path.GetInvalidFileNameChars())
                name = name.Replace(ch, '_');

            return name;
        }

        private sealed class PendingImport
        {
            public ObjectId TableId { get; set; }
            public string FilePath { get; set; }
            public DateTime CreatedUtc { get; set; }

            public bool SeenLocked { get; set; }
        }

        private readonly struct ExcelMerge
        {
            public readonly int TopRow;
            public readonly int LeftCol;
            public readonly int BottomRow;
            public readonly int RightCol;

            public ExcelMerge(int topRow, int leftCol, int bottomRow, int rightCol)
            {
                TopRow = topRow;
                LeftCol = leftCol;
                BottomRow = bottomRow;
                RightCol = rightCol;
            }
        }
    }
}
