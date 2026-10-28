// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace AutocadTools.Services
{
    internal static class StampSchema
    {
        internal static IEnumerable<string> Keys
        {
            get
            {
                foreach (var key in new[] { "Взам.инв.№", "Подп.и.дата", "Инв.№", "Стадия", "Листов", "Компания",
                    "Обозначение_документа", "Наименование_объекта", "Наименование_участка",
                    "Обозначение_документа1", "Обозначение_документа2", "Наименование_объекта1", "Наименование_объекта2", "Наименование_участка1", "Наименование_участка2" }) yield return key;
                for (int i = 1; i <= 4; i++) foreach (var prefix in new[] { "Изм.", "Кол.уч.", "Лист.", "Nдок." }) yield return prefix + i;
                for (int i = 1; i <= 6; i++) { yield return "Должн." + i; yield return "Фам." + i; }
                for (int i = 1; i <= 10; i++) yield return "Дата." + i;
            }
        }

        internal static void EnsureProperties(Database database)
        {
            var builder = new DatabaseSummaryInfoBuilder(database.SummaryInfo);
            var properties = builder.CustomPropertyTable;
            bool changed = false;
            foreach (var key in new[] { "Обозначение_документа", "Наименование_объекта", "Наименование_участка" })
            {
                if (properties.Contains(key)) continue;
                string first = properties.Contains(key + "1") ? properties[key + "1"]?.ToString() ?? "" : "";
                string second = properties.Contains(key + "2") ? properties[key + "2"]?.ToString() ?? "" : "";
                string separator = first.Length != 64 && first.Length > 0 && second.Length > 0 && !char.IsWhiteSpace(first[first.Length - 1]) && !char.IsWhiteSpace(second[0]) ? " " : "";
                string value = (first + separator + second).Trim();
                properties.Add(key, string.IsNullOrWhiteSpace(value) ? " " : value);
                changed = true;
            }
            foreach (var key in Keys)
            {
                if (!properties.Contains(key)) { properties.Add(key, " "); changed = true; }
                else if (string.IsNullOrWhiteSpace(properties[key]?.ToString())) { properties[key] = " "; changed = true; }
            }
            if (changed) database.SummaryInfo = builder.ToDatabaseSummaryInfo();
        }
    }
}
