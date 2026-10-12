// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

// File: Services/PlotProgressEventArgs.cs

using System;

namespace AutocadTools.Services
{
    /// <summary>
    /// Аргументы события прогресса печати отдельного листа
    /// </summary>
    public class PlotProgressEventArgs : EventArgs
    {
        public string Stage { get; set; }           // Текстовое описание этапа (для логов)
        public string SheetName { get; set; }       // Имя листа
        public int SheetNumber { get; set; }        // Номер текущего листа
        public int TotalSheets { get; set; }        // Общее количество листов
        public string Status { get; set; }          // Статус ("Печать...", "Готово")

        /// <summary>
        /// Прогресс в процентах (0-100)
        /// </summary>
        public int Progress => TotalSheets > 0 ? (SheetNumber * 100 / TotalSheets) : 0;

        public override string ToString()
        {
            return $"[{Progress}%] Лист {SheetNumber}/{TotalSheets}: {SheetName} - {Status}";
        }
    }

    /// <summary>
    /// Аргументы события завершения печати всего пакета
    /// </summary>
    public class PlotCompleteEventArgs : EventArgs
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public int TotalProcessed => SuccessCount + FailedCount;
        public double SuccessRate => TotalProcessed > 0 ? (double)SuccessCount / TotalProcessed * 100 : 0;

        public override string ToString()
        {
            if (IsSuccess)
                return $"✓ Успешно: {SuccessCount}/{TotalProcessed} листов (100%)";
            else
                return $"⚠ Завершено с ошибками: {SuccessCount} успешно, {FailedCount} ошибок ({SuccessRate:F1}%)";
        }
    }

    /// <summary>
    /// Аргументы ошибки печати
    /// </summary>
    public class PlotErrorEventArgs : EventArgs
    {
        public string ErrorMessage { get; set; }

        public override string ToString()
        {
            return $"❌ Ошибка: {ErrorMessage}";
        }
    }
}
