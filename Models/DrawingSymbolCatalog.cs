// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AutocadTools.Models
{
    public sealed class DrawingSymbolDefinition
    {
        public DrawingSymbolKind Kind { get; }
        public string Title { get; }
        public string Group { get; }
        public string Standard { get; }
        public string SamplePath { get; }
        public string SampleText { get; }
        public DrawingSymbolDefinition(DrawingSymbolKind kind, string title, string group, string standard, string path, string text)
        { Kind = kind; Title = title; Group = group; Standard = standard; SamplePath = path; SampleText = text; }
        public override string ToString() => Title;
        public DrawingSymbolOptions Defaults()
        {
            var options = new DrawingSymbolOptions { Kind = Kind, TextHeight = 3.5 };
            if (options.IsSection) { options.Mark = "А"; options.TextHeight = 5; }
            else if (options.IsBreak) options.Mark = "а";
            else if (options.IsLevel) options.Mark = "+3,600";
            else if (options.IsSlope) options.Mark = Kind == DrawingSymbolKind.SlopeArrow ? "2%" : "0,02";
            else if (options.IsLeader) options.Mark = "Трубопровод В1";
            else if (Kind == DrawingSymbolKind.Fragment) options.Mark = "Фрагмент 1";
            else if (Kind == DrawingSymbolKind.ImageTitle) options.Mark = "Разрез А—А";
            else if (Kind >= DrawingSymbolKind.DetailCircle && Kind <= DrawingSymbolKind.DetailTitle) options.Mark = "1";
            if (Kind >= DrawingSymbolKind.DetailCircle && Kind <= DrawingSymbolKind.DetailRectangle) options.Diameter = 20;
            return options;
        }
    }

    public static class DrawingSymbolCatalog
    {
        public static IReadOnlyList<DrawingSymbolDefinition> All { get; } = Array.AsReadOnly(new[]
        {
            new DrawingSymbolDefinition(DrawingSymbolKind.Section, "Прямой разрез", "Разрезы и виды", "ГОСТ Р 21.101, п. 5.5.2", "M15,45 H50 M150,45 H185 M25,80 V45 M22,52 L25,45 28,52 M175,80 V45 M172,52 L175,45 178,52", "А                       А"),
            new DrawingSymbolDefinition(DrawingSymbolKind.SteppedSection, "Ступенчатый разрез", "Разрезы и виды", "ГОСТ Р 21.101, п. 5.5.2", "M15,35 H50 M85,35 H100 V65 H115 M150,65 H185 M25,65 V35 M22,42 L25,35 28,42 M175,95 V65 M172,72 L175,65 178,72", "А                       А"),
            new DrawingSymbolDefinition(DrawingSymbolKind.ViewDirection, "Направление вида", "Разрезы и виды", "ГОСТ Р 21.101, п. 5.5.5", "M40,60 H160 M150,55 L160,60 150,65", "А"),
            new DrawingSymbolDefinition(DrawingSymbolKind.Zigzag, "Линия с изломом", "Обрывы и разрывы", "ГОСТ 2.303, таблица 1; дополнительная надпись", "M15,60 H90 L95,42 105,78 110,60 H185", "а     а"),
            new DrawingSymbolDefinition(DrawingSymbolKind.Wave, "Волнистая линия", "Обрывы и разрывы", "ГОСТ 2.303, таблица 1", "M15,60 C25,45 35,75 45,60 S65,75 75,60 S95,75 105,60 S125,75 135,60 S155,75 165,60 S180,70 185,60", "а"),
            new DrawingSymbolDefinition(DrawingSymbolKind.SchemeBreak, "Разрыв на схеме ОВ / ВК", "Обрывы и разрывы", "ГОСТ 21.602, п. 6.2.3; ГОСТ 21.601, п. 6.2.4", "M15,60 H50 M150,60 H185 M54,60 H56 M64,60 H66 M74,60 H76 M84,60 H86 M94,60 H96 M104,60 H106 M114,60 H116 M124,60 H126 M134,60 H136 M144,60 H146", "а                       а"),
            new DrawingSymbolDefinition(DrawingSymbolKind.DetailCircle, "Узел: круг и полка", "Узлы и фрагменты", "ГОСТ Р 21.101, п. 5.5.4, рисунок 13", "M20,65 A25,25 0 1 0 70,65 A25,25 0 1 0 20,65 M64,48 L95,25 H180", "1 (5)"),
            new DrawingSymbolDefinition(DrawingSymbolKind.DetailOval, "Узел: овал и полка", "Узлы и фрагменты", "ГОСТ Р 21.101, п. 5.5.4, рисунок 13", "M15,65 A35,20 0 1 0 85,65 A35,20 0 1 0 15,65 M75,51 L105,25 H185", "1 (5)"),
            new DrawingSymbolDefinition(DrawingSymbolKind.DetailRectangle, "Узел: скруглённая рамка", "Узлы и фрагменты", "ГОСТ Р 21.101, п. 5.5.4, рисунок 13", "M20,45 H70 Q75,45 75,50 V80 Q75,85 70,85 H20 Q15,85 15,80 V50 Q15,45 20,45 M75,50 L105,25 H185", "1 (5)"),
            new DrawingSymbolDefinition(DrawingSymbolKind.DetailTitle, "Номер узла над изображением", "Узлы и фрагменты", "ГОСТ Р 21.101, п. 5.5.4, рисунок 14", "M70,60 A30,30 0 1 0 130,60 A30,30 0 1 0 70,60 M70,60 H130", "1 / 5"),
            new DrawingSymbolDefinition(DrawingSymbolKind.Fragment, "Фрагмент: фигурная скобка", "Узлы и фрагменты", "ГОСТ Р 21.101, п. 5.5.7, рисунок 16", "M15,40 Q15,50 25,50 H90 Q98,50 100,60 Q102,50 110,50 H175 Q185,50 185,40", "Фрагмент 1"),
            new DrawingSymbolDefinition(DrawingSymbolKind.Elevation, "Высотная отметка на разрезе", "Отметки и уклоны", "ГОСТ Р 21.101, п. 5.4.3, рисунок 6", "M35,65 L45,75 55,65 M45,75 V35 H175", "+3,600"),
            new DrawingSymbolDefinition(DrawingSymbolKind.PlanElevation, "Высотная отметка на плане", "Отметки и уклоны", "ГОСТ Р 21.101, п. 5.4.3, рисунок 7", "M40,35 H160 V80 H40 Z", "+3,600"),
            new DrawingSymbolDefinition(DrawingSymbolKind.SlopeArrow, "Уклон: стрелка на плане", "Отметки и уклоны", "ГОСТ Р 21.101, п. 5.4.4, рисунок 8", "M25,65 H175 M163,60 L175,65 163,70", "2%"),
            new DrawingSymbolDefinition(DrawingSymbolKind.SlopeSign, "Уклон: знак на разрезе", "Отметки и уклоны", "ГОСТ Р 21.101, п. 5.4.4, рисунок 9", "M35,65 V50 L75,65 Z M85,65 H175", "0,02"),
            new DrawingSymbolDefinition(DrawingSymbolKind.LeaderDot, "Выноска с точкой", "Выноски и надписи", "ГОСТ Р 21.101, п. 5.4.5", "M25,85 L90,50 H185 M23,83 H27 V87 H23 Z", "Трубопровод В1"),
            new DrawingSymbolDefinition(DrawingSymbolKind.LeaderArrow, "Выноска со стрелкой", "Выноски и надписи", "ГОСТ Р 21.101, п. 5.4.5", "M25,85 L90,50 H185 M27,77 L25,85 33,83", "Воздуховод"),
            new DrawingSymbolDefinition(DrawingSymbolKind.LeaderPlain, "Выноска без наконечника", "Выноски и надписи", "ГОСТ Р 21.101, п. 5.4.5", "M25,85 L90,50 H185", "Обозначение"),
            new DrawingSymbolDefinition(DrawingSymbolKind.MultiLeader, "Общая полка двух выносок", "Выноски и надписи", "ГОСТ Р 21.101, п. 5.4.5", "M25,80 L90,45 H185 M55,90 L90,45 M23,78 H27 V82 H23 Z M53,88 H57 V92 H53 Z", "Трубопроводы В1"),
            new DrawingSymbolDefinition(DrawingSymbolKind.ImageTitle, "Наименование изображения", "Выноски и надписи", "ГОСТ Р 21.101, п. 5.5.14–5.5.16", "", "Разрез А—А (5)"),
            new DrawingSymbolDefinition(DrawingSymbolKind.CoordinateAxis, "Координационная ось", "Координационные оси", "ГОСТ Р 21.101, п. 5.3.2", "M20,65 A20,20 0 1 0 60,65 A20,20 0 1 0 20,65 M60,65 H100 M108,65 H110 M118,65 H158 M166,65 H168 M176,65 H190", "А")
        });
        public static DrawingSymbolDefinition Get(DrawingSymbolKind kind) => All.Single(d => d.Kind == kind);
    }
}
