// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    public static class StampTextLayoutService
    {
        private sealed class Cell
        {
            public string Tag, Property, Label;
            public double Width, Height;
        }
        private static readonly Cell[] Cells = {
            new Cell { Tag = "ШИФР", Property = "Обозначение_документа", Label = "Обозначение документа", Width = 120, Height = 10 },
            new Cell { Tag = "ОБЪЕКТ", Property = "Наименование_объекта", Label = "Наименование проекта", Width = 120, Height = 15 },
            new Cell { Tag = "КОМПЛЕКТ", Property = "Наименование_участка", Label = "Наименование здания", Width = 70, Height = 15 },
            new Cell { Tag = "КОМПАНИЯ", Property = "Компания", Label = "Компания", Width = 50, Height = 15 }
        };

        internal static string SourceState(Database database)
        {
            var summary = new DatabaseSummaryInfoBuilder(database.SummaryInfo);
            return string.Join("\0", Cells.SelectMany(cell => new[] { cell.Property, cell.Property + "1", cell.Property + "2" })
                .Select(key => summary.CustomPropertyTable.Contains(key) ? key + "=" + summary.CustomPropertyTable[key] : key));
        }

        public static StampTypography ReadTypography(Database database)
        {
            var result = new StampTypography();
            using (var transaction = database.TransactionManager.StartOpenCloseTransaction())
            {
                var table = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
                if (!table.Has(FormatInsertService.BlockName)) return result;
                var block = (BlockTableRecord)transaction.GetObject(table[FormatInsertService.BlockName], OpenMode.ForRead);
                foreach (ObjectId id in block)
                {
                    if (!(transaction.GetObject(id, OpenMode.ForRead) is AttributeDefinition attribute)) continue;
                    var style = (TextStyleTableRecord)transaction.GetObject(attribute.TextStyleId, OpenMode.ForRead);
                    var metrics = new StampTextStyle { Height = attribute.Height, WidthFactor = attribute.WidthFactor, FamilyName = style.Font.TypeFace };
                    switch (attribute.Tag.ToUpperInvariant())
                    {
                        case "ШИФР": result.Code = metrics; break;
                        case "ОБЪЕКТ": result.Project = metrics; break;
                        case "КОМПЛЕКТ": result.Building = metrics; break;
                        case "КОМПАНИЯ": result.Company = metrics; break;
                        case "ФИО1": result.Body = metrics; break;
                    }
                }
            }
            return result;
        }

        public static void Apply(Database database)
        {
            var summary = new DatabaseSummaryInfoBuilder(database.SummaryInfo);
            bool migrated = false;
            var values = new Dictionary<string, string>();
            foreach (var cell in Cells)
            {
                if (summary.CustomPropertyTable.Contains(cell.Property)) values[cell.Property] = summary.CustomPropertyTable[cell.Property]?.ToString() ?? " ";
                else if (summary.CustomPropertyTable.Contains(cell.Property + "1"))
                {
                    string first = summary.CustomPropertyTable[cell.Property + "1"]?.ToString() ?? "";
                    string second = summary.CustomPropertyTable[cell.Property + "2"]?.ToString() ?? "";
                    values[cell.Property] = (first + (first.Length != 64 && first.Length > 0 && second.Length > 0 && !char.IsWhiteSpace(first.Last()) && !char.IsWhiteSpace(second[0]) ? " " : "") + second).Trim();
                    summary.CustomPropertyTable[cell.Property] = values[cell.Property]; migrated = true;
                }
            }
            if (values.Count == 0) return;
            if (migrated) database.SummaryInfo = summary.ToDatabaseSummaryInfo();
            using (var transaction = database.TransactionManager.StartTransaction())
            {
                var table = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
                var pending = new List<Tuple<AttributeReference, MText, bool>>();
                try
                {
                    foreach (ObjectId id in table)
                    {
                        var owner = (BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead);
                        if (owner.IsFromExternalReference) continue;
                        foreach (ObjectId entityId in owner)
                        {
                            if (!(transaction.GetObject(entityId, OpenMode.ForRead) is BlockReference frame) || !IsFormat(frame, transaction)) continue;
                            var definition = (BlockTableRecord)transaction.GetObject(frame.BlockTableRecord, OpenMode.ForRead);
                            var anchors = definition.Cast<ObjectId>().Select(a => transaction.GetObject(a, OpenMode.ForRead)).OfType<AttributeDefinition>()
                                .ToDictionary(a => a.Tag, StringComparer.OrdinalIgnoreCase);
                            foreach (ObjectId attributeId in frame.AttributeCollection)
                            {
                                var attribute = (AttributeReference)transaction.GetObject(attributeId, OpenMode.ForRead);
                                var cell = Cells.FirstOrDefault(c => string.Equals(c.Tag, attribute.Tag, StringComparison.OrdinalIgnoreCase));
                                if (cell == null || !values.TryGetValue(cell.Property, out var value) || !anchors.TryGetValue(cell.Tag, out var anchor)) continue;
                                var axes = frame.BlockTransform.CoordinateSystem3d;
                                double scaleX = axes.Xaxis.Length, scaleY = axes.Yaxis.Length;
                                using (var text = new MText())
                                {
                                    text.SetDatabaseDefaults(database);
                                    text.TextStyleId = attribute.TextStyleId;
                                    text.TextHeight = attribute.Height / scaleY;
                                    text.Width = cell.Width - 2;
                                    text.Attachment = AttachmentPoint.MiddleCenter;
                                    text.Location = anchor.AlignmentPoint;
                                    string prefix = "{\\W" + (attribute.WidthFactor * scaleY / scaleX).ToString("G", CultureInfo.InvariantCulture) + ";";
                                    string wrapped = BreakLongWords(text, value, cell.Width - 2, prefix);
                                    bool explicitBreaks = wrapped != value || value.Contains('\n') || value.Contains('\r');
                                    text.Contents = prefix + Escape(wrapped) + "}";

                                    // Validate actual formatted extents before changing the DWG; font heights stay unchanged.
                                    if (text.ActualHeight > cell.Height - 1 + 1e-6 || text.ActualWidth > cell.Width - 2 + 1e-6)
                                        throw new InvalidOperationException("Поле «" + cell.Label + "» не помещается в ячейку при высоте текста " + text.TextHeight.ToString("G", CultureInfo.GetCultureInfo("ru-RU")) + " мм. Сократите текст или уберите лишние переносы.");
                                    text.TransformBy(frame.BlockTransform);

                                    // Measure all cells before converting any attribute: conversion invalidates native text layout caches.
                                    if (Matches(attribute, text, explicitBreaks, cell.Property, transaction)) continue;
                                    pending.Add(Tuple.Create(attribute, (MText)text.Clone(), explicitBreaks));
                                }
                            }
                        }
                    }
                    foreach (var change in pending)
                    {
                        change.Item1.UpgradeOpen();
                        if (change.Item1.HasFields) change.Item1.RemoveField();
                        change.Item1.IsMTextAttribute = true;
                        change.Item1.UpdateMTextAttribute();
                        using (var internalText = change.Item1.MTextAttribute)
                        {
                            internalText.TextStyleId = change.Item2.TextStyleId;
                            internalText.TextHeight = change.Item2.TextHeight;
                            internalText.Width = change.Item2.Width;
                            internalText.Attachment = change.Item2.Attachment;
                            internalText.Location = change.Item2.Location;
                            internalText.Direction = change.Item2.Direction;
                            internalText.Contents = change.Item2.Contents;
                            change.Item1.MTextAttribute = internalText;
                        }
                        if (!change.Item3)
                        {
                            var cell = Cells.First(c => string.Equals(c.Tag, change.Item1.Tag, StringComparison.OrdinalIgnoreCase));
                            using (var field = new Field("\\AcVar CustomDP." + cell.Property)) change.Item1.SetField(field);
                            change.Item1.UpdateMTextAttribute();
                        }
                        change.Item1.RecordGraphicsModified(true);
                    }
                    transaction.Commit();
                }
                finally { foreach (var change in pending) change.Item2.Dispose(); }
            }
            database.EvaluateFields(FieldEvaluationContext.Demand);
        }
        private static string BreakLongWords(MText source, string value, double width, string prefix)
        {
            // AutoCAD word wrapping does not split an unbroken word. Keep the full property unchanged,
            // and use explicit breaks in its displayed attribute when character wrapping is needed.
            using (var measure = (MText)source.Clone())
            {
                measure.Width = 0;
                double Measure(string word) { measure.Contents = prefix + Escape(word) + "}"; return measure.ActualWidth; }
                return Regex.Replace(value, @"\S+", match =>
                {
                    var word = match.Value;
                    if (Measure(word) <= width) return word;
                    var result = new List<string>();
                    var indices = StringInfo.ParseCombiningCharacters(word);
                    for (int start = 0; start < indices.Length;)
                    {
                        int low = 1, high = indices.Length - start, best = 0;
                        while (low <= high)
                        {
                            int count = (low + high) / 2;
                            int end = start + count == indices.Length ? word.Length : indices[start + count];
                            if (Measure(word.Substring(indices[start], end - indices[start])) <= width)
                            { best = count; low = count + 1; }
                            else high = count - 1;
                        }
                        if (best == 0) throw new InvalidOperationException("Символ не помещается в ячейку штампа при заданной высоте шрифта.");
                        int next = start + best == indices.Length ? word.Length : indices[start + best];
                        result.Add(word.Substring(indices[start], next - indices[start])); start += best;
                    }
                    return string.Join("\n", result);
                });
            }
        }
        private static bool IsFormat(BlockReference block, Transaction transaction)
        {
            var id = block.IsDynamicBlock ? block.DynamicBlockTableRecord : block.BlockTableRecord;
            return string.Equals(((BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead)).Name, FormatInsertService.BlockName, StringComparison.OrdinalIgnoreCase);
        }
        private static bool Matches(AttributeReference attribute, MText expected, bool explicitBreaks, string property, Transaction transaction)
        {
            if (!attribute.IsMTextAttribute) return false;
            using (var current = attribute.MTextAttribute)
            {
                if (Math.Abs(current.Width - expected.Width) > 1e-6 || Math.Abs(current.TextHeight - expected.TextHeight) > 1e-6 ||
                    current.TextStyleId != expected.TextStyleId || current.Attachment != expected.Attachment ||
                    current.Location.DistanceTo(expected.Location) > 1e-6 || !current.Direction.IsEqualTo(expected.Direction)) return false;
                if (explicitBreaks) return !attribute.HasFields && current.Contents == expected.Contents;
                if (!attribute.HasFields) return false;
                var field = (Field)transaction.GetObject(attribute.GetField(), OpenMode.ForRead);
                return field.GetFieldCode().Trim() == "\\AcVar CustomDP." + property;
            }
        }
        private static string Escape(string value) => (value ?? " ").Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}")
            .Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\P");
    }
}
