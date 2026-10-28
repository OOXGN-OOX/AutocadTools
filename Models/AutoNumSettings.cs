// SPDX-License-Identifier: AGPL-3.0-only
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: BotPlusAutocadTool
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using Microsoft.Win32;

namespace AutocadTools.Models
{
    /// <summary>
    /// Настройки команды автонумерации с сохранением в реестр Windows
    /// </summary>
    public class AutoNumSettings
    {
        #region Constants

        private const string RegistryPath = @"Software\OOXGN\AutocadTools\AutoNum";

        #endregion

        #region Enums

        /// <summary>
        /// Порядок нумерации объектов
        /// </summary>
        public enum NumberingOrder
        {
            /// <summary>В порядке выбора</summary>
            Pick,
            /// <summary>Геометрическая сортировка</summary>
            Geometric
        }

        /// <summary>
        /// Пресет сортировки
        /// </summary>
        public enum SortPreset
        {
            /// <summary>По строкам: сверху вниз, слева направо ↓→</summary>
            RowsTopDown,
            /// <summary>По строкам: снизу вверх, слева направо ↑→</summary>
            RowsBottomUp,
            /// <summary>По столбцам: слева направо, сверху вниз →↓</summary>
            ColumnsLeftRight,
            /// <summary>По столбцам: справа налево, сверху вниз ←↓</summary>
            ColumnsRightLeft,
            /// <summary>Только горизонталь: слева направо →</summary>
            HorizontalOnly,
            /// <summary>Только вертикаль: сверху вниз ↓</summary>
            VerticalOnly
        }

        /// <summary>
        /// Режим нумерации (тип целевых объектов)
        /// </summary>
        public enum NumberingMode
        {
            /// <summary>Текстовые объекты (DBText, MText)</summary>
            Text,
            /// <summary>Атрибуты блоков</summary>
            Attribute,
            /// <summary>Мультивыноски (MLeader)</summary>
            MLeader,
            Table
        }

        #endregion

        #region Properties - Numbering Format

        /// <summary>Префикс номера</summary>
        public string Prefix { get; set; } = string.Empty;

        /// <summary>Суффикс номера</summary>
        public string Suffix { get; set; } = string.Empty;

        /// <summary>Начальный номер</summary>
        public int StartNumber { get; set; } = 1;

        /// <summary>Шаг нумерации</summary>
        public int Step { get; set; } = 1;

        /// <summary>Количество знаков (0 = без дополнения нулями)</summary>
        public int Digits { get; set; } = 0;

        #endregion

        #region Properties - Mode & Order

        /// <summary>Порядок нумерации</summary>
        public NumberingOrder Order { get; set; } = NumberingOrder.Pick;

        /// <summary>Режим нумерации</summary>
        public NumberingMode Mode { get; set; } = NumberingMode.Text;

        /// <summary>Тег атрибута (для режима Attribute)</summary>
        public string AttributeTag { get; set; } = "MARK";
        // One-based indices include title and header rows, matching the visible table.
        public int TableRow { get; set; } = 1;
        public int TableColumn { get; set; } = 1;

        #endregion

        #region Properties - Sorting Configuration

        /// <summary>Пресет сортировки</summary>
        public SortPreset Sorting { get; set; } = SortPreset.RowsTopDown;

        #endregion

        #region Methods

        /// <summary>
        /// Загрузить настройки из реестра
        /// </summary>
        public static AutoNumSettings Load()
        {
            var settings = new AutoNumSettings();

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
                if (key == null) return settings;

                settings.Prefix = GetString(key, "Prefix", string.Empty);
                settings.Suffix = GetString(key, "Suffix", string.Empty);
                settings.StartNumber = GetInt(key, "StartNumber", 1);
                settings.Step = GetInt(key, "Step", 1);
                settings.Digits = GetInt(key, "Digits", 0);
                settings.Order = (NumberingOrder)GetInt(key, "Order", (int)NumberingOrder.Pick);
                settings.Mode = (NumberingMode)GetInt(key, "Mode", (int)NumberingMode.Text);
                settings.AttributeTag = GetString(key, "AttributeTag", "MARK");
                settings.Sorting = (SortPreset)GetInt(key, "Sorting", (int)SortPreset.RowsTopDown);
                settings.TableRow = GetInt(key, "TableRow", 1);
                settings.TableColumn = GetInt(key, "TableColumn", 1);
            }
            catch
            {
                // При ошибке чтения используем значения по умолчанию
            }

            settings.Normalize();
            return settings;
        }

        /// <summary>
        /// Сохранить настройки в реестр
        /// </summary>
        public void Save()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
                if (key == null) return;

                key.SetValue("Prefix", Prefix ?? string.Empty);
                key.SetValue("Suffix", Suffix ?? string.Empty);
                key.SetValue("StartNumber", StartNumber);
                key.SetValue("Step", Step);
                key.SetValue("Digits", Digits);
                key.SetValue("Order", (int)Order);
                key.SetValue("Mode", (int)Mode);
                key.SetValue("AttributeTag", AttributeTag ?? "MARK");
                key.SetValue("Sorting", (int)Sorting);
                key.SetValue("TableRow", TableRow);
                key.SetValue("TableColumn", TableColumn);
            }
            catch
            {
                // Игнорируем ошибки записи
            }
        }

        /// <summary>
        /// Форматировать номер с учётом префикса, суффикса и дополнения нулями
        /// </summary>
        public string FormatNumber(int number)
        {
            int digits = System.Math.Clamp(Digits, 0, 16);
            var format = digits > 0 ? "D" + digits : "D";
            return (Prefix ?? string.Empty) + number.ToString(format, System.Globalization.CultureInfo.InvariantCulture) + (Suffix ?? string.Empty);
        }

        internal void Normalize()
        {
            Digits = System.Math.Clamp(Digits, 0, 16);
            if (Step == 0) Step = 1;
            if (!System.Enum.IsDefined(Order)) Order = NumberingOrder.Pick;
            if (!System.Enum.IsDefined(Mode)) Mode = NumberingMode.Text;
            if (!System.Enum.IsDefined(Sorting)) Sorting = SortPreset.RowsTopDown;
            AttributeTag = string.IsNullOrWhiteSpace(AttributeTag) ? "MARK" : AttributeTag;
            TableRow = System.Math.Max(1, TableRow);
            TableColumn = System.Math.Max(1, TableColumn);
        }
        #endregion

        #region Private Helpers

        private static string GetString(RegistryKey key, string name, string defaultValue)
            => key.GetValue(name, defaultValue)?.ToString() ?? defaultValue;

        private static int GetInt(RegistryKey key, string name, int defaultValue)
        {
            var value = key.GetValue(name, defaultValue)?.ToString();
            return int.TryParse(value, out var result) ? result : defaultValue;
        }

        #endregion
    }
}
