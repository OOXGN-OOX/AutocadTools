// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

namespace AutocadTools.Models
{
    public class SelectSimilarOptions
    {
        // Общие настройки
        public bool AddSourceObjects { get; set; } = true;
        public bool MatchType { get; set; } = true;
        public bool MatchLayer { get; set; } = true;
        public bool MatchColor { get; set; } = false;
        public bool MatchLinetype { get; set; } = false;
        public bool MatchLinetypeScale { get; set; } = false;
        public bool MatchLineWeight { get; set; } = false;

        // Блоки
        public bool BlockName { get; set; } = true;
        public bool BlockScale { get; set; } = false;
        public bool BlockAnyAttribute { get; set; } = false; // Совпадение любого атрибута
        public bool BlockAllAttributes { get; set; } = false; // Совпадение всех атрибутов

        // Мультивыноски
        public bool MLeaderContent { get; set; } = false;

        // Растровые изображения
        public bool ImagePath { get; set; } = true;

        // Отрезки
        public bool LineLength { get; set; } = false;

        // Размеры
        public bool DimValue { get; set; } = false;
        public bool DimTextOverride { get; set; } = false;
        public bool DimTextRotation { get; set; } = false;
        public bool DimStyle { get; set; } = true;

        // Окружности
        public bool CircleRadius { get; set; } = false;

        // Однострочный текст (DBText)
        public bool TextContent { get; set; } = false;
        public bool TextStyle { get; set; } = true;
        public bool TextHeight { get; set; } = false;
        public bool TextLength { get; set; } = false; // Кол-во символов

        // МТекст (MText)
        public bool MTextContent { get; set; } = false;
        public bool MTextStyle { get; set; } = true;

        // Штриховки
        public bool HatchPattern { get; set; } = true;
        public bool HatchScale { get; set; } = false;

        // Полилинии
        public bool PolylineLength { get; set; } = false;
        public bool PolylineGlobalWidth { get; set; } = false;

        public bool RememberSettings { get; set; } = true;
    }
}