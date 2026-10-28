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
    public partial class StampPropsViewModel : BaseViewModel
    {
        private readonly IAutoCADService _autoCADService;

        // Only used when reading legacy fields that were split into two parts.
        private const int MaxFieldPartLength = 64;
        public StampTypography Typography { get; private set; } = new StampTypography();

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
            Typography = (_autoCADService as IStampTypographyService)?.LoadTypography() ?? new StampTypography();
            OnPropertyChanged(nameof(Typography));

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

            ОбозначениеДокумента = ReadText(properties, "Обозначение_документа");
            НаименованиеОбъекта = ReadText(properties, "Наименование_объекта");
            НаименованиеУчастка = ReadText(properties, "Наименование_участка");
            FieldsMap = newMap;
            LoadSignatures();
        }

        private void ExecuteUpdate(object parameter)
        {
            if (new[] { ОбозначениеДокумента, НаименованиеОбъекта, НаименованиеУчастка }
                .Any(value => (value?.Trim().Length ?? 0) > 1000))
            {
                System.Windows.MessageBox.Show("Значение поля штампа не должно превышать 1000 символов.", "Autocad Tools");
                return;
            }
            var properties = CaptureValues();
            try
            {
                if (_autoCADService is IStampSignatureService signatureService)
                    signatureService.SaveStamp(properties, Signatures.Where(s => s.Value.Signature != null).ToDictionary(s => s.Key, s => s.Value.Signature));
                else _autoCADService.SaveStampProperties(properties);
            }
            catch (Exception ex) { System.Windows.MessageBox.Show("Не удалось применить штамп: " + ex.Message, "Свойства штампа"); }
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

        public StampProperties CaptureValues(bool empty = false)
        {
            if (empty)
            {
                var blank = new StampProperties();
                foreach (var key in StampSchema.Keys) blank.SetValue(key, " ");
                return blank;
            }
            if (new[] { ОбозначениеДокумента, НаименованиеОбъекта, НаименованиеУчастка }.Any(value => (value?.Trim().Length ?? 0) > 1000))
                throw new ArgumentException("Значение поля штампа не должно превышать 1000 символов.");
            var properties = new StampProperties();

            // Собираем данные из словаря
            foreach (var field in FieldsMap.Values)
            {
                properties.SetValue(field.Key, field.Value);
            }

            WriteText(properties, "Обозначение_документа", ОбозначениеДокумента);
            WriteText(properties, "Наименование_объекта", НаименованиеОбъекта);
            WriteText(properties, "Наименование_участка", НаименованиеУчастка);
            return properties;
        }

        private string ReadText(StampProperties properties, string key)
        {
            return properties.Properties.TryGetValue(key, out var value) ? value?.Trim() ?? ""
                : MergeFields(properties.GetValue(key + "1"), properties.GetValue(key + "2"));
        }

        private static void WriteText(StampProperties properties, string key, string value)
        {
            value = string.IsNullOrWhiteSpace(value) ? " " : value.Trim();
            properties.SetValue(key, value);
            // Existing DWG fields can still reference these aliases; no text is split or truncated.
            properties.SetValue(key + "1", value);
            properties.SetValue(key + "2", " ");
        }
    }
}
