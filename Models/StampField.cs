// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AutocadTools.Models
{
    /// <summary>
    /// Represents a single field in the stamp
    /// </summary>
    public class StampField : INotifyPropertyChanged
    {
        private string _key;
        // По умолчанию инициализируем пробелом
        private string _value = " ";

        public string Key
        {
            get => _key;
            set
            {
                if (_key != value)
                {
                    _key = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Value
        {
            get => _value;
            set
            {
                // Если значение пустое или null, заменяем на пробел
                string newValue = string.IsNullOrEmpty(value) ? " " : value;

                if (_value != newValue)
                {
                    _value = newValue;
                    OnPropertyChanged();
                }
            }
        }

        public StampField(string key, string value = "")
        {
            _key = key;
            // Используем свойство Value, чтобы сработала логика замены пустоты на пробел
            Value = value;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}