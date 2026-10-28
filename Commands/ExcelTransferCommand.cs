// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Infrastructure;
using AutocadTools.Services;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutocadTools.Commands
{
    public sealed class ExcelTransferCommand
    {
        internal static ObjectId SelectTable(Editor editor)
        {
            var options = new PromptEntityOptions("\nВыберите таблицу AutoCAD: ");
            options.SetRejectMessage("\nНужен объект TABLE."); options.AddAllowedClass(typeof(Table), false);
            var selected = editor.GetEntity(options);
            return selected.Status == PromptStatus.OK ? selected.ObjectId : ObjectId.Null;
        }
        [CommandMethod("ATEXPORTEXCEL", CommandFlags.Modal)]
        [CommandMethod("АТЭКСПОРТЭКСЕЛЬ", CommandFlags.Modal)]
        public void Export()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                var id = SelectTable(doc.Editor); if (id.IsNull) return;
                TableWorkbookData snapshot;
                using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
                    snapshot = TableExcelService.ExportSnapshot((Table)tr.GetObject(id, OpenMode.ForRead));
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Экспорт таблицы в Excel", Filter = "Книга Excel (*.xlsx)|*.xlsx", DefaultExt = ".xlsx", AddExtension = true,
                    FileName = Path.GetFileNameWithoutExtension(doc.Name) + "_Table.xlsx", InitialDirectory = Path.GetDirectoryName(doc.Database.Filename) ?? "", OverwritePrompt = true
                };
                if (dialog.ShowDialog() != true) return;
                ExcelWorkbookService.Save(dialog.FileName, snapshot);
                doc.Editor.WriteMessage("\n[Autocad Tools] Таблица экспортирована: " + dialog.FileName);
            }
            catch (System.Exception ex) { PluginLog.Error("Export XLSX", ex); doc.Editor.WriteMessage("\nНе удалось экспортировать таблицу: " + ex.Message); }
        }
        [CommandMethod("ATIMPORTEXCEL", CommandFlags.Modal)]
        [CommandMethod("АТИМПОРТЭКСЕЛЬ", CommandFlags.Modal)]
        public void Import()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Импорт таблицы из Excel", Filter = "Книга Excel (*.xlsx)|*.xlsx", CheckFileExists = true };
                if (dialog.ShowDialog() != true) return;
                var data = ExcelWorkbookService.Read(dialog.FileName, allowFirstWorksheet: true);
                if (data.Rows < 1 || data.Columns < 1 || (long)data.Rows * data.Columns > 100000 || data.Rows > 10000 || data.Columns > 256)
                    throw new InvalidDataException("Для импорта в DWG допустимы 1–10000 строк, 1–256 столбцов и до 100000 ячеек. Уменьшите лист Excel.");
                var options = new PromptKeywordOptions("\nИмпортировать таблицу [New/Replace] <New>: ");
                options.Keywords.Add("New"); options.Keywords.Add("Replace"); options.Keywords.Default = "New"; options.AllowNone = true;
                var mode = doc.Editor.GetKeywords(options);
                if (mode.Status != PromptStatus.OK && mode.Status != PromptStatus.None) return;
                if (mode.StringResult == "Replace")
                {
                    var id = SelectTable(doc.Editor); if (id.IsNull) return;
                    TableExcelService.ImportWorkbook(doc, id, data);
                }
                else
                {
                    var point = doc.Editor.GetPoint("\nУкажите точку вставки таблицы: ");
                    if (point.Status != PromptStatus.OK) return;
                    TableExcelService.CreateTable(doc, data, point.Value.TransformBy(doc.Editor.CurrentUserCoordinateSystem));
                }
                doc.Editor.Regen();
                doc.Editor.WriteMessage("\n[Autocad Tools] XLSX импортирован. Используется лист Table, а при его отсутствии — первый видимый лист.");
            }
            catch (System.Exception ex) { PluginLog.Error("Import XLSX", ex); doc.Editor.WriteMessage("\nНе удалось импортировать таблицу: " + ex.Message); }
        }
    }
}
