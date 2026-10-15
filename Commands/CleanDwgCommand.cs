// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Services;
using AutocadTools.ViewModels;
using AutocadTools.Views;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutocadTools.Commands
{
    public class CleanDwgCommand
    {
        [CommandMethod("ATCLEAN")]
        [CommandMethod("АТОЧИСТКА")]
        public void Execute()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;

            // 1. Окно настроек
            var vm = new CleanDwgViewModel();
            var window = new CleanDwgWindow { DataContext = vm };
            vm.CloseAction = new Action(window.Close);

            bool? result = AcadApp.ShowModalWindow(window);
            if (vm.DialogResult != true) return;

            // 2. Очистка
            try
            {
                using (doc.LockDocument())
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var service = new CleanDwgService(vm.Options, doc, tr);
                        service.Process();

                        if (vm.Options.PurgeAll)
                        {
                            doc.Editor.WriteMessage("\nВыполняется полная очистка (Purge)...");
                            PurgeDatabase(db, tr);
                        }

                        tr.Commit();
                    }

                    // Post-processing
                    if (vm.Options.Audit)
                    {
                        doc.SendStringToExecute("_AUDIT _Y ", false, false, false);
                    }

                    if (vm.Options.SaveDoc)
                    {
                        doc.SendStringToExecute("_QSAVE ", false, false, false);
                    }
                }
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage($"\nОшибка CleanDWG: {ex.Message}");
            }
        }

        private void PurgeDatabase(Database db, Transaction tr)
        {
            bool keepPurging = true;
            int totalPurged = 0;

            while (keepPurging)
            {
                keepPurging = false;
                ObjectIdCollection idsToPurge = new ObjectIdCollection();

                CollectTableIds(db.BlockTableId, tr, idsToPurge);
                CollectTableIds(db.LayerTableId, tr, idsToPurge);
                CollectTableIds(db.LinetypeTableId, tr, idsToPurge);
                CollectTableIds(db.TextStyleTableId, tr, idsToPurge);
                CollectTableIds(db.DimStyleTableId, tr, idsToPurge);
                CollectTableIds(db.RegAppTableId, tr, idsToPurge);
                CollectTableIds(db.UcsTableId, tr, idsToPurge);
                CollectTableIds(db.ViewTableId, tr, idsToPurge);
                CollectTableIds(db.ViewportTableId, tr, idsToPurge);

                if (idsToPurge.Count > 0)
                {
                    db.Purge(idsToPurge);

                    if (idsToPurge.Count > 0)
                    {
                        foreach (ObjectId id in idsToPurge)
                        {
                            var obj = tr.GetObject(id, OpenMode.ForWrite);
                            obj.Erase();
                        }
                        totalPurged += idsToPurge.Count;
                        keepPurging = true;
                    }
                }
            }

            if (totalPurged > 0)
            {
                AcadApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\nВсего удалено через Purge: {totalPurged} объектов.");
            }
        }

        private void CollectTableIds(ObjectId tableId, Transaction tr, ObjectIdCollection ids)
        {
            var table = (SymbolTable)tr.GetObject(tableId, OpenMode.ForRead);
            foreach (ObjectId id in table)
            {
                ids.Add(id);
            }
        }
    }
}