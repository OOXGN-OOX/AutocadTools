// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;



using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using AcadCoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutocadTools.Services
{
    public sealed class TableExcelService
    {
        private static readonly Dictionary<(Document Document, ObjectId TableId), PendingImport> _pending = new();
        private static bool _idleHooked;
        private static bool _docDestroyHooked;

        public void ExportOpenAndAutoImport(Document doc, ObjectId tableId)
        {
            if (doc == null) return;
            if (_pending.ContainsKey((doc, tableId)))
                throw new InvalidOperationException("Сначала завершите редактирование уже открытой таблицы этой таблицы.");
            TableWorkbookData snapshot;
            using (doc.LockDocument())
            using (var transaction = doc.TransactionManager.StartOpenCloseTransaction())
            {
                if (transaction.GetObject(tableId, OpenMode.ForRead, false) is not Table table)
                    throw new InvalidOperationException("Выбранный объект не является таблицей.");
                snapshot = ExportSnapshot(table);
            }
            var directory = Path.Combine(Path.GetTempPath(), "AutocadTools", "TableExcel");
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, MakeSafeFileName(Path.GetFileNameWithoutExtension(doc.Name)) + "_" + Guid.NewGuid().ToString("N") + ".xlsx");
            ExcelWorkbookService.Save(file, snapshot);
            // Do not register a pending session if no spreadsheet editor can be launched.
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true })?.Dispose();
            var info = new FileInfo(file);
            StartPending(doc, new PendingImport
            {
                TableId = tableId,
                FilePath = file,
                State = new FileEditSession(DateTime.UtcNow, info.LastWriteTimeUtc, info.Length)
            });
            doc.Editor.WriteMessage($"\n[Autocad Tools] Таблица открыта: {file}\nСохраните изменения и закройте файл. Импорт выполнится, когда этот чертёж активен и не выполняет команду.\n");
        }

        internal static TableWorkbookData ExportSnapshot(Table table)
        {
            int rows = table.Rows.Count, columns = table.Columns.Count;
            var values = new string[rows, columns];
            var merges = new HashSet<ExcelMerge>();
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    values[row, column] = GetAcadCellText(table, row, column);
                    if (table.Cells[row, column].IsMerged.GetValueOrDefault(false))
                    {
                        var range = table.Cells[row, column].GetMergeRange();
                        merges.Add(new ExcelMerge(range.TopRow, range.LeftColumn, range.BottomRow, range.RightColumn));
                    }
                }
            return new TableWorkbookData(rows, columns, values, new List<ExcelMerge>(merges));
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

        private static void StartPending(Document doc, PendingImport session)
        {
            _pending[(doc, session.TableId)] = session;

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

        private static DateTime _nextPollUtc;

        private static void OnIdle(object sender, EventArgs e)
        {
            var now = DateTime.UtcNow;
            if (now < _nextPollUtc) return;
            _nextPollUtc = now.AddSeconds(1);
            foreach (var key in new List<(Document Document, ObjectId TableId)>(_pending.Keys))
            {
                var doc = key.Document;
                if (!_pending.TryGetValue(key, out var session)) continue;
                try
                {
                    if (doc != AcadCoreApp.DocumentManager.MdiActiveDocument || !string.IsNullOrEmpty(doc.CommandInProgress)) continue;
                    if (!File.Exists(session.FilePath)) throw new FileNotFoundException("Файл таблицы удалён или перемещён.", session.FilePath);
                    if (now - session.State.CreatedUtc > TimeSpan.FromHours(24))
                        throw new TimeoutException("Сеанс редактирования таблицы истёк. XLSX сохранён: " + session.FilePath);
                    var info = new FileInfo(session.FilePath);
                    if (!session.State.IsReady(now, IsFileLocked(session.FilePath), info.LastWriteTimeUtc, info.Length)) continue;
                    var snapshot = ExcelWorkbookService.Read(session.FilePath);
                    ImportWorkbook(doc, session.TableId, snapshot);
                    RemovePending(key);
                }
                catch (System.Exception ex)
                {
                    Infrastructure.PluginLog.Error("Import table from " + session.FilePath, ex);
                    RemovePending(key);
                    try { doc.Editor.WriteMessage("\n[Autocad Tools] Таблица не импортирована: " + ex.Message + "\nФайл сохранён: " + session.FilePath + "\n"); }
                    catch { }
                }
            }
        }

        internal static void ImportWorkbook(Document doc, ObjectId tableId, TableWorkbookData snapshot)
        {
            var ed = doc.Editor;

            using (doc.LockDocument())
            using (var tr = doc.TransactionManager.StartTransaction())
            {
                var table = tr.GetObject(tableId, OpenMode.ForWrite, false) as Table;
                if (table == null)
                    throw new InvalidOperationException("Таблица не найдена (возможно удалена/заменена).");

                ApplyWorkbookData(table, snapshot);
                tr.Commit();
                ed.WriteMessage($"\n[Autocad Tools] Таблица обновлена: {snapshot.Rows} строк, {snapshot.Columns} столбцов.\n");
            }
        }

        internal static ObjectId CreateTable(Document doc, TableWorkbookData snapshot, Autodesk.AutoCAD.Geometry.Point3d position)
        {
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);
                var table = new Table { TableStyle = doc.Database.Tablestyle, Position = position };
                table.SetSize(Math.Max(1, snapshot.Rows), Math.Max(1, snapshot.Columns));
                table.SetRowHeight(8); table.SetColumnWidth(35);
                var id = space.AppendEntity(table); tr.AddNewlyCreatedDBObject(table, true);
                ApplyWorkbookData(table, snapshot);
                tr.Commit();
                return id;
            }
        }

        private static void ApplyWorkbookData(Table table, TableWorkbookData snapshot)
        {
                int usedRows = snapshot.Rows, usedCols = snapshot.Columns;
                var data = snapshot.Values;
                var merges = snapshot.Merges;
                table.SuppressRegenerateTable(true);
                try
                {

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

                UnmergeImportedArea(table, usedRows, usedCols);
                // Write only the imported region; preserve cells outside it.
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

                }
                finally { table.SuppressRegenerateTable(false); }
                table.GenerateLayout();
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

                    // FIX: CellRange нельзя создавать через new(...), используем CellRange.Create(table, ...)
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
            foreach (var key in new List<(Document Document, ObjectId TableId)>(_pending.Keys))
                if (key.Document == e.Document) RemovePending(key);
        }

        private static void RemovePending((Document Document, ObjectId TableId) key)
        {
            _pending.Remove(key);
            if (_pending.Count == 0) Shutdown();
        }

        public static void Shutdown()
        {
            if (_idleHooked) AcadCoreApp.Idle -= OnIdle;
            if (_docDestroyHooked) AcadCoreApp.DocumentManager.DocumentToBeDestroyed -= OnDocToBeDestroyed;
            _idleHooked = _docDestroyHooked = false;
            _pending.Clear();
        }

        private static void UnmergeImportedArea(Table table, int rows, int columns)
        {
            var ranges = new HashSet<ExcelMerge>();
            for (int row = 0; row < Math.Min(rows, table.Rows.Count); row++)
                for (int column = 0; column < Math.Min(columns, table.Columns.Count); column++)
                {
                    if (!table.Cells[row, column].IsMerged.GetValueOrDefault(false)) continue;
                    var range = table.Cells[row, column].GetMergeRange();
                    if (range.BottomRow < rows && range.RightColumn < columns)
                        ranges.Add(new ExcelMerge(range.TopRow, range.LeftColumn, range.BottomRow, range.RightColumn));
                }
            foreach (var range in ranges)
                table.UnmergeCells(CellRange.Create(table, range.TopRow, range.LeftCol, range.BottomRow, range.RightCol));
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
            public FileEditSession State { get; set; }
        }
    }
}
