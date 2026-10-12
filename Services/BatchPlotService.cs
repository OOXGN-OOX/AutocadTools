// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

// File: Services/BatchPlotService.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutocadTools.Models;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using SystemException = System.Exception;

namespace AutocadTools.Services
{
    /// <summary>
    /// Определение формата бумаги (имя + размеры в мм)
    /// </summary>
    public class PaperFormatDefinition
    {
        public string Name { get; }
        public double Width { get; }   // Ширина (мм)
        public double Height { get; }  // Высота (мм)

        public PaperFormatDefinition(string name, double width, double height)
        {
            Name = name;
            Width = width;
            Height = height;
        }

        public override string ToString()
        {
            return $"{Name} ({Width}×{Height} мм)";
        }
    }

    /// <summary>
    /// Сервис для работы с "форматками":
    /// - поиск блоков по имени/слою (настраиваемо)
    /// - определение формата по габаритам (СТРОГО по ГОСТ)
    /// - чтение/запись атрибутов (НАИМЕНОВАНИЕ/ЛИСТ)
    /// </summary>
    public class BatchPlotService
    {
        #region Фильтр (настраиваемый)

        public const string DefaultBlockName = "WM_FORMAT";
        public const string DefaultLayerName = "WM_FORMAT";

        public string BlockName { get; set; } = DefaultBlockName;
        public string LayerName { get; set; } = DefaultLayerName;

        #endregion

        #region Константы

        private const string TagDrawingName = "НАИМЕНОВАНИЕ";
        private const string TagSheetNumber = "ЛИСТ";
        private const double ToleranceMm = 5.0;     // Допуск ±5мм для определения формата
        private const double RowToleranceMm = 10.0; // Допуск по Y для объединения в одну "строку"

        #endregion

        #region Определения форматов (СТРОГО по ГОСТ 2.301-68)

        private readonly List<PaperFormatDefinition> _definedFormats = new List<PaperFormatDefinition>
        {
            // Стандартные форматы (АЛЬБОМНАЯ ориентация: Width > Height)
            new PaperFormatDefinition("A0", 1189, 841),
            new PaperFormatDefinition("A1", 841, 594),
            new PaperFormatDefinition("A2", 594, 420),
            new PaperFormatDefinition("A3", 420, 297),
            new PaperFormatDefinition("A4", 297, 210),

            // Кратные A0
            new PaperFormatDefinition("A0x2", 1682, 1189),
            new PaperFormatDefinition("A0x3", 2523, 1189),

            // Кратные A1
            new PaperFormatDefinition("A1x3", 1783, 841),
            new PaperFormatDefinition("A1x4", 2378, 841),

            // Кратные A2
            new PaperFormatDefinition("A2x3", 1261, 594),
            new PaperFormatDefinition("A2x4", 1682, 594),
            new PaperFormatDefinition("A2x5", 2102, 594),

            // Кратные A3
            new PaperFormatDefinition("A3x3", 891, 420),
            new PaperFormatDefinition("A3x4", 1189, 420),
            new PaperFormatDefinition("A3x5", 1486, 420),
            new PaperFormatDefinition("A3x6", 1783, 420),
            new PaperFormatDefinition("A3x7", 2080, 420),

            // Кратные A4
            new PaperFormatDefinition("A4x3", 630, 297),
            new PaperFormatDefinition("A4x4", 841, 297),
            new PaperFormatDefinition("A4x5", 1051, 297),
            new PaperFormatDefinition("A4x6", 1261, 297),
            new PaperFormatDefinition("A4x7", 1471, 297),
            new PaperFormatDefinition("A4x8", 1682, 297),
            new PaperFormatDefinition("A4x9", 1892, 297)
        };

        #endregion

        #region Публичные методы

        /// <summary>
        /// Получить список имён всех определённых форматов
        /// </summary>
        public List<string> GetDefinedFormatNames()
        {
            var list = new List<string>();

            // Стандартные форматы (A0-A4) в двух ориентациях
            var standardFormats = new[] { "A0", "A1", "A2", "A3", "A4" };
            foreach (var fmt in standardFormats)
            {
                list.Add(fmt);        // Альбомная
                list.Add($"{fmt} v"); // Книжная
            }

            // Кратные форматы (без " v", т.к. всегда альбомные)
            var multipleFormats = _definedFormats
                .Where(f => f.Name.Contains("x"))
                .Select(f => f.Name)
                .OrderBy(n => n);
            list.AddRange(multipleFormats);

            // Нестандартный
            list.Add("Нестандартный");

            return list;
        }

