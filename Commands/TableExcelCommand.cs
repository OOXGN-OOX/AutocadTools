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
        [CommandMethod("AUTOCADTOOLS_TABLEEXCEL")]
        public void Run()
        {
            var doc = AcadCoreApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var ed = doc.Editor;

            try
            {
                var peo = new PromptEntityOptions("\nВыберите таблицу AutoCAD: ");
                peo.SetRejectMessage("\nНужен объект TABLE.");
                peo.AddAllowedClass(typeof(Table), exactMatch: false);

                var per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK) return;

                var service = new TableExcelService();
                service.ExportOpenAndAutoImport(doc, per.ObjectId);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Autocad Tools TableExcel] Ошибка: {ex}\n");
            }
        }
    }
}
