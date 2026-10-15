// Copyright (c) 2026 OOXGN (GitHub: OOXGN-OOX). All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;
using AutocadTools.Services;

// Core.Application
using AcadCoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutocadTools.Commands
{
    public sealed class TableExcelCommand
    {
        [CommandMethod("ATEXCEL", CommandFlags.UsePickSet | CommandFlags.Modal)]
        [CommandMethod("АТЭКСЕЛЬ", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void Run()
        {
            var doc = AcadCoreApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var ed = doc.Editor;

            try
            {
                ObjectId tableId = ObjectId.Null;
                var selection = ed.SelectImplied();
                if (selection.Status == PromptStatus.OK && selection.Value.Count == 1)
                {
                    var id = selection.Value.GetObjectIds()[0];
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                        if (tr.GetObject(id, OpenMode.ForRead) is Table) tableId = id;
                }
                if (tableId.IsNull)
                {
                    var options = new PromptEntityOptions("\nВыберите таблицу AutoCAD: ");
                    options.SetRejectMessage("\nВыберите таблицу TABLE.");
                    options.AddAllowedClass(typeof(Table), false);
                    var result = ed.GetEntity(options);
                    if (result.Status != PromptStatus.OK) return;
                    tableId = result.ObjectId;
                }
                ed.SetImpliedSelection(Array.Empty<ObjectId>());
                var service = new TableExcelService();
                service.ExportOpenAndAutoImport(doc, tableId);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Autocad Tools TableExcel] Ошибка: {ex}\n");
            }
        }
    }
}
