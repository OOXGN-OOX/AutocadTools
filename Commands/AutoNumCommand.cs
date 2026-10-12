// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Models;
using AutocadTools.ViewModels;
using AutocadTools.Views;

namespace AutocadTools.Commands
{
    /// <summary>
    /// Команда автонумерации для текстов, мультитекстов, атрибутов блоков и мультивыносок
    /// </summary>
    public sealed class AutoNumCommand
    {
        #region Constants

        private const string DxfText = "TEXT";
        private const string DxfMText = "MTEXT";
        private const string DxfMLeader = "MULTILEADER";
        private const string DxfBlock = "INSERT";

        #endregion

        #region Command Entry Point

        [CommandMethod("AUTONUM", CommandFlags.UsePickSet | CommandFlags.Modal)]
        [CommandMethod("НУМ", CommandFlags.UsePickSet | CommandFlags.Modal)]
        public void ExecuteAutoNum()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var ed = doc.Editor;
            var db = doc.Database;

            try
            {
                // Загружаем настройки
                var settings = LoadSettings(ed);

                // Показываем диалог настроек
                var viewModel = new AutoNumSettingsViewModel();
                bool dialogAccepted = ShowSettingsDialog(doc, viewModel, ed, settings);

                if (!dialogAccepted)
                {
                    ed.WriteMessage("\nОтменено пользователем.");
                    return;
                }

                settings = viewModel.Settings;
                settings.Save();

                // Получаем выборку объектов
                var selectedObjects = GetSelection(ed, settings);
                if (selectedObjects == null || selectedObjects.Count == 0)
                {
                    ed.WriteMessage("\nОбъекты не выбраны.");
                    return;
                }

                // Выполняем нумерацию
                int numberedCount = ProcessNumbering(db, ed, selectedObjects, settings);
                ed.WriteMessage($"\nПронумеровано объектов: {numberedCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nОшибка: {ex.Message}");
            }
        }

        #endregion

        #region Settings

        private AutoNumSettings LoadSettings(Editor ed)
        {
            try
            {
                return AutoNumSettings.Load();
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nПредупреждение: не удалось загрузить настройки. {ex.Message}");
                return new AutoNumSettings();
            }
        }

