// SPDX-License-Identifier: AGPL-3.0-only
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: BotPlusAutocadTool
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    public class StripTextService
    {
        private readonly StripTextOptions _options;

        public StripTextService(StripTextOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        // ИСПРАВЛЕНО: Проверка наличия полей (Fields) у объекта
        private bool EntityHasFields(DBObject obj)
        {
            if (obj.ObjectId == ObjectId.Null) return false;

            // В новых версиях API проверяем, не пустой ли ID словаря
            if (obj.ExtensionDictionary == ObjectId.Null) return false;

            var tr = obj.Database.TransactionManager.TopTransaction;
            if (tr == null) return false;

            try
            {
                var dict = tr.GetObject(obj.ExtensionDictionary, OpenMode.ForRead) as DBDictionary;
                return dict != null && dict.Contains("ACAD_FIELD");
            }
            catch
            {
                return false;
            }
        }

        // НОВОЕ: поиск поля в контенте ячейки таблицы (замена устаревшего Table.GetFieldId)
        private static bool TryGetFirstFieldContentIndex(Cell cell, out int contentIndex)
        {
            contentIndex = -1;
            if (cell == null) return false;

            var contents = cell.Contents;
            if (contents == null) return false;

            for (int i = 0; i < contents.Count; i++)
            {
                if (contents[i].FieldId != ObjectId.Null)
                {
                    contentIndex = i;
                    return true;
                }
            }

            return false;
        }

        public string StripMtextFormatting(string input) => MTextFormatting.Strip(input, _options);

        public void ProcessMText(MText mtext)
        {
            if (mtext == null) return;

            if (_options.Columns) mtext.ColumnType = ColumnType.NoColumns;
            if (_options.BackgroundMask) mtext.BackgroundFill = false;

            if (_options.Alignment)
            {
                if (mtext.Attachment != AttachmentPoint.TopLeft)
                    mtext.Attachment = AttachmentPoint.TopLeft;
            }

            string raw = mtext.Contents;

            if (_options.Fields && EntityHasFields(mtext))
            {
                raw = mtext.Text;
            }

            string stripped = StripMtextFormatting(raw);

            if (raw != stripped || (_options.Fields && EntityHasFields(mtext)))
            {
                mtext.Contents = stripped;
            }
        }

        // --- DBText ---
        public void ProcessDBText(DBText text)
        {
            if (text == null) return;

            if (_options.Color) text.ColorIndex = 256; // ByLayer
            if (_options.Oblique) text.Oblique = 0;
            if (_options.Width) text.WidthFactor = 1.0;

            if (_options.Alignment)
            {
                text.HorizontalMode = TextHorizontalMode.TextLeft;
                text.VerticalMode = TextVerticalMode.TextBase;
                text.Position = text.Position;
            }

            if (_options.Fields && EntityHasFields(text))
            {
                text.TextString = text.TextString;
            }

            string content = text.TextString;
            bool changed = false;

            if (_options.Underline && (content.Contains("%%u") || content.Contains("%%U")))
            {
                content = content.Replace("%%u", "", StringComparison.OrdinalIgnoreCase);
                changed = true;
            }

            if (_options.Overline && (content.Contains("%%o") || content.Contains("%%O")))
            {
                content = content.Replace("%%o", "", StringComparison.OrdinalIgnoreCase);
                changed = true;
            }

            if (changed) text.TextString = content;
        }

        // --- MLeader ---
        public void ProcessMLeader(MLeader mleader)
        {
            if (mleader == null) return;
            if (mleader.ContentType == ContentType.MTextContent)
            {
                MText mtext = mleader.MText;
                bool changed = false;

                if (_options.BackgroundMask && mtext.BackgroundFill)
                {
                    mtext.BackgroundFill = false;
                    changed = true;
                }

                if (_options.Frame && mleader.EnableFrameText)
                {
                    mleader.EnableFrameText = false;
                    changed = true;
                }

                if (_options.Alignment && mtext.Attachment != AttachmentPoint.TopLeft)
                {
                    mtext.Attachment = AttachmentPoint.TopLeft;
                    changed = true;
                }

                string raw = mtext.Contents;

                if (_options.Fields && EntityHasFields(mtext))
                {
                    raw = mtext.Text;
                }

                string stripped = StripMtextFormatting(raw);
                if (raw != stripped)
                {
                    mtext.Contents = stripped;
                    changed = true;
                }

                if (changed) mleader.MText = mtext;
            }
        }

        // --- Dimension ---
        public void ProcessDimension(Dimension dim)
        {
            if (dim == null) return;

            if (_options.BackgroundMask) dim.Dimtfill = 0;

            if (!string.IsNullOrEmpty(dim.DimensionText))
            {
                string stripped = StripMtextFormatting(dim.DimensionText);
                if (dim.DimensionText != stripped)
                {
                    dim.DimensionText = stripped;
                }
            }
        }

        // --- Table ---
        public void ProcessTable(Table table)
        {
            if (table == null) return;

            for (int r = 0; r < table.Rows.Count; r++)
            {
                for (int c = 0; c < table.Columns.Count; c++)
                {
                    var cell = table.Cells[r, c];

                    var state = cell.State ?? CellStates.None;
                    if (state.HasFlag(CellStates.Linked) || state.HasFlag(CellStates.ContentLocked))
                        continue;

                    // ИСПРАВЛЕНО: вместо table.GetFieldId(r,c,i) используем cell.Contents[i].FieldId
                    if (_options.Fields && TryGetFirstFieldContentIndex(cell, out int fieldContentIndex))
                    {
                        // Берем отображаемую строку (уже вычисленную) и сохраняем как обычный текст
                        string val = cell.GetTextString(FormatOption.FormatOptionNone);

                        // “Убиваем” поле: отвязываем FieldId и записываем текст в Value конкретного контента
                        cell.Contents[fieldContentIndex].FieldId = ObjectId.Null;
                        cell.Contents[fieldContentIndex].Value = val;

                        // На всякий случай также выставляем Value ячейки (это может отличаться по версиям API)
                        cell.Value = val;
                    }

                    string raw = cell.TextString;
                    if (string.IsNullOrEmpty(raw)) continue;

                    string stripped = StripMtextFormatting(raw);

                    if (_options.Alignment)
                    {
                        cell.Alignment = CellAlignment.TopLeft;
                    }

                    if (_options.BackgroundMask && cell.BackgroundColor.ColorIndex != 0)
                    {
                        cell.BackgroundColor = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                            Autodesk.AutoCAD.Colors.ColorMethod.ByBlock, 0);
                    }

                    if (raw != stripped)
                    {
                        cell.TextString = stripped;
                    }
                }
            }
        }

        // --- Block Attributes ---
        public bool ProcessBlockReference(BlockReference blkRef, Transaction tr)
        {
            if (blkRef == null) return false;
            bool wasModified = false;

            foreach (ObjectId attId in blkRef.AttributeCollection)
            {
                if (attId.IsErased) continue;

                var attRef = tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;
                if (attRef != null && !attRef.IsConstant)
                {
                    if (_options.Alignment)
                    {
                        if (attRef.HorizontalMode != TextHorizontalMode.TextLeft ||
                            attRef.VerticalMode != TextVerticalMode.TextBase)
                        {
                            attRef.HorizontalMode = TextHorizontalMode.TextLeft;
                            attRef.VerticalMode = TextVerticalMode.TextBase;
                            wasModified = true;
                        }
                    }

                    string raw = attRef.TextString;

                    if (_options.Fields && EntityHasFields(attRef))
                    {
                        // Простое чтение и запись могут не убить поле в атрибуте, но пока оставим как есть
                    }

                    if (!string.IsNullOrEmpty(raw))
                    {
                        string stripped = StripMtextFormatting(raw);
                        if (raw != stripped)
                        {
                            attRef.TextString = stripped;
                            wasModified = true;
                        }
                    }

                    if (attRef.IsMTextAttribute)
                    {
                        MText mtext = attRef.MTextAttribute;
                        bool mtextChanged = false;
                        if (_options.BackgroundMask && mtext.BackgroundFill)
                        {
                            mtext.BackgroundFill = false;
                            mtextChanged = true;
                        }

                        if (mtextChanged)
                        {
                            attRef.MTextAttribute = mtext;
                            wasModified = true;
                        }
                    }
                }
            }
            return wasModified;
        }
    }
}
