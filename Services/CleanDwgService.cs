// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    public class CleanDwgService
    {
        private readonly CleanDwgOptions _options;
        private readonly Database _db;
        private readonly Transaction _tr;
        private readonly Editor _ed;

        public CleanDwgService(CleanDwgOptions options, Document doc, Transaction tr)
        {
            _options = options;
            _db = doc.Database;
            _tr = tr;
            _ed = doc.Editor;
        }

        public void Process()
        {
            int count = 0;

            if (_options.Points || _options.Wipeouts || _options.BlankText)
            {
                count += CleanVisualObjects();
            }

            if (_options.Groups)
            {
                count += CleanGroups();
            }

            if (_options.LayerFilters || _options.LayerReconcile)
            {
                CleanLayers();
            }

            if (_options.PageSetups)
            {
                CleanPageSetups();
            }

            if (_options.RegApps)
            {
                CleanRegApps();
            }

            if (_options.ScaleList)
            {
                CleanScaleList();
            }

            CleanDictionaries();

            if (_options.SolidHistory || _options.Constraints)
            {
                CleanSolidsAndConstraints();
            }
        }

        private int CleanVisualObjects()
        {
            int deleted = 0;
            BlockTable bt = (BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForRead);

            foreach (ObjectId btrId in bt)
            {
                BlockTableRecord btr = (BlockTableRecord)_tr.GetObject(btrId, OpenMode.ForRead);
                foreach (ObjectId entId in btr)
                {
                    Entity ent = (Entity)_tr.GetObject(entId, OpenMode.ForRead);
                    bool toDelete = false;

                    if (_options.Points && ent is DBPoint) toDelete = true;
                    if (_options.Wipeouts && ent is Wipeout) toDelete = true;

                    if (_options.BlankText)
                    {
                        if (ent is DBText txt && string.IsNullOrWhiteSpace(txt.TextString)) toDelete = true;
                        if (ent is MText mtxt && string.IsNullOrWhiteSpace(mtxt.Text)) toDelete = true;
                    }

                    if (toDelete)
                    {
                        ent.UpgradeOpen();
                        ent.Erase();
                        deleted++;
                    }
                }
            }
            if (deleted > 0) _ed.WriteMessage($"\nУдалено визуальных объектов: {deleted}");
            return deleted;
        }

        private int CleanGroups()
        {
            int deleted = 0;
            DBDictionary groupDict = (DBDictionary)_tr.GetObject(_db.GroupDictionaryId, OpenMode.ForRead);
            foreach (var entry in groupDict)
            {
                var grp = _tr.GetObject(entry.Value, OpenMode.ForWrite);
                grp.Erase();
                deleted++;
            }
            if (deleted > 0) _ed.WriteMessage($"\nУдалено групп: {deleted}");
            return deleted;
        }

        private void CleanLayers()
        {
            LayerTable lt = (LayerTable)_tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId layId in lt)
            {
                LayerTableRecord lay = (LayerTableRecord)_tr.GetObject(layId, OpenMode.ForRead);

                if (_options.LayerReconcile)
                {
                    if (lay.ExtensionDictionary != ObjectId.Null)
                    {
                        var extDict = (DBDictionary)_tr.GetObject(lay.ExtensionDictionary, OpenMode.ForWrite);
                        if (extDict.Contains("ADSK_XREC_LAYER_RECONCILED"))
                        {
                            extDict.Remove("ADSK_XREC_LAYER_RECONCILED");
                        }
                    }
                }
            }

            if (_options.LayerFilters)
            {
                var nod = (DBDictionary)_tr.GetObject(_db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                string[] filters = { "ACLYDICTIONARY", "ACAD_LAYERFILTERS", "ACAD_LAYERSTATES" };
                foreach (var name in filters)
                {
                    if (nod.Contains(name))
                    {
                        nod.Remove(name);
                        _ed.WriteMessage($"\nУдален словарь фильтров: {name}");
                    }
                }
            }
        }

        private void CleanPageSetups()
        {
            int deleted = 0;
            DBDictionary plotDict = (DBDictionary)_tr.GetObject(_db.PlotSettingsDictionaryId, OpenMode.ForRead);
            foreach (var entry in plotDict)
            {
                var ps = (PlotSettings)_tr.GetObject(entry.Value, OpenMode.ForWrite);
                ps.Erase();
                deleted++;
            }
            if (deleted > 0) _ed.WriteMessage($"\nУдалено настроек печати: {deleted}");
        }

        private void CleanRegApps()
        {
            RegAppTable rat = (RegAppTable)_tr.GetObject(_db.RegAppTableId, OpenMode.ForWrite);
            int deleted = 0;
            foreach (ObjectId id in rat)
            {
                RegAppTableRecord regApp = (RegAppTableRecord)_tr.GetObject(id, OpenMode.ForRead);
                string name = regApp.Name.ToUpper();
                if (name == "ACAD" || name.StartsWith("ACAD_")) continue;

                try
                {
                    regApp.UpgradeOpen();
                    regApp.Erase();
                    deleted++;
                }
                catch { }
            }
            if (deleted > 0) _ed.WriteMessage($"\nУдалено RegApps: {deleted}");
        }

        private void CleanScaleList()
        {
            var nod = (DBDictionary)_tr.GetObject(_db.NamedObjectsDictionaryId, OpenMode.ForWrite);
            if (nod.Contains("ACAD_SCALELIST"))
            {
                nod.Remove("ACAD_SCALELIST");
                _ed.WriteMessage("\nСброшен список масштабов (Annotation Scales).");
            }
        }

        private void CleanDictionaries()
        {
            var nod = (DBDictionary)_tr.GetObject(_db.NamedObjectsDictionaryId, OpenMode.ForWrite);

            if (_options.VbaMacros && nod.Contains("ACAD_VBA"))
            {
                nod.Remove("ACAD_VBA");
                _ed.WriteMessage("\nУдалены VBA макросы.");
            }

            if (_options.DgnLineStyles && nod.Contains("ACAD_DGNLINESTYLECOMP"))
            {
                nod.Remove("ACAD_DGNLINESTYLECOMP");
                _ed.WriteMessage("\nУдалены стили линий DGN.");
            }

            if (_options.Dictionaries)
            {
                foreach (var entry in nod)
                {
                    string name = entry.Key.ToUpper();
                    if (name.StartsWith("ACAD_") || name.StartsWith("ACDB")) continue;

                    try
                    {
                        nod.Remove(entry.Key);
                        _ed.WriteMessage($"\nУдален словарь: {entry.Key}");
                    }
                    catch { }
                }
            }
        }

        private void CleanSolidsAndConstraints()
        {
            int histDel = 0;
            // Переменная constrDel удалена (CS0219)

            BlockTable bt = (BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId btrId in bt)
            {
                BlockTableRecord btr = (BlockTableRecord)_tr.GetObject(btrId, OpenMode.ForRead);
                if (btr.IsLayout)
                {
                    foreach (ObjectId entId in btr)
                    {
                        Entity ent = (Entity)_tr.GetObject(entId, OpenMode.ForRead);

                        // История Solid (3DSolid History)
                        if (_options.SolidHistory && ent is Solid3d solid)
                        {
                            // ИСПРАВЛЕНИЕ: HistoryRep не существует. Используем RecordHistory.
                            if (solid.RecordHistory)
                            {
                                solid.UpgradeOpen();
                                solid.RecordHistory = false; // Отключаем запись истории
                                histDel++;
                            }
                        }

                        // Параметрические зависимости (Constraints)
                        if (_options.Constraints && ent.ExtensionDictionary != ObjectId.Null)
                        {
                            // Логика очистки зависимостей сложна без AssocAPI, пока пропускаем
                        }
                    }
                }
            }
            if (histDel > 0) _ed.WriteMessage($"\nОтключена история у {histDel} тел.");
        }
    }
}