        private bool ShowSettingsDialog(Document doc, AutoNumSettingsViewModel viewModel, Editor ed, AutoNumSettings fallbackSettings)
        {
            try
            {
                using (doc.LockDocument())
                {
                    var window = new AutoNumSettingsWindow(viewModel);
                    return Application.ShowModalWindow(window) == true;
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nОшибка открытия окна настроек: {ex.Message}");
                // Fallback: получаем настройки через командную строку
                if (GetSettingsFromConsole(ed, fallbackSettings))
                {
                    fallbackSettings.Save();
                    return true;
                }
                return false;
            }
        }

        #endregion

        #region Selection

        private SelectionSet GetSelection(Editor ed, AutoNumSettings settings)
        {
            // Проверяем предварительную выборку
            var psr = ed.SelectImplied();
            if (psr.Status == PromptStatus.OK && psr.Value.Count > 0)
                return psr.Value;

            // Запрашиваем выбор у пользователя
            var pso = new PromptSelectionOptions
            {
                MessageForAdding = GetSelectionPrompt(settings)
            };

            var filter = CreateSelectionFilter(settings);
            psr = ed.GetSelection(pso, filter);

            return psr.Status == PromptStatus.OK ? psr.Value : null;
        }

        private string GetSelectionPrompt(AutoNumSettings settings)
        {
            return settings.Mode switch
            {
                AutoNumSettings.NumberingMode.Text => "\nВыберите тексты (TEXT/MTEXT) для нумерации: ",
                AutoNumSettings.NumberingMode.MLeader => "\nВыберите мультивыноски (MLEADER) для нумерации: ",
                AutoNumSettings.NumberingMode.Attribute => $"\nВыберите блоки с атрибутом '{settings.AttributeTag}' для нумерации: ",
                _ => "\nВыберите объекты для нумерации: "
            };
        }

        private SelectionFilter CreateSelectionFilter(AutoNumSettings settings)
        {
            TypedValue[] filterValues = settings.Mode switch
            {
                AutoNumSettings.NumberingMode.Text => new[]
                {
                    new TypedValue((int)DxfCode.Operator, "<OR"),
                    new TypedValue((int)DxfCode.Start, DxfText),
                    new TypedValue((int)DxfCode.Start, DxfMText),
                    new TypedValue((int)DxfCode.Operator, "OR>")
                },
                AutoNumSettings.NumberingMode.MLeader => new[]
                {
                    new TypedValue((int)DxfCode.Start, DxfMLeader)
                },
                AutoNumSettings.NumberingMode.Attribute => new[]
                {
                    new TypedValue((int)DxfCode.Start, DxfBlock)
                },
                _ => Array.Empty<TypedValue>()
            };

            return new SelectionFilter(filterValues);
        }

        #endregion

        #region Numbering Processing

        private int ProcessNumbering(Database db, Editor ed, SelectionSet selectionSet, AutoNumSettings settings)
        {
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

                // Применяем нумерацию
                foreach (var item in items)
                {
                    if (ApplyNumber(tr, item, settings.FormatNumber(currentNumber), ed))
                    {
                        numberedCount++;
                        currentNumber += settings.Step;
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
            Transaction tr, SelectionSet selectionSet, AutoNumSettings settings)
        {
            var items = new List<NumberingItem>();
            int skippedCount = 0;
            string skippedReason = string.Empty;

            foreach (SelectedObject so in selectionSet)
            {
                if (so.ObjectId.IsErased) continue;

                var ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                var item = CreateNumberingItem(tr, so.ObjectId, ent, settings, ref skippedCount, ref skippedReason);
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
                _ => null
            };
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
                return mleader.MText?.Location ?? mleader.GetFirstVertex(0);
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

        private bool ApplyNumber(Transaction tr, NumberingItem item, string formattedNumber, Editor ed)
        {
            try
            {
                var obj = tr.GetObject(item.ObjectId, OpenMode.ForWrite);
                if (obj == null || obj.IsErased) return false;

                switch (item.Type)
                {
                    case NumberingItemType.DBText when obj is DBText dbText:
                        dbText.TextString = formattedNumber;
                        return true;

                    case NumberingItemType.MText when obj is MText mText:
                        mText.Contents = formattedNumber;
                        return true;

                    case NumberingItemType.MLeader when obj is MLeader mleader && mleader.ContentType == ContentType.MTextContent:
                        var mtext = mleader.MText;
                        if (mtext != null)
                        {
                            mtext.Contents = formattedNumber;
                            mleader.MText = mtext;
                            return true;
                        }
                        return false;

                    case NumberingItemType.Attribute when obj is AttributeReference attRef:
                        attRef.TextString = formattedNumber;
                        return true;

                    default:
                        return false;
                }
            }
            catch (System.Exception ex)
            {
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

        #region Console Fallback

        private bool GetSettingsFromConsole(Editor ed, AutoNumSettings settings)
        {
            // Prefix
            var pr = ed.GetString(new PromptStringOptions("\nПрефикс [<none>]: ")
            {
                AllowSpaces = true,
                DefaultValue = settings.Prefix ?? string.Empty,
                UseDefaultValue = true
            });
            if (pr.Status != PromptStatus.OK) return false;
            settings.Prefix = pr.StringResult ?? string.Empty;

            // Suffix
            pr = ed.GetString(new PromptStringOptions("\nСуффикс [<none>]: ")
            {
                AllowSpaces = true,
                DefaultValue = settings.Suffix ?? string.Empty,
                UseDefaultValue = true
            });
            if (pr.Status != PromptStatus.OK) return false;
            settings.Suffix = pr.StringResult ?? string.Empty;

            // Start number
            var pir = ed.GetInteger(new PromptIntegerOptions($"\nНачальный номер <{settings.StartNumber}>: ")
            {
                DefaultValue = settings.StartNumber,
                UseDefaultValue = true,
                AllowNegative = false
            });
            if (pir.Status != PromptStatus.OK) return false;
            settings.StartNumber = pir.Value;

            // Step
            pir = ed.GetInteger(new PromptIntegerOptions($"\nШаг <{settings.Step}>: ")
            {
                DefaultValue = settings.Step,
                UseDefaultValue = true,
                AllowNegative = false
            });
            if (pir.Status != PromptStatus.OK) return false;
            settings.Step = pir.Value;

            // Digits
            pir = ed.GetInteger(new PromptIntegerOptions($"\nКоличество знаков (0=без заполнения) <{settings.Digits}>: ")
            {
                DefaultValue = settings.Digits,
                UseDefaultValue = true,
                AllowNegative = false
            });
            if (pir.Status != PromptStatus.OK) return false;
            settings.Digits = pir.Value;

            // Order
            var pko = new PromptKeywordOptions($"\nПорядок [Pick/Geometric] <{settings.Order}>: ");
            pko.Keywords.Add("Pick");
            pko.Keywords.Add("Geometric");
            pko.Keywords.Default = settings.Order.ToString();
            pko.AllowNone = true;
            pr = ed.GetKeywords(pko);
            if (pr.Status == PromptStatus.OK)
            {
                settings.Order = pr.StringResult == "Geometric"
                    ? AutoNumSettings.NumberingOrder.Geometric
                    : AutoNumSettings.NumberingOrder.Pick;
            }

            // Mode
            pko = new PromptKeywordOptions($"\nРежим [Text/MLeader/Attribute] <{settings.Mode}>: ");
            pko.Keywords.Add("Text");
            pko.Keywords.Add("MLeader");
            pko.Keywords.Add("Attribute");
            pko.Keywords.Default = settings.Mode.ToString();
            pko.AllowNone = true;
            pr = ed.GetKeywords(pko);
            if (pr.Status == PromptStatus.OK)
            {
                settings.Mode = pr.StringResult switch
                {
                    "MLeader" => AutoNumSettings.NumberingMode.MLeader,
                    "Attribute" => AutoNumSettings.NumberingMode.Attribute,
                    _ => AutoNumSettings.NumberingMode.Text
                };
            }

            // Attribute tag
            if (settings.Mode == AutoNumSettings.NumberingMode.Attribute)
            {
                pr = ed.GetString(new PromptStringOptions($"\nТег атрибута <{settings.AttributeTag}>: ")
                {
                    DefaultValue = settings.AttributeTag,
                    UseDefaultValue = true,
                    AllowSpaces = false
                });
                if (pr.Status != PromptStatus.OK) return false;
                settings.AttributeTag = pr.StringResult;
            }

            return true;
        }

        #endregion

        #region Nested Types

        private enum NumberingItemType
        {
            DBText,
            MText,
            MLeader,
            Attribute
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
