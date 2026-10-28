// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Models;
using AutocadTools.ViewModels;
using AutocadTools.Views;

namespace AutocadTools.Commands
{
    /// <summary>
    /// Команда автонумерации для текстов, мультитекстов, атрибутов блоков и мультивыносок
    /// </summary>
    public sealed class AutoNumCommand
    {
        #region Constants

        private const string DxfText = "TEXT";
        private const string DxfMText = "MTEXT";
        private const string DxfMLeader = "MULTILEADER";
        private const string DxfBlock = "INSERT";

        #endregion

        #region Command Entry Point

        [CommandMethod("ATNUM", CommandFlags.UsePickSet | CommandFlags.Modal)]
        [CommandMethod("АТНУМ", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void ExecuteAutoNum()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var ed = doc.Editor;
            var db = doc.Database;

            try
            {
                var settings = LoadSettings(ed);
                var implied = ed.SelectImplied();
                var preselected = implied.Status == PromptStatus.OK && implied.Value.Count > 0 ? implied.Value.GetObjectIds() : null;
                while (true)
                {
                    var options = new PromptIntegerOptions("\nНачальный номер или")
                    {
                        DefaultValue = settings.StartNumber, UseDefaultValue = true, AllowNegative = false
                    };
                    options.Keywords.Add("Settings", "Настройки", "Настройки");
                    var result = ed.GetInteger(options);
                    if (result.Status == PromptStatus.Keyword)
                    {
                        var viewModel = new AutoNumSettingsViewModel { Settings = settings };
                        if (Application.ShowModalWindow(new AutoNumSettingsWindow(viewModel)) != true) return;
                        settings = viewModel.Settings;
                        settings.Save();
                        continue;
                    }
                    if (result.Status != PromptStatus.OK) return;
                    settings.StartNumber = result.Value;
                    settings.Save();
                    break;
                }
                var selectedObjects = preselected != null ? SelectionSet.FromObjectIds(preselected) : GetSelection(ed, settings);
                if (selectedObjects == null || selectedObjects.Count == 0) return;
                ed.SetImpliedSelection(Array.Empty<ObjectId>());

                // Выполняем нумерацию
                int numberedCount = new Services.NumberingService().Number(db, ed, selectedObjects.GetObjectIds(), settings);
                ed.WriteMessage($"\nПронумеровано объектов: {numberedCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nОшибка: {ex.Message}");
            }
        }

        #endregion

        #region Settings

        private AutoNumSettings LoadSettings(Editor ed)
        {
            try
            {
                return AutoNumSettings.Load();
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nПредупреждение: не удалось загрузить настройки. {ex.Message}");
                return new AutoNumSettings();
            }
        }

        #endregion

        #region Selection

        private SelectionSet GetSelection(Editor ed, AutoNumSettings settings)
        {
            // Проверяем предварительную выборку
            var psr = ed.SelectImplied();
            if (psr.Status == PromptStatus.OK && psr.Value.Count > 0)
                return psr.Value;

            // Запрашиваем выбор у пользователя
            var pso = new PromptSelectionOptions
            {
                MessageForAdding = GetSelectionPrompt(settings)
            };

            var filter = CreateSelectionFilter(settings);
            psr = ed.GetSelection(pso, filter);

            return psr.Status == PromptStatus.OK ? psr.Value : null;
        }

        private string GetSelectionPrompt(AutoNumSettings settings)
        {
            return settings.Mode switch
            {
                AutoNumSettings.NumberingMode.Text => "\nВыберите тексты (TEXT/MTEXT) для нумерации: ",
                AutoNumSettings.NumberingMode.MLeader => "\nВыберите мультивыноски (MLEADER) для нумерации: ",
                AutoNumSettings.NumberingMode.Attribute => $"\nВыберите блоки с атрибутом '{settings.AttributeTag}' для нумерации: ",
                AutoNumSettings.NumberingMode.Table => "\nВыберите таблицы для нумерации: ",
                _ => "\nВыберите объекты для нумерации: "
            };
        }

        private SelectionFilter CreateSelectionFilter(AutoNumSettings settings)
        {
            TypedValue[] filterValues = settings.Mode switch
            {
                AutoNumSettings.NumberingMode.Text => new[]
                {
                    new TypedValue((int)DxfCode.Operator, "<OR"),
                    new TypedValue((int)DxfCode.Start, DxfText),
                    new TypedValue((int)DxfCode.Start, DxfMText),
                    new TypedValue((int)DxfCode.Operator, "OR>")
                },
                AutoNumSettings.NumberingMode.MLeader => new[]
                {
                    new TypedValue((int)DxfCode.Start, DxfMLeader)
                },
                AutoNumSettings.NumberingMode.Attribute => new[]
                {
                    new TypedValue((int)DxfCode.Start, DxfBlock)
                },
                AutoNumSettings.NumberingMode.Table => new[] { new TypedValue((int)DxfCode.Start, "ACAD_TABLE") },
                _ => Array.Empty<TypedValue>()
            };

            return new SelectionFilter(filterValues);
        }

        #endregion

    }
}
