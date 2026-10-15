// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Services;
using AutocadTools.ViewModels;
using AutocadTools.Views;

// Псевдоним для Application согласно правилам
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutocadTools.Commands
{
    public class SelectSimilarCommand
    {
        [CommandMethod("ATSIMILAR", CommandFlags.UsePickSet | CommandFlags.Modal | CommandFlags.Redraw)]
        [CommandMethod("АТПОДОБНЫЕ", CommandFlags.UsePickSet | CommandFlags.Modal | CommandFlags.Redraw)]
        public void Execute()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            try
            {
                // 1. Логика выбора: сначала проверяем PickFirst (выбрано ли что-то ДО запуска)
                PromptSelectionResult psr = ed.SelectImplied();

                // Если предварительного выбора нет, просим пользователя выбрать объекты
                if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0)
                {
                    PromptSelectionOptions pso = new PromptSelectionOptions();
                    pso.MessageForAdding = "\nВыберите объекты-образцы: ";
                    pso.RejectObjectsOnLockedLayers = false;

                    psr = ed.GetSelection(pso);
                }

                // Если все равно ничего не выбрано — выходим
                if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0)
                {
                    ed.WriteMessage("\nОбъекты не выбраны.");
                    return;
                }

                // 2. Открываем окно настроек (WPF)
                var vm = new SelectSimilarViewModel();
                var window = new SelectSimilarWindow { DataContext = vm };

                // Передаем действие закрытия окна во ViewModel
                vm.CloseAction = new Action(window.Close);

                bool? result = AcadApp.ShowModalWindow(window);

                // Если нажали "Отмена" или просто закрыли окно
                if (vm.DialogResult != true) return;

                // 3. Выполнение поиска
                using (doc.LockDocument())
                {
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        // Получаем список эталонных объектов (Source Entities)
                        List<Entity> sources = new List<Entity>();
                        foreach (SelectedObject so in psr.Value)
                        {
                            var ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                            if (ent != null)
                            {
                                sources.Add(ent);
                            }
                        }

                        // Работаем в текущем пространстве (Model или Layout)
                        var space = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForRead);

                        // Создаем сервис и ищем похожие
                        var service = new SelectSimilarService(vm.Options);
                        List<ObjectId> similarIds = service.FindSimilar(tr, space, sources);

                        if (similarIds.Count > 0)
                        {
                            // Выделяем найденные объекты в редакторе
                            ed.SetImpliedSelection(similarIds.ToArray());
                            ed.WriteMessage($"\nНайдено и выбрано объектов: {similarIds.Count}");
                        }
                        else
                        {
                            ed.WriteMessage("\nПохожие объекты не найдены.");
                        }

                        tr.Commit();
                    }
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nОшибка SelectSimilar: {ex.Message}");
            }
        }
    }
}