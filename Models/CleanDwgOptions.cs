// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

namespace AutocadTools.Models
{
    public class CleanDwgOptions
    {
        // Визуальные объекты
        public bool Points { get; set; } = false;           // Точки
        public bool Wipeouts { get; set; } = false;         // Маскировки
        public bool BlankText { get; set; } = false;        // Пустой текст

        // Невизуальные объекты / Словари / Таблицы
        public bool Groups { get; set; } = true;            // Группы
        public bool LayerFilters { get; set; } = true;      // Фильтры слоев
        public bool LayerReconcile { get; set; } = true;    // Согласование слоев (Reconcile)
        public bool Constraints { get; set; } = false;      // Параметрические зависимости
        public bool SolidHistory { get; set; } = true;      // История 3D тел (Solid History)
        public bool PageSetups { get; set; } = true;        // Настройки печати (кроме текущих)
        public bool RegApps { get; set; } = true;           // Зарегистрированные приложения (RegApps)
        public bool ScaleList { get; set; } = true;         // Список масштабов (Annotation Scales)
        public bool VbaMacros { get; set; } = true;         // VBA Макросы
        public bool DgnLineStyles { get; set; } = true;     // DGN стили линий
        public bool Dictionaries { get; set; } = true;      // Сторонние словари

        // Операции
        public bool PurgeAll { get; set; } = true;          // _PURGE (Очистить все)
        public bool Audit { get; set; } = false;            // _AUDIT (Проверка ошибок)
        public bool SaveDoc { get; set; } = false;          // Сохранить после очистки

        public bool RememberSettings { get; set; } = true;
    }
}