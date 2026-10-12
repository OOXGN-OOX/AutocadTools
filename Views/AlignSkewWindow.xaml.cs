// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AutocadTools.Views
{
    // Класс окна
    public partial class AlignSkewWindow : Window
    {
        public AlignSkewWindow()
        {
            InitializeComponent(); // <--- ЭТО САМАЯ ВАЖНАЯ СТРОКА. БЕЗ НЕЕ ОКНО ПУСТОЕ.
        }
    }

    // Конвертер для инверсии булевых значений (нужен для RadioButton)
    // Можно положить его в отдельный файл, но для простоты оставим здесь
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (targetType != typeof(bool?))
                return !(bool)value;

            return !(bool?)value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (targetType != typeof(bool?))
                return !(bool)value;

            return !(bool?)value;
        }
    }
}