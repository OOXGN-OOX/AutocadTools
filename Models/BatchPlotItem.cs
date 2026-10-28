// SPDX-License-Identifier: AGPL-3.0-only
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: BotPlusAutocadTool
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.DatabaseServices;
using AutocadTools.Services; // ← ДОБАВЬ эту строку

namespace AutocadTools.Models
{
    /// <summary>
    /// Модель элемента для пакетной печати форматов
    /// Представляет один блок формата в чертеже AutoCAD
    /// </summary>
    public class BatchPlotItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _status = "Ожидание";
        private string _name;
        private string _sheetNumber;
        private string _format;

        #region Внутренние свойства AutoCAD

        /// <summary>
        /// Идентификатор блока в базе данных AutoCAD
        /// </summary>
        public ObjectId BlockId { get; set; }

        /// <summary>
        /// Геометрические границы блока для определения окна печати
        /// </summary>
        public Extents3d Extents { get; set; }

        /// <summary>
        /// НОВОЕ: Определение формата (ТОЧНЫЕ размеры по ГОСТ) для использования в PageSetup
        /// </summary>
        public PaperFormatDefinition FormatDefinition { get; set; }
        public int PlotScale { get; set; } = 1;
        public double PlotWidthMm { get; set; }
        public double PlotHeightMm { get; set; }

        #endregion

        #region Публичные свойства для UI

        /// <summary>
        /// Флаг выбора для печати (CheckBox в DataGrid)
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Наименование чертежа (редактируемое поле)
        /// </summary>
        public string Name
        {
            get => _name;
            set
            {
                // ИСПРАВЛЕНИЕ: Валидация и очистка имени файла
                if (value != _name)
                {
                    _name = CleanFileName(value);
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Номер листа (редактируемое поле)
        /// </summary>
        public string SheetNumber
        {
            get => _sheetNumber;
            set
            {
                if (value != _sheetNumber)
                {
                    _sheetNumber = value?.Trim();
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Формат бумаги (выбор из ComboBox)
        /// </summary>
        public string Format
        {
            get => _format;
            set
            {
                // ИСПРАВЛЕНИЕ: Защита от пустых значений
                if (!string.IsNullOrWhiteSpace(value) && value != _format)
                {
                    _format = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Текущий статус обработки (только для чтения)
        /// </summary>
        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region Вспомогательные методы

        /// <summary>
        /// Очистка имени файла от недопустимых символов
        /// </summary>
        private static string CleanFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            // Удаление недопустимых символов для имени файла
            var invalidChars = Path.GetInvalidFileNameChars();
            var cleanedName = new string(name
                .Where(c => !invalidChars.Contains(c))
                .ToArray());

            // Удаление лишних пробелов и точек в начале/конце
            cleanedName = cleanedName.Trim().Trim('.');

            // НОВОЕ: Ограничение длины имени (Windows limit)
            if (cleanedName.Length > 100)
                cleanedName = cleanedName.Substring(0, 100);

            return cleanedName;
        }

        /// <summary>
        /// Проверка валидности элемента перед печатью
        /// </summary>
        public bool IsValid(out string errorMessage)
        {
            errorMessage = null;

            if (BlockId.IsNull || BlockId.IsErased)
            {
                errorMessage = $"Лист {SheetNumber}: Блок удален или недоступен";
                return false;
            }

            if (string.IsNullOrWhiteSpace(Format))
            {
                errorMessage = $"Лист {SheetNumber}: Не указан формат";
                return false;
            }

            if (string.IsNullOrWhiteSpace(SheetNumber))
            {
                errorMessage = "Не указан номер листа";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Получение имени выходного файла PDF
        /// </summary>
        public string GetOutputFileName()
        {
            string safeName = CleanFileName(Name);
            string safeSheet = CleanFileName(SheetNumber);

            // Если наименование пустое, используем только номер листа
            if (string.IsNullOrEmpty(safeName))
                return $"{safeSheet}.pdf";

            return $"{safeSheet}_{safeName}.pdf";
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Вызов события изменения свойства
        /// </summary>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region Переопределение методов

        /// <summary>
        /// Строковое представление элемента для отладки
        /// </summary>
        public override string ToString()
        {
            return $"Лист {SheetNumber} - {Name} ({Format}) [{Status}]";
        }

        #endregion
    }
}
