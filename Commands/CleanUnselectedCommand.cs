// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

// File: AutocadTools/Commands/CleanUnselectedCommand.cs
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Services;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutocadTools.Commands
{
    public class CleanUnselectedCommand
    {
        [CommandMethod("ATDELETEOTHERS", CommandFlags.UsePickSet | CommandFlags.Modal)]
        [CommandMethod("АТУДАЛИТЬОСТАЛЬНОЕ", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void Execute()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            try
            {
                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\nВыберите объекты, которые нужно ОСТАВИТЬ (остальные будут удалены): ";
                pso.RejectObjectsOnLockedLayers = false;

                var psr = ed.SelectImplied();
                if (psr.Status != PromptStatus.OK || psr.Value.Count == 0) psr = ed.GetSelection(pso);

                if (psr.Status != PromptStatus.OK || psr.Value.Count == 0)
                {
                    ed.WriteMessage("\nНичего не выбрано. Команда отменена.");
                    return;
                }

                HashSet<ObjectId> keepIds = new HashSet<ObjectId>();
                foreach (SelectedObject so in psr.Value)
                {
                    keepIds.Add(so.ObjectId);
                }

                var scope = new PromptKeywordOptions("\nУдалить остальные объекты") { AllowNone = true };
                scope.Keywords.Add("Model", "Модель", "Модель");
                scope.Keywords.Add("Layouts", "ТакжеЛисты", "ТакжеЛисты");
                scope.Keywords.Default = "Model";
                var answer = ed.GetKeywords(scope);
                if (answer.Status != PromptStatus.OK && answer.Status != PromptStatus.None) return;
                bool cleanLayouts = answer.StringResult == "Layouts";

                int deletedCount = 0;
                int purgedCount = 0;

                // Блокировка документа обязательна!
                using (doc.LockDocument())
                {
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        var service = new CleanUnselectedService(doc.Database, tr);
                        deletedCount = service.Process(keepIds, cleanLayouts);

                        if (deletedCount > 0)
                        {
                            ed.WriteMessage("\nВыполняется Purge...");
                            purgedCount = service.PurgeDatabase();
                        }

                        tr.Commit();
                    }
                    ed.Regen();
                }

                ed.WriteMessage($"\nГотово. Удалено: {deletedCount}. Purge: {purgedCount}.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nОшибка: {ex.Message}");
            }
        }
    }
}