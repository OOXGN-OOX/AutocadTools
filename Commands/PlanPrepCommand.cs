// SPDX-License-Identifier: AGPL-3.0-only
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: BotPlusAutocadTool
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Services;
using AutocadTools.ViewModels;
using AutocadTools.Views;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutocadTools.Commands
{
    public class PlanPrepCommand
    {
        [CommandMethod("ATPLAN", CommandFlags.UsePickSet | CommandFlags.Modal)]
        [CommandMethod("АТПЛАН", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void Execute()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            try
            {
                // 1. Выбор области
                PromptSelectionOptions pso = new PromptSelectionOptions();
                pso.MessageForAdding = "\nВыберите область для подготовки плана: ";
                var psr = ed.SelectImplied();
                if (psr.Status != PromptStatus.OK || psr.Value.Count == 0) psr = ed.GetSelection(pso);

                if (psr.Status != PromptStatus.OK) return;

                // 2. Окно настроек
                var vm = new PlanPrepViewModel();
                var window = new PlanPrepWindow { DataContext = vm };
                vm.CloseAction = new Action(window.Close);

                bool? result = AcadApp.ShowModalWindow(window);
                if (vm.DialogResult != true) return;

                Autodesk.AutoCAD.Geometry.Point3d? basePoint = null;
                if (vm.Options.CreateBlock && vm.Options.PickBasePoint)
                {
                    var picked = ed.GetPoint("\nУкажите базовую точку будущего блока: ");
                    if (picked.Status != PromptStatus.OK) return;
                    basePoint = picked.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                }

                // 3. Выполнение
                using (doc.LockDocument())
                {
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        var service = new PlanPrepService(vm.Options, doc.Database, tr, basePoint);

                        List<ObjectId> ids = new List<ObjectId>();
                        foreach (SelectedObject so in psr.Value)
                        {
                            ids.Add(so.ObjectId);
                        }

                        service.Process(ids);
                        tr.Commit();
                    }
                }
                ed.WriteMessage("\nПодготовка плана завершена успешно.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nОшибка подготовки плана: {ex.Message}");
            }
        }
    }
}
