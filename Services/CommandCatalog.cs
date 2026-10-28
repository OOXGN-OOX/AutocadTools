// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AutocadTools.Services
{
    public sealed class CommandDefinition
    {
        public string English { get; }
        public string Russian { get; }
        public string Title { get; }
        public string SuggestedAlias { get; }
        public string ToolTip => Title + "\n" + English + " / " + Russian;
        public CommandDefinition(string english, string russian, string title, string suggestedAlias = "")
        { English = english; Russian = russian; Title = title; SuggestedAlias = suggestedAlias; }
    }

    public static class CommandCatalog
    {
        public static IReadOnlyList<CommandDefinition> All { get; } = Array.AsReadOnly(new[]
        {
            new CommandDefinition("ATISO", "АТАКСО", "Аксонометрия", "АКС"),
            new CommandDefinition("ATNUM", "АТНУМ", "Автонумерация", "НУМ"),
            new CommandDefinition("ATSIMILAR", "АТПОДОБНЫЕ", "Выбор по образцу", ""),
            new CommandDefinition("ATSUM", "АТСУММА", "Сумма длин и площадей", ""),
            new CommandDefinition("ATMCOPY", "АТМКОП", "Мультикопирование", "МК"),
            new CommandDefinition("ATNCOPY", "АТНКОП", "Копирование с нумерацией"),
            new CommandDefinition("ATSECTION", "АТРАЗРЕЗ", "Обозначение разреза"),
            new CommandDefinition("ATSYMBOL", "АТУСЛОВНЫЕ", "Условные обозначения: оформление чертежей"),
            new CommandDefinition("ATBREAK", "АТРАЗРЫВ", "Обозначение обрыва / разрыва"),
            new CommandDefinition("ATPLAN", "АТПЛАН", "Подготовка плана", ""),
            new CommandDefinition("ATCLEAN", "АТОЧИСТКА", "Очистка чертежа", ""),
            new CommandDefinition("ATDELETEOTHERS", "АТУДАЛИТЬОСТАЛЬНОЕ", "Удаление невыбранных", ""),
            new CommandDefinition("ATTEXTRESET", "АТСБРОСТЕКСТА", "Сброс оформления текста", ""),
            new CommandDefinition("ATFRAME", "АТРАМКА", "Вставка форматки", "ФМ"),
            new CommandDefinition("ATSTAMP", "АТШТАМП", "Редактирование штампа", "ШТ"),
            new CommandDefinition("ATPLOT", "АТПЕЧАТЬ", "Пакетная печать", "ПП"),
            new CommandDefinition("ATHANDOFF", "АТПЕРЕДАЧА", "Подготовка DWG для передачи", ""),
            new CommandDefinition("ATEXCEL", "АТЭКСЕЛЬ", "Таблица через Excel", ""),
            new CommandDefinition("ATEXPORTEXCEL", "АТЭКСПОРТЭКСЕЛЬ", "Экспорт таблицы в Excel"),
            new CommandDefinition("ATIMPORTEXCEL", "АТИМПОРТЭКСЕЛЬ", "Импорт таблицы из Excel"),
            new CommandDefinition("ATLIBRARY", "АТБИБЛИОТЕКА", "Дополнительные функции LISP"),
            new CommandDefinition("ATCLASSIC", "АТМЕНЮ", "Классическое меню и панель"),
            new CommandDefinition("ATSETTINGS", "АТНАСТРОЙКИ", "Настройки инструментов"),
            new CommandDefinition("ATINFO", "АТИНФО", "Информация и настройки", ""),
            new CommandDefinition("ATUPDATE", "АТОБНОВЛЕНИЯ", "Проверка обновлений", ""),
            new CommandDefinition("ATALIASES", "АТСОКРАЩЕНИЯ", "Команды и сокращения")
        });
        public static CommandDefinition Find(string name) => All.FirstOrDefault(c =>
            string.Equals(c.English, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Russian, name, StringComparison.OrdinalIgnoreCase));
    }
}
