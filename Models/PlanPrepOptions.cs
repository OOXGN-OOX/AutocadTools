// SPDX-License-Identifier: AGPL-3.0-only
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: BotPlusAutocadTool
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
namespace AutocadTools.Models
{
    public class PlanPrepOptions
    {
        public bool ExplodeAllBlocks { get; set; } = true;      // 1. Взорвать все блоки
        public bool ExplodeHatches { get; set; } = true;        // 2. Взорвать штриховки
        public bool DeleteSolidHatches { get; set; } = true;    // 3. Удалить Solid штриховки

        // НОВЫЕ ОПЦИИ
        public bool DeleteWipeouts { get; set; } = true;        // 4. Удалить маскировки
        public bool DeletePoints { get; set; } = true;          // 5. Удалить точки

        public bool JoinLines { get; set; } = true;             // 6. Линии в полилинии
        public bool DeleteShortSegments { get; set; } = true;   // 7. Удалить короткие
        public bool SetGrayColor { get; set; } = true;          // 8. Цвет в серый
        public bool CreateBlock { get; set; } = true;           // 9. Создать блок
        public bool PickBasePoint { get; set; }
    }
}
