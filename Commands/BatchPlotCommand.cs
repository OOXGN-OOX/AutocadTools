// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

// File: Commands/BatchPlotCommand.cs

using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Views;

using SystemException = System.Exception;

namespace AutocadTools.Commands
{
    public class BatchPlotCommand
    {
        [CommandMethod("ATPLOT", CommandFlags.Modal)]
        [CommandMethod("АТПЕЧАТЬ", CommandFlags.Modal)]
        public void Execute()
        {
            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null)
                {
                    Application.ShowAlertDialog("Нет активного документа.\n\nОткройте чертеж перед запуском команды.");
                    return;
                }

                // Проверка: чертеж должен быть сохранен (иметь путь), чтобы DSD и PDF знали куда сохраняться
                if (string.IsNullOrEmpty(doc.Name) || doc.Name.StartsWith("Drawing", StringComparison.OrdinalIgnoreCase))
                {
                    Application.ShowAlertDialog(
                        "Чертеж не сохранен.\n\n" +
                        "Сохраните чертеж перед использованием функции печати,\n" +
                        "чтобы определить путь для вывода PDF.");
                    return;
                }

                using (DocumentLock docLock = doc.LockDocument())
                {
                    // Логика подготовки DSD теперь перенесена внутрь ViewModel (по кнопке "Печать"),
                    // так как она зависит от выбранных пользователем галочек и форматов в таблице.

                    var window = new BatchPlotWindow();
                    Application.ShowModalWindow(window);
                }
            }
            catch (SystemException ex)
            {
                string errorMessage = $"Ошибка запуска окна печати:\n\n{ex.Message}";
                Application.ShowAlertDialog(errorMessage);

                var doc = Application.DocumentManager.MdiActiveDocument;
                doc?.Editor.WriteMessage($"\n[Autocad Tools Error] BatchPlotCommand failed: {ex.Message}\n");
                doc?.Editor.WriteMessage($"[Autocad Tools Error] Stack trace: {ex.StackTrace}\n");
            }
        }
    }
}
