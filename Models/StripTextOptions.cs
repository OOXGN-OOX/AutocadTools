// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

namespace AutocadTools.Models
{
    public class StripTextOptions
    {
        // Форматирование внутри текста (MText codes)
        public bool Alignment { get; set; } = true;
        public bool Color { get; set; } = true;
        public bool Font { get; set; } = true;
        public bool Height { get; set; } = true;
        public bool LineFeed { get; set; } = true;
        public bool NonBreakingSpace { get; set; } = true;
        public bool Oblique { get; set; } = true;
        public bool Overline { get; set; } = true;
        public bool Paragraph { get; set; } = true;
        public bool Stacking { get; set; } = false;
        public bool Tabs { get; set; } = true;
        public bool Tracking { get; set; } = true;
        public bool Underline { get; set; } = true;
        public bool Width { get; set; } = false;

        // Свойства объектов (Entity properties)
        public bool BackgroundMask { get; set; } = true;
        public bool Fields { get; set; } = false;        // Конвертация полей в текст
        public bool Columns { get; set; } = true;
        public bool Frame { get; set; } = true;          // Скрытие рамки текста

        public bool RememberSettings { get; set; } = true;
    }
}