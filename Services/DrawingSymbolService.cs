// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    public static class DrawingSymbolService
    {
        public static double UnitsPerMillimeter(Database database) =>
            database.CurrentSpaceId == SymbolUtilityServices.GetBlockPaperSpaceId(database) || database.Insunits == UnitsValue.Undefined ? 1 :
                UnitsConverter.GetConversionFactor(UnitsValue.Millimeters, database.Insunits);

        public static ObjectId EnsureTextStyle(Database database)
        {
            string family = null, font = null;
            foreach (var candidate in new[] { (Family: "ISOCPEUR", File: "isocpeur.ttf"), (Family: "GOST Common", File: "GOST_Common.ttf") })
            {
                foreach (var directory in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts") })
                {
                    var path = Path.Combine(directory, candidate.File);
                    if (!File.Exists(path)) continue;
                    family = candidate.Family; font = path; break;
                }
                if (font != null) break;
            }
            if (font == null) throw new InvalidOperationException("Установите шрифт ISOCPEUR или GOST Common для условных обозначений.");
            using (var transaction = database.TransactionManager.StartTransaction())
            {
                var table = (TextStyleTable)transaction.GetObject(database.TextStyleTableId, OpenMode.ForRead);
                string root = "AT_SYMBOLS_" + family.Replace(' ', '_');
                for (int suffix = 0; ; suffix++)
                {
                    string name = root + (suffix == 0 ? "" : "_" + suffix);
                    if (table.Has(name))
                    {
                        var existing = (TextStyleTableRecord)transaction.GetObject(table[name], OpenMode.ForRead);
                        if (string.Equals(Path.GetFileName(existing.FileName), Path.GetFileName(font), StringComparison.OrdinalIgnoreCase) &&
                            existing.TextSize == 0 && existing.XScale == 1 && existing.ObliquingAngle == 0)
                            return existing.ObjectId;
                        continue;
                    }
                    table.UpgradeOpen();
                    var style = new TextStyleTableRecord { Name = name, TextSize = 0, XScale = 1, ObliquingAngle = 0,
                        Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor(family, false, false, 204, 34), FileName = font };
                    var id = table.Add(style); transaction.AddNewlyCreatedDBObject(style, true);
                    transaction.Commit(); return id;
                }
            }
        }

        public static List<Entity> CreateEntities(Database database, SymbolGeometry geometry, DrawingSymbolOptions options, bool definitions, ObjectId textStyle = default)
        {
            var entities = new List<Entity>();
            try
            {
                if (textStyle.IsNull) textStyle = EnsureTextStyle(database);
                foreach (var stroke in geometry.Strokes)
                {
                    if (stroke.Filled)
                    {
                        Point3d P(int i) => new Point3d(stroke.Points[i].X, stroke.Points[i].Y, 0);
                        entities.Add(new Solid(P(0), P(1), P(stroke.Points.Length == 4 ? 3 : 2), P(2)));
                    }
                    else
                    {
                        var line = new Polyline(stroke.Points.Length);
                        entities.Add(line);
                        for (int i = 0; i < stroke.Points.Length; i++) line.AddVertexAt(i, stroke.Points[i], stroke.Bulges?[i] ?? 0, 0, 0);
                        line.ConstantWidth = stroke.Width;
                        line.LineWeight = stroke.Width > 0 ? LineWeight.LineWeight000 : ThinWeight(options.MainThickness / 2);
                    }
                }
                foreach (var label in geometry.Labels)
                {
                    DBText text;
                    if (definitions)
                    {
                        var attribute = new AttributeDefinition { Tag = label.Tag, Prompt = label.Tag == "NOTE" ? "Лист / нижняя строка" :
                            options.IsLevel ? "Отметка, м" : options.FreeText ? "Текст" : "Обозначение", TextString = options.Mark,
                            Constant = false, Preset = true, LockPositionInBlock = false };
                        text = attribute;
                    }
                    else text = new DBText { TextString = options.Designation };
                    entities.Add(text);
                    text.TextString = label.Text ?? options.Designation;
                    text.TextStyleId = textStyle;
                    text.Height = geometry.TextHeight * label.HeightFactor;
                    text.HorizontalMode = TextHorizontalMode.TextCenter;
                    text.VerticalMode = TextVerticalMode.TextVerticalMid;
                    text.AlignmentPoint = new Point3d(label.Position.X, label.Position.Y, 0);
                    text.AdjustAlignment(database);
                }
                foreach (var entity in entities)
                {
                    entity.Layer = "0";
                    entity.Color = Color.FromColorIndex(ColorMethod.ByBlock, 0);
                    // A continuous entity must not inherit the drawing's current dashed linetype.
                    entity.LinetypeId = database.ContinuousLinetype;
                }
                return entities;
            }
            catch { foreach (var entity in entities) entity.Dispose(); throw; }
        }

        public static ObjectId Insert(Database database, SymbolGeometry geometry, DrawingSymbolOptions options, Matrix3d placement)
        {
            using (var transaction = database.TransactionManager.StartTransaction())
            {
                var layer = (LayerTableRecord)transaction.GetObject(database.Clayer, OpenMode.ForRead);
                if (layer.IsLocked || layer.IsFrozen || layer.IsOff)
                    throw new InvalidOperationException("Для вставки выберите включённый, размороженный и разблокированный текущий слой.");
                var table = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForWrite);
                var definition = new BlockTableRecord { Name = (options.IsSection ? "AT_SECTION_" : options.IsBreak ? "AT_BREAK_" : "AT_SYMBOL_") + Guid.NewGuid().ToString("N") };
                var blockId = table.Add(definition);
                transaction.AddNewlyCreatedDBObject(definition, true);
                var entities = CreateEntities(database, geometry, options, true);
                try
                {
                    foreach (var entity in entities)
                    {
                        definition.AppendEntity(entity);
                        transaction.AddNewlyCreatedDBObject(entity, true);
                    }
                    var space = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForWrite);
                    var reference = new BlockReference(Point3d.Origin, blockId);
                    reference.SetDatabaseDefaults(database);
                    reference.TransformBy(placement);
                    space.AppendEntity(reference);
                    transaction.AddNewlyCreatedDBObject(reference, true);
                    foreach (var entity in entities)
                    {
                        if (!(entity is AttributeDefinition attribute)) continue;
                        var value = new AttributeReference();
                        value.SetAttributeFromBlock(attribute, reference.BlockTransform);
                        value.TextString = attribute.TextString;
                        reference.AttributeCollection.AppendAttribute(value);
                        transaction.AddNewlyCreatedDBObject(value, true);
                        value.AdjustAlignment(database);
                    }
                    transaction.Commit();
                    return reference.ObjectId;
                }
                finally { foreach (var entity in entities) entity.Dispose(); }
            }
        }

        private static LineWeight ThinWeight(double millimeters)
        {
            int[] weights = { 18, 20, 25, 30, 35, 40, 50, 53, 60, 70 };
            int best = weights[0];
            foreach (int weight in weights)
                if (weight <= millimeters * 100 + 1e-8) best = weight;
            return (LineWeight)best;
        }
    }
}
