// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    public sealed class NumberingService
    {
        #region Numbering Processing

        public int Number(Database db, Editor ed, IEnumerable<ObjectId> selectionSet, AutoNumSettings settings)
        {
            settings.Normalize();
            int numberedCount = 0;
            int currentNumber = settings.StartNumber;

            using var tr = db.TransactionManager.StartTransaction();

            try
            {
                // Собираем объекты для нумерации
                var (items, skippedCount, skippedReason) = CollectItems(tr, selectionSet, settings);

                if (items.Count == 0)
                {
                    ed.WriteMessage("\nНе найдено объектов для нумерации.");
                    if (skippedCount > 0)
                        ed.WriteMessage($" Пропущено: {skippedCount} ({skippedReason}).");
                    return 0;
                }

                if (skippedCount > 0)
                    ed.WriteMessage($"\nПредупреждение: пропущено объектов: {skippedCount} ({skippedReason}).");

                // Сортируем если выбран геометрический порядок
                if (settings.Order == AutoNumSettings.NumberingOrder.Geometric)
                    items = SortItems(items, settings);

                // Validate the complete sequence before mutating native table content.
                long finalNumber = settings.StartNumber + (long)(items.Count - 1) * settings.Step;
                if (finalNumber < int.MinValue || finalNumber > int.MaxValue) throw new OverflowException("Диапазон номеров превышает предел целого числа.");

                // Применяем нумерацию
                for (int index = 0; index < items.Count; index++)
                {
                    var item = items[index];
                    if (ApplyNumber(tr, item, settings.FormatNumber(currentNumber), ed, settings))
                    {
                        numberedCount++;
                        if (index < items.Count - 1) currentNumber = checked(currentNumber + settings.Step);
                    }
                }

                tr.Commit();
            }
            catch
            {
                tr.Abort();
                throw;
            }

            return numberedCount;
        }

        private (List<NumberingItem> items, int skippedCount, string skippedReason) CollectItems(
            Transaction tr, IEnumerable<ObjectId> selectionSet, AutoNumSettings settings)
        {
            var items = new List<NumberingItem>();
            int skippedCount = 0;
            string skippedReason = string.Empty;

            foreach (var id in selectionSet.Distinct())
            {
                if (id.IsNull || !id.IsValid || id.IsErased) continue;

                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                var item = CreateNumberingItem(tr, id, ent, settings, ref skippedCount, ref skippedReason);
                if (item != null)
                    items.Add(item);
            }

            return (items, skippedCount, skippedReason);
        }

        private NumberingItem CreateNumberingItem(Transaction tr, ObjectId objectId, Entity ent,
            AutoNumSettings settings, ref int skippedCount, ref string skippedReason)
        {
            return settings.Mode switch
            {
                AutoNumSettings.NumberingMode.Text => CreateTextItem(objectId, ent, ref skippedCount, ref skippedReason),
                AutoNumSettings.NumberingMode.MLeader => CreateMLeaderItem(objectId, ent, ref skippedCount, ref skippedReason),
                AutoNumSettings.NumberingMode.Attribute => CreateAttributeItem(tr, ent, settings.AttributeTag, ref skippedCount, ref skippedReason),
                AutoNumSettings.NumberingMode.Table when ent is Table table => CreateTableItem(tr, objectId, table, settings),
                _ => null
            };
        }

        private NumberingItem CreateTableItem(Transaction tr, ObjectId id, Table table, AutoNumSettings settings)
        {
            int row = settings.TableRow - 1, column = settings.TableColumn - 1;
            if (row >= table.Rows.Count || column >= table.Columns.Count)
                throw new InvalidOperationException($"В таблице {id.Handle} нет ячейки ({settings.TableRow}, {settings.TableColumn}). Изменения отменены.");
            if (table.Cells[row, column].IsMerged.GetValueOrDefault(false))
            {
                var range = table.Cells[row, column].GetMergeRange();
                if (range.TopRow != row || range.LeftColumn != column)
                    throw new InvalidOperationException("Для объединённой ячейки укажите её верхнюю левую строку и столбец.");
            }
            if (!table.Cells[row, column].IsContentEditable.GetValueOrDefault(false))
                throw new InvalidOperationException($"Ячейка таблицы {id.Handle} защищена от записи.");
            var cell = table.Cells[row, column];
            if (cell.Contents.Count > 1)
                throw new InvalidOperationException($"В указанной ячейке таблицы {id.Handle} несколько элементов. Нумерация отменена, чтобы сохранить содержимое.");
            foreach (CellContent content in cell.Contents)
                if (content.HasFormula || !content.FieldId.IsNull || !content.BlockTableRecordId.IsNull)
                    throw new InvalidOperationException($"Указанная ячейка таблицы {id.Handle} содержит поле, формулу или блок. Выберите обычную текстовую ячейку; изменения отменены.");
            // Acquire all target table write locks before the first cell is changed.
            tr.GetObject(id, OpenMode.ForWrite);
            return new NumberingItem(id, table.Position, NumberingItemType.Table);
        }

        private NumberingItem CreateTextItem(ObjectId objectId, Entity ent, ref int skippedCount, ref string skippedReason)
        {
            if (ent is DBText dbText)
            {
                return new NumberingItem(objectId, dbText.Position, NumberingItemType.DBText);
            }
            if (ent is MText mText)
            {
                return new NumberingItem(objectId, mText.Location, NumberingItemType.MText);
            }

            skippedCount++;
            skippedReason = "не является текстом";
            return null;
        }

        private NumberingItem CreateMLeaderItem(ObjectId objectId, Entity ent, ref int skippedCount, ref string skippedReason)
        {
            if (ent is not MLeader mleader)
            {
                skippedCount++;
                skippedReason = "не является мультивыноской";
                return null;
            }

            if (mleader.ContentType != ContentType.MTextContent)
            {
                skippedCount++;
                skippedReason = "мультивыноска не содержит текст";
                return null;
            }

            var position = GetMLeaderPosition(mleader);
            return new NumberingItem(objectId, position, NumberingItemType.MLeader);
        }

        private Point3d GetMLeaderPosition(MLeader mleader)
        {
            try
            {
                using var text = mleader.MText;
                return text?.Location ?? mleader.GetFirstVertex(0);
            }
            catch
            {
                return Point3d.Origin;
            }
        }

        private NumberingItem CreateAttributeItem(Transaction tr, Entity ent, string attributeTag,
            ref int skippedCount, ref string skippedReason)
        {
            if (ent is not BlockReference br)
            {
                skippedCount++;
                skippedReason = "не является блоком";
                return null;
            }

            foreach (ObjectId attId in br.AttributeCollection)
            {
                if (attId.IsErased) continue;

                if (tr.GetObject(attId, OpenMode.ForRead) is AttributeReference attRef &&
                    string.Equals(attRef.Tag, attributeTag, StringComparison.OrdinalIgnoreCase))
                {
                    return new NumberingItem(attId, attRef.Position, NumberingItemType.Attribute);
                }
            }

            skippedCount++;
            skippedReason = $"блок не содержит атрибут '{attributeTag}'";
            return null;
        }

        private bool ApplyNumber(Transaction tr, NumberingItem item, string formattedNumber, Editor ed, AutoNumSettings settings)
        {
            try
            {
                var obj = tr.GetObject(item.ObjectId, OpenMode.ForWrite);
                if (obj == null || obj.IsErased) return false;

                switch (item.Type)
                {
                    case NumberingItemType.Table when obj is Table table:
                        var target = table.Cells[settings.TableRow - 1, settings.TableColumn - 1];
                        // Change one content only. GenerateLayout is for constructing a
                        // table; rebuilding an existing table can reevaluate unrelated fields.
                        if (target.Contents.Count == 1) target.Contents[0].TextString = formattedNumber;
                        else target.TextString = formattedNumber;
                        return true;
                    case NumberingItemType.DBText when obj is DBText dbText:
                        dbText.TextString = formattedNumber;
                        return true;

                    case NumberingItemType.MText when obj is MText mText:
                        mText.Contents = formattedNumber;
                        return true;

                    case NumberingItemType.MLeader when obj is MLeader mleader && mleader.ContentType == ContentType.MTextContent:
                        {
                            using var mtext = mleader.MText;
                            if (mtext != null)
                            {
                                mtext.Contents = formattedNumber;
                                mleader.MText = mtext;
                                return true;
                            }
                            return false;
                        }

                    case NumberingItemType.Attribute when obj is AttributeReference attRef:
                        attRef.TextString = formattedNumber;
                        return true;

                    default:
                        return false;
                }
            }
            catch (System.Exception ex)
            {
                if (item.Type == NumberingItemType.Table) throw;
                ed.WriteMessage($"\nОшибка при нумерации объекта: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Sorting

        private List<NumberingItem> SortItems(List<NumberingItem> items, AutoNumSettings settings)
        {
            return settings.Sorting switch
            {
                // По строкам: сверху вниз, слева направо
                AutoNumSettings.SortPreset.RowsTopDown => items
                    .OrderByDescending(i => i.Position.Y)
                    .ThenBy(i => i.Position.X)
                    .ToList(),

                // По строкам: снизу вверх, слева направо
                AutoNumSettings.SortPreset.RowsBottomUp => items
                    .OrderBy(i => i.Position.Y)
                    .ThenBy(i => i.Position.X)
                    .ToList(),

                // По столбцам: слева направо, сверху вниз
                AutoNumSettings.SortPreset.ColumnsLeftRight => items
                    .OrderBy(i => i.Position.X)
                    .ThenByDescending(i => i.Position.Y)
                    .ToList(),

                // По столбцам: справа налево, сверху вниз
                AutoNumSettings.SortPreset.ColumnsRightLeft => items
                    .OrderByDescending(i => i.Position.X)
                    .ThenByDescending(i => i.Position.Y)
                    .ToList(),

                // Только горизонталь: слева направо
                AutoNumSettings.SortPreset.HorizontalOnly => items
                    .OrderBy(i => i.Position.X)
                    .ToList(),

                // Только вертикаль: сверху вниз
                AutoNumSettings.SortPreset.VerticalOnly => items
                    .OrderByDescending(i => i.Position.Y)
                    .ToList(),

                _ => items
            };
        }

        #endregion

        #region Nested Types

        private enum NumberingItemType
        {
            DBText,
            MText,
            MLeader,
            Attribute,
            Table
        }

        private class NumberingItem
        {
            public ObjectId ObjectId { get; }
            public Point3d Position { get; }
            public NumberingItemType Type { get; }

            public NumberingItem(ObjectId objectId, Point3d position, NumberingItemType type)
            {
                ObjectId = objectId;
                Position = position;
                Type = type;
            }
        }

        #endregion
    }
}
