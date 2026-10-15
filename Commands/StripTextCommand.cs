// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

// File: AutocadTools/Commands/StripTextCommand.cs
using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Services;
using AutocadTools.ViewModels;
using AutocadTools.Views;

// Важно для разрешения конфликта имен
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutocadTools.Commands
{
    public class StripTextCommand
    {
        [CommandMethod("ATTEXTRESET", CommandFlags.UsePickSet | CommandFlags.Modal)]
        [CommandMethod("АТСБРОСТЕКСТА", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void Execute()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            try
            {
                TypedValue[] tvs = new TypedValue[] {
                    new TypedValue((int)DxfCode.Operator, "<OR"),
                    new TypedValue((int)DxfCode.Start, "MTEXT"),
                    new TypedValue((int)DxfCode.Start, "TEXT"),
                    new TypedValue((int)DxfCode.Start, "DIMENSION"),
                    new TypedValue((int)DxfCode.Start, "MULTILEADER"),
                    new TypedValue((int)DxfCode.Start, "ACAD_TABLE"),
                    new TypedValue((int)DxfCode.Start, "INSERT"),
                    new TypedValue((int)DxfCode.Operator, "OR>")
                };
                SelectionFilter filter = new SelectionFilter(tvs);

                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\nВыберите объекты для сброса текста: ";
                pso.RejectObjectsOnLockedLayers = true;

                var psr = ed.SelectImplied();
                if (psr.Status != PromptStatus.OK || psr.Value.Count == 0) psr = ed.GetSelection(pso, filter);

                if (psr.Status != PromptStatus.OK) return;

                var vm = new StripTextViewModel();
                var window = new StripTextWindow { DataContext = vm };

                // В MVVM для закрытия окна из VM
                vm.CloseAction = new Action(window.Close);

                // Модальное окно (блокирует AutoCAD пока открыто)
                bool? result = AcadApp.ShowModalWindow(window);

                if (vm.DialogResult != true) return;

                var options = vm.Options;
                StripTextService stripper = new StripTextService(options);
                int count = 0;

                using (doc.LockDocument())
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (SelectedObject so in psr.Value)
                        {
                            if (so.ObjectId.IsErased) continue;
                            Entity ent = (Entity)tr.GetObject(so.ObjectId, OpenMode.ForWrite);

                            if (ent is MText mtext) { stripper.ProcessMText(mtext); count++; }
                            else if (ent is DBText dbText) { stripper.ProcessDBText(dbText); count++; }
                            else if (ent is MLeader mleader) { stripper.ProcessMLeader(mleader); count++; }
                            else if (ent is Dimension dim) { stripper.ProcessDimension(dim); count++; }
                            else if (ent is Table table) { stripper.ProcessTable(table); count++; }
                            else if (ent is BlockReference blkRef)
                            {
                                if (stripper.ProcessBlockReference(blkRef, tr)) count++;
                            }
                        }
                        tr.Commit();
                    }
                    ed.Regen();
                }
                ed.WriteMessage($"\nОбработано объектов: {count}.\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nОшибка: {ex.Message}\n");
            }
        }
    }
}