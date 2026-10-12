// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using System;
using System.Globalization;
using System.Windows.Data;

namespace AutocadTools.ViewModels
{
    /// <summary>
    /// Converter to bind TextBox to StampField by key via FieldsMap
    /// </summary>
    public class StampFieldConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // value - это сам ViewModel (через {Binding .})
            if (value is StampPropsViewModel viewModel && parameter is string key)
            {
                // --- ИСПРАВЛЕНИЕ CS1061 ---
                // Используем FieldsMap вместо старого метода GetFieldByKey
                if (viewModel.FieldsMap != null && viewModel.FieldsMap.TryGetValue(key, out var field))
                {
                    return field.Value ?? string.Empty;
                }
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // OneWay binding в данном случае достаточно, так как обновление идет через 
            // UpdateSourceTrigger=PropertyChanged в самом Binding'е окна,
            // но для совместимости вернем value.
            return value;
        }
    }
}