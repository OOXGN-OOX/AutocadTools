// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

// File: AutocadTools/ViewModels/StampPropsViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AutocadTools.Models;
using AutocadTools.Services;

namespace AutocadTools.ViewModels
{
    public class StampPropsViewModel : BaseViewModel
    {
        private readonly IAutoCADService _autoCADService;

        // Максимальная длина одной части поля (ограничение AutoCAD Sheet Set Manager — 64 символа)
        private const int MaxFieldPartLength = 64;

        // Словарь для быстрого Binding: Text="{Binding FieldsMap[Key].Value}"
        private Dictionary<string, StampField> _fieldsMap;

        public Dictionary<string, StampField> FieldsMap
        {
            get => _fieldsMap;
            set => SetProperty(ref _fieldsMap, value);
        }

        // Объединённые свойства для UI (одно поле вместо двух)
        private string _обозначениеДокумента;
        public string ОбозначениеДокумента
        {
            get => _обозначениеДокумента;
            set => SetProperty(ref _обозначениеДокумента, value);
        }

        private string _наименованиеОбъекта;
        public string НаименованиеОбъекта
        {
            get => _наименованиеОбъекта;
            set => SetProperty(ref _наименованиеОбъекта, value);
        }

        private string _наименованиеУчастка;
        public string НаименованиеУчастка
        {
            get => _наименованиеУчастка;
            set => SetProperty(ref _наименованиеУчастка, value);
        }

        public RelayCommand UpdateCommand { get; }
        public RelayCommand RefreshCommand { get; }

        public StampPropsViewModel(IAutoCADService autoCADService)
        {
            _autoCADService = autoCADService;
            _fieldsMap = new Dictionary<string, StampField>();

            UpdateCommand = new RelayCommand(ExecuteUpdate);
            RefreshCommand = new RelayCommand(_ => LoadValues());
        }

        public void LoadValues()
        {
            var properties = _autoCADService.LoadStampProperties();

            // Список всех возможных ключей штампа (кроме объединяемых)
            var keys = new[]
            {
                "Взам.инв.№", "Подп.и.дата", "Инв.№",
                "Изм.4", "Кол.уч.4", "Лист.4", "Nдок.4", "Дата.10",
                "Изм.3", "Кол.уч.3", "Лист.3", "Nдок.3", "Дата.9",
                "Изм.2", "Кол.уч.2", "Лист.2", "Nдок.2", "Дата.8",
                "Изм.1", "Кол.уч.1", "Лист.1", "Nдок.1", "Дата.7",
                "Должн.1", "Фам.1", "Дата.1",
                "Должн.2", "Фам.2", "Дата.2",
                "Должн.3", "Фам.3", "Дата.3",
                "Должн.4", "Фам.4", "Дата.4",
                "Должн.5", "Фам.5", "Дата.5",
                "Должн.6", "Фам.6", "Дата.6",
                "Стадия", "Листов", "Компания"
            };

            var newMap = new Dictionary<string, StampField>();

            foreach (var key in keys)
            {
                string val = properties.GetValue(key);
                newMap[key] = new StampField(key, val);
            }

            // Объединяем парные поля в одно для отображения в UI
            ОбозначениеДокумента = MergeFields(
                properties.GetValue("Обозначение_документа1"),
                properties.GetValue("Обозначение_документа2"));

            НаименованиеОбъекта = MergeFields(
                properties.GetValue("Наименование_объекта1"),
                properties.GetValue("Наименование_объекта2"));

            НаименованиеУчастка = MergeFields(
                properties.GetValue("Наименование_участка1"),
                properties.GetValue("Наименование_участка2"));

            FieldsMap = newMap;
        }

        private void ExecuteUpdate(object parameter)
        {
            if (new[] { ОбозначениеДокумента, НаименованиеОбъекта, НаименованиеУчастка }
                .Any(value => (value?.Trim().Length ?? 0) > MaxFieldPartLength * 2))
            {
                System.Windows.MessageBox.Show("Значение поля штампа не должно превышать 128 символов.", "Autocad Tools");
                return;
            }
            var properties = new StampProperties();

            // Собираем данные из словаря
            foreach (var field in FieldsMap.Values)
            {
                properties.SetValue(field.Key, field.Value);
            }

            // Разбиваем объединённые поля обратно на части
            var (doc1, doc2) = SplitField(ОбозначениеДокумента);
            properties.SetValue("Обозначение_документа1", doc1);
            properties.SetValue("Обозначение_документа2", doc2);

            var (obj1, obj2) = SplitField(НаименованиеОбъекта);
            properties.SetValue("Наименование_объекта1", obj1);
            properties.SetValue("Наименование_объекта2", obj2);

            var (part1, part2) = SplitField(НаименованиеУчастка);
            properties.SetValue("Наименование_участка1", part1);
            properties.SetValue("Наименование_участка2", part2);

            _autoCADService.SaveStampProperties(properties);
        }

        /// <summary>
        /// Объединяет два значения полей в одну строку
        /// </summary>
        private string MergeFields(string part1, string part2)
        {
            if (string.IsNullOrWhiteSpace(part1)) return part2?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(part2)) return part1.Trim();
            // New splits retain separators; a full 64-character first part may split a word.
            if (part1.Length == MaxFieldPartLength || char.IsWhiteSpace(part1[part1.Length - 1]) || char.IsWhiteSpace(part2[0]))
                return (part1 + part2).Trim();
            // Compatibility with old values split into two trimmed words.
            return part1.Trim() + " " + part2.Trim();
        }

        private (string part1, string part2) SplitField(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return (" ", " ");
            value = value.Trim();
            if (value.Length > MaxFieldPartLength * 2)
                throw new ArgumentException("Значение поля превышает 128 символов.", nameof(value));
            if (value.Length <= MaxFieldPartLength) return (value, " ");
            int minimum = value.Length - MaxFieldPartLength;
            int middle = value.Length / 2;
            int split = MaxFieldPartLength;
            for (int offset = 0; offset <= MaxFieldPartLength; offset++)
            {
                int right = middle + offset;
                int left = middle - offset;
                if (right >= minimum && right <= MaxFieldPartLength && char.IsWhiteSpace(value[right - 1])) { split = right; break; }
                if (left >= minimum && left <= MaxFieldPartLength && char.IsWhiteSpace(value[left - 1])) { split = left; break; }
            }
            // Keep whitespace in the first part so a save/reload preserves the exact value.
            return (value.Substring(0, split), value.Substring(split));
        }
    }
}
