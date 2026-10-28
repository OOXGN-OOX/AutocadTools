// SPDX-License-Identifier: AGPL-3.0-only
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: BotPlusAutocadTool
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using System.Collections.Generic;

namespace AutocadTools.Services
{
    public class CleanUnselectedService
    {
        private readonly Database _db;
        private readonly Transaction _tr;

        public CleanUnselectedService(Database db, Transaction tr)
        {
            _db = db;
            _tr = tr;
        }

        public int Process(HashSet<ObjectId> keepIds, bool cleanLayouts)
        {
            int deletedCount = 0;
            BlockTable bt = (BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForRead);

            // 1. Проход по всем пространствам (Model + Layouts)
            foreach (ObjectId btrId in bt)
            {
                BlockTableRecord btr = (BlockTableRecord)_tr.GetObject(btrId, OpenMode.ForRead);

                // Определяем тип пространства
                bool isModel = btr.Name.Equals(BlockTableRecord.ModelSpace, System.StringComparison.OrdinalIgnoreCase);
                bool isPaper = btr.IsLayout;

                // Логика пропуска:
                // Если это служебный блок (не модель и не лист) -> пропускаем
                if (!isModel && !isPaper) continue;

                // Если это Лист, а пользователь отказался чистить листы -> пропускаем
                if (!isModel && isPaper && !cleanLayouts) continue;

                // Проход по объектам внутри пространства
                foreach (ObjectId entId in btr)
                {
                    // Если объект в списке "Оставить" -> пропускаем
                    if (keepIds.Contains(entId)) continue;

                    // Получаем объект
                    if (entId.IsNull || !entId.IsValid || entId.IsErased) continue;
                    Entity ent = (Entity)_tr.GetObject(entId, OpenMode.ForRead, false, forceOpenOnLockedLayer: true);
                    if (ent is Viewport viewport && viewport.Number == 1) continue;

                    // Нельзя удалять Viewport, который является самим листом (обычно первый в списке)
                    // Но проще проверить через исключение или тип, если это специфичный объект листа.
                    // Обычно Viewport #1 на листе нельзя удалить, но попытка Erase не крашит кад, а просто игнорируется или кидает ошибку.

                    try
                    {
                        // Проверка на заблокированный слой
                        ent.UpgradeOpen();
                        ent.Erase();
                        deletedCount++;
                    }
                    catch { /* Игнорируем объекты, которые нельзя удалить */ }
                }
            }

            return deletedCount;
        }

        // Встроенный Purge (аналог того, что мы делали в CleanDwgCommand)
        public int PurgeDatabase()
        {
            bool keepPurging = true;
            int totalPurged = 0;

            while (keepPurging)
            {
                keepPurging = false;
                ObjectIdCollection idsToPurge = new ObjectIdCollection();

                // Собираем из основных таблиц
                CollectTableIds(_db.BlockTableId, idsToPurge);
                CollectTableIds(_db.LayerTableId, idsToPurge);
                CollectTableIds(_db.LinetypeTableId, idsToPurge);
                CollectTableIds(_db.TextStyleTableId, idsToPurge);
                CollectTableIds(_db.DimStyleTableId, idsToPurge);

                if (idsToPurge.Count > 0)
                {
                    _db.Purge(idsToPurge); // API оставляет только то, что можно удалить

                    if (idsToPurge.Count > 0)
                    {
                        foreach (ObjectId id in idsToPurge)
                        {
                            var obj = _tr.GetObject(id, OpenMode.ForWrite);
                            obj.Erase();
                        }
                        totalPurged += idsToPurge.Count;
                        keepPurging = true; // Повторяем, вдруг освободилось что-то еще
                    }
                }
            }
            return totalPurged;
        }

        private void CollectTableIds(ObjectId tableId, ObjectIdCollection ids)
        {
            var table = (SymbolTable)_tr.GetObject(tableId, OpenMode.ForRead);
            foreach (ObjectId id in table) ids.Add(id);
        }
    }
}