        /// <summary>
        /// Получить определение формата по имени (БЕЗ учёта " v")
        /// </summary>
        public PaperFormatDefinition GetFormatDefinition(string formatName)
        {
            string baseName = formatName.Replace(" v", "").Trim();

            return _definedFormats.FirstOrDefault(f =>
                f.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Сканирование Model Space на наличие блоков-форматок
        /// </summary>
        public List<BatchPlotItem> GetFormatBlocks()
        {
            var items = new List<BatchPlotItem>();
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;

            if (doc == null)
            {
                LogError("No active document");
                return items;
            }

            Database db = doc.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    int scanned = 0;
                    int matched = 0;

                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased || id.IsNull || id.ObjectClass.DxfName != "INSERT")
                            continue;

                        scanned++;

                        var blk = (BlockReference)tr.GetObject(id, OpenMode.ForRead);

                        // Фильтр: по имени блока и слою
                        string effectiveName = GetEffectiveName(blk, tr);
                        if (!effectiveName.Equals(BlockName, StringComparison.OrdinalIgnoreCase) ||
                            !blk.Layer.Equals(LayerName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        matched++;

                        // Получаем границы блока
                        Extents3d ext;
                        try
                        {
                            ext = blk.GeometricExtents;
                        }
                        catch (SystemException ex)
                        {
                            doc.Editor.WriteMessage($"\n[Autocad Tools Warning] Блок {id.Handle} имеет невалидные границы: {ex.Message}\n");
                            continue;
                        }

                        // ЦЕЛЫЕ размеры (округление до мм)
                        double blockWidth = Math.Abs(Math.Round(ext.MaxPoint.X - ext.MinPoint.X, 0));
                        double blockHeight = Math.Abs(Math.Round(ext.MaxPoint.Y - ext.MinPoint.Y, 0));

                        doc.Editor.WriteMessage($"\n[DEBUG] Блок {id.Handle}: размеры {blockWidth:F0}×{blockHeight:F0} мм\n");

                        string detectedFormat = IdentifyFormatBySize(blockWidth, blockHeight, out PaperFormatDefinition matchedDef);

                        if (matchedDef != null)
                        {
                            doc.Editor.WriteMessage($"[DEBUG] → Определён формат: {detectedFormat} (ГОСТ: {matchedDef.Width}×{matchedDef.Height})\n");
                        }
                        else
                        {
                            doc.Editor.WriteMessage($"[DEBUG] → Нестандартный формат!\n");
                        }

                        // Читаем атрибуты
                        var attrs = GetAttributes(blk, tr);
                        string sheetVal = attrs.TryGetValue(TagSheetNumber.ToUpper(), out string sheet) ? sheet : "";
                        string nameVal = attrs.TryGetValue(TagDrawingName.ToUpper(), out string name) ? name : "";

                        // Создаём элемент для таблицы
                        var item = new BatchPlotItem
                        {
                            BlockId = id,
                            Extents = ext,
                            Name = nameVal,
                            SheetNumber = sheetVal,
                            Format = detectedFormat,
                            IsSelected = true,
                            Status = "Готов",
                            FormatDefinition = matchedDef
                        };

                        items.Add(item);
                    }

                    tr.Commit();

                    doc.Editor.WriteMessage($"\n[Autocad Tools] Просканировано: {scanned} блоков, найдено форматок: {matched}\n");
                }
                catch (SystemException ex)
                {
                    tr.Abort();
                    LogError($"Error scanning blocks: {ex.Message}");
                    doc.Editor.WriteMessage($"\n[Autocad Tools Error] Ошибка поиска блоков: {ex.Message}\n");
                }
            }

            // НОВОЕ: сортировка по расположению — сверху вниз, слева направо
            return SortLeftRightTopBottom(items, RowToleranceMm);
        }

        /// <summary>
        /// Обновление атрибутов блоков в чертеже
        /// </summary>
        public void UpdateBlockAttributes(List<BatchPlotItem> items)
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                try
                {
                    int count = 0;
                    int skipped = 0;

                    foreach (var item in items)
                    {
                        if (item.BlockId.IsNull || item.BlockId.IsErased)
                        {
                            skipped++;
                            continue;
                        }

                        var blkRef = tr.GetObject(item.BlockId, OpenMode.ForWrite) as BlockReference;
                        if (blkRef == null || blkRef.AttributeCollection.Count == 0)
                        {
                            skipped++;
                            continue;
                        }

                        foreach (ObjectId attId in blkRef.AttributeCollection)
                        {
                            if (attId.IsErased) continue;

                            var attRef = tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;
                            if (attRef == null) continue;

                            string tag = attRef.Tag.ToUpper();

                            if (tag == TagDrawingName.ToUpper())
                                attRef.TextString = item.Name ?? "";
                            else if (tag == TagSheetNumber.ToUpper())
                                attRef.TextString = item.SheetNumber ?? "";
                        }

                        count++;
                    }

                    tr.Commit();
                    doc.Editor.Regen();

                    MessageBox.Show(
                        $"Обновлено блоков: {count}\n" +
                        (skipped > 0 ? $"Пропущено: {skipped}" : ""),
                        "Autocad Tools",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (SystemException ex)
                {
                    tr.Abort();
                    LogError($"Update attributes failed: {ex.Message}");
                    MessageBox.Show(
                        $"Ошибка обновления атрибутов:\n\n{ex.Message}",
                        "Autocad Tools - Ошибка",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Вспомогательные методы

        /// <summary>
        /// КЛЮЧЕВОЙ МЕТОД: Определение формата по ЦЕЛЫМ размерам блока (±5мм допуск)
        /// Возвращает строку формата ("A3", "A3 v", "A4x6") и определение (для PageSetup)
        /// </summary>
        private string IdentifyFormatBySize(double blockWidth, double blockHeight, out PaperFormatDefinition matchedDefinition)
        {
            matchedDefinition = null;

            foreach (var fmt in _definedFormats)
            {
                bool matchesDirect =
                    Math.Abs(fmt.Width - blockWidth) < ToleranceMm &&
                    Math.Abs(fmt.Height - blockHeight) < ToleranceMm;

                bool matchesRotated =
                    Math.Abs(fmt.Width - blockHeight) < ToleranceMm &&
                    Math.Abs(fmt.Height - blockWidth) < ToleranceMm;

                if (matchesDirect)
                {
                    matchedDefinition = fmt;

                    if (fmt.Name.Contains("x"))
                        return fmt.Name;

                    bool isPortrait = blockHeight > blockWidth;
                    return isPortrait ? $"{fmt.Name} v" : fmt.Name;
                }
                else if (matchesRotated)
                {
                    matchedDefinition = fmt;

                    if (fmt.Name.Contains("x"))
                        return fmt.Name;

                    return $"{fmt.Name} v";
                }
            }

            return "Нестандартный";
        }

        /// <summary>
        /// Чтение атрибутов блока
        /// </summary>
        private Dictionary<string, string> GetAttributes(BlockReference blk, Transaction tr)
        {
            var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (ObjectId id in blk.AttributeCollection)
            {
                if (id.IsErased) continue;

                var att = tr.GetObject(id, OpenMode.ForRead) as AttributeReference;
                if (att == null) continue;

                string tag = att.Tag.ToUpper();
                if (!res.ContainsKey(tag))
                    res.Add(tag, att.TextString);
            }

            return res;
        }

        /// <summary>
        /// Получение реального имени блока (с учётом динамических блоков)
        /// </summary>
        private string GetEffectiveName(BlockReference blk, Transaction tr)
        {
            if (blk.IsDynamicBlock)
            {
                var btr = tr.GetObject(blk.DynamicBlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                return btr?.Name ?? blk.Name;
            }
            return blk.Name;
        }

        private void LogError(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[Autocad Tools Error] {message}");
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            doc?.Editor.WriteMessage($"\n[Autocad Tools Log] {message}\n");
        }

        #endregion

        #region Дополнительные утилиты

        public bool BlockDefinitionExists(string blockName)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                try
                {
                    var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                    bool exists = bt.Has(blockName);
                    tr.Commit();
                    return exists;
                }
                catch
                {
                    tr.Abort();
                    return false;
                }
            }
        }

        public Dictionary<string, int> GetFormatStatistics()
        {
            var items = GetFormatBlocks();
            return items
                .GroupBy(x => x.Format)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        #endregion

        #region Сортировка по расположению

        /// <summary>
        /// Сортировка форматок "сверху вниз, слева направо".
        /// Опорная точка — левый нижний угол рамки (Extents.MinPoint).
        /// </summary>
        private static List<BatchPlotItem> SortLeftRightTopBottom(
            List<BatchPlotItem> items,
            double rowTolerance)
        {
            if (items == null || items.Count <= 1)
                return items ?? new List<BatchPlotItem>();

            // Грубая сортировка: сверху вниз (Y desc), слева направо (X asc)
            var sorted = items
                .OrderByDescending(i => i.Extents.MinPoint.Y)
                .ThenBy(i => i.Extents.MinPoint.X)
                .ToList();

            var rows = new List<List<BatchPlotItem>>();

            foreach (var it in sorted)
            {
                double y = it.Extents.MinPoint.Y;

                if (rows.Count == 0)
                {
                    rows.Add(new List<BatchPlotItem> { it });
                    continue;
                }

                double baseY = rows[^1][0].Extents.MinPoint.Y;

                // если по Y в пределах допуска — та же строка, иначе новая строка
                if (Math.Abs(y - baseY) <= rowTolerance)
                    rows[^1].Add(it);
                else
                    rows.Add(new List<BatchPlotItem> { it });
            }

            // Внутри строки — слева направо
            foreach (var row in rows)
                row.Sort((a, b) => a.Extents.MinPoint.X.CompareTo(b.Extents.MinPoint.X));

            return rows.SelectMany(r => r).ToList();
        }

        #endregion
    }
}
