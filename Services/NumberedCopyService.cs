// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    public sealed class NumberedCopyTarget
    {
        public ObjectId EntityId { get; }
        public IReadOnlyList<NumberedCopySource> Sources { get; }
        internal ObjectId[] ValueIds { get; }
        public Point3d Anchor { get; }
        public Vector3d AxisX { get; }
        public Vector3d Normal { get; }
        internal NumberedCopyTarget(ObjectId entity, List<NumberedCopySource> sources, List<ObjectId> values,
            Point3d anchor, Vector3d axisX, Vector3d normal)
        { EntityId = entity; Sources = sources; ValueIds = values.ToArray(); Anchor = anchor; AxisX = axisX.GetNormal(); Normal = normal.GetNormal(); }
    }

    public static class NumberedCopyService
    {
        public static NumberedCopyTarget Read(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var entity = (Entity)tr.GetObject(id, OpenMode.ForRead);
                CheckSource(db, tr, entity);
                var sources = new List<NumberedCopySource>(); var values = new List<ObjectId>();
                Point3d anchor; Vector3d axis, normal;
                if (entity is BlockReference block)
                {
                    var definition = (BlockTableRecord)tr.GetObject(block.BlockTableRecord, OpenMode.ForRead);
                    if (definition.IsFromExternalReference) throw new ArgumentException("Внешнюю ссылку нельзя использовать как исходный блок.");
                    foreach (ObjectId attributeId in block.AttributeCollection)
                    {
                        var attribute = (AttributeReference)tr.GetObject(attributeId, OpenMode.ForRead);
                        var source = Describe(attribute, attribute.Tag + ": ");
                        if (NumberedCopyText.Find(source.Text, source.Formatted).Count == 0) continue;
                        sources.Add(source); values.Add(attributeId);
                    }
                    anchor = block.Position; normal = block.Normal;
                    axis = block.BlockTransform.CoordinateSystem3d.Xaxis;
                }
                else if (entity is DBText text)
                {
                    sources.Add(Describe(text, "Текст: ")); values.Add(id);
                    anchor = text.VerticalMode == TextVerticalMode.TextBase && (text.HorizontalMode == TextHorizontalMode.TextLeft
                        || text.HorizontalMode == TextHorizontalMode.TextAlign || text.HorizontalMode == TextHorizontalMode.TextFit)
                        ? text.Position : text.AlignmentPoint;
                    normal = text.Normal; axis = Vector3d.XAxis.TransformBy(Matrix3d.PlaneToWorld(normal)).RotateBy(text.Rotation, normal);
                }
                else if (entity is MText mtext)
                {
                    sources.Add(Describe(mtext, "МТекст: ")); values.Add(id);
                    anchor = mtext.Location; axis = mtext.Direction; normal = mtext.Normal;
                }
                else if (entity is MLeader leader && leader.ContentType == ContentType.MTextContent)
                {
                    sources.Add(Describe(leader, "Мультивыноска: ")); values.Add(id);
                    using (var content = leader.MText) { anchor = content.Location; axis = content.Direction; normal = content.Normal; }
                }
                else throw new ArgumentException("Выберите текст, МТекст, блок с атрибутами или текстовую мультивыноску.");
                if (sources.Count == 0 || !sources.Any(s => NumberedCopyText.Find(s.Text, s.Formatted).Count > 0))
                    throw new ArgumentException("В объекте нет доступных чисел. Поля AutoCAD и параметры форматирования не нумеруются.");
                return new NumberedCopyTarget(id, sources, values, anchor, axis, normal);
            }
        }

        private static void CheckSource(Database db, Transaction tr, Entity entity)
        {
            if (entity.OwnerId != db.CurrentSpaceId) throw new ArgumentException("Выберите объект текущего пространства.");
            if (((LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead)).IsLocked)
                throw new ArgumentException("Исходный объект находится на заблокированном слое.");
        }

        private static NumberedCopySource Describe(Entity entity, string prefix)
        {
            string raw, plain; bool formatted = false;
            if (entity is AttributeReference attribute)
            {
                formatted = attribute.IsMTextAttribute;
                if (formatted) using (var text = attribute.MTextAttribute) { raw = text.Contents; plain = text.Text; }
                else raw = plain = attribute.TextString;
            }
            else if (entity is DBText text) raw = plain = text.TextString;
            else if (entity is MText mtext) { raw = mtext.Contents; plain = mtext.Text; formatted = true; }
            else if (entity is MLeader leader)
                using (var content = leader.MText) { raw = content.Contents; plain = content.Text; formatted = true; }
            else throw new ArgumentException("Неподдерживаемый тип текста.");
            plain = plain.Replace('\r', ' ').Replace('\n', ' ');
            return new NumberedCopySource(prefix + (plain.Length > 100 ? plain.Substring(0, 100) + "…" : plain), raw, formatted);
        }

        public static ObjectId[] Copy(Database db, NumberedCopyTarget target, NumberedCopyOptions options,
            IReadOnlyList<Matrix3d> transforms, int firstIndex = 1)
        {
            options.Validate();
            if (transforms.Count > NumberedCopyOptions.MaximumCopies) throw new ArgumentException("Более 10000 копий за один запуск.");
            if (options.SourceIndex >= target.Sources.Count) throw new ArgumentException("Выбранный атрибут отсутствует.");
            var contents = Enumerable.Range(0, transforms.Count).Select(i =>
                NumberedCopyText.Format(target.Sources[options.SourceIndex], options, checked(firstIndex + i))).ToArray();
            var result = new List<ObjectId>();
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    CheckSource(db, tr, (Entity)tr.GetObject(target.EntityId, OpenMode.ForRead));
                    for (int i = 0; i < transforms.Count; i++)
                    {
                        using (var mapping = new IdMapping())
                        {
                            ObjectId cloneId = ObjectId.Null, valueId = ObjectId.Null;
                            try { db.DeepCloneObjects(new ObjectIdCollection(new[] { target.EntityId }), db.CurrentSpaceId, mapping, false); }
                            finally
                            {
                                foreach (IdPair pair in mapping)
                                {
                                    if (!pair.IsCloned) continue;
                                    if (pair.Key == target.EntityId) { cloneId = pair.Value; result.Add(cloneId); }
                                    if (pair.Key == target.ValueIds[options.SourceIndex]) valueId = pair.Value;
                                }
                            }
                            if (cloneId.IsNull || valueId.IsNull) throw new InvalidOperationException("AutoCAD не скопировал выбранный текст или атрибут.");
                            var clone = (Entity)tr.GetObject(cloneId, OpenMode.ForWrite);
                            clone.TransformBy(transforms[i]);
                            SetText(db, (Entity)tr.GetObject(valueId, OpenMode.ForWrite), contents[i]);
                        }
                    }
                    tr.Commit();
                }
            }
            catch
            {
                // DeepCloneObjects appends database-resident objects outside the caller's transaction.
                // Aborting text/geometry changes alone would leave unnumbered copies behind.
                using (var cleanup = db.TransactionManager.StartTransaction())
                {
                    foreach (var id in result.Where(id => id.IsValid && !id.IsErased)) cleanup.GetObject(id, OpenMode.ForWrite).Erase();
                    cleanup.Commit();
                }
                throw;
            }
            return result.ToArray();
        }

        internal static void SetText(Database db, Entity entity, string content)
        {
            if (entity is AttributeReference attribute)
            {
                if (attribute.IsMTextAttribute)
                {
                    using (var text = attribute.MTextAttribute) { text.Contents = content; attribute.MTextAttribute = text; }
                    attribute.UpdateMTextAttribute();
                }
                else { attribute.TextString = content; attribute.AdjustAlignment(db); }
            }
            else if (entity is DBText text) { text.TextString = content; text.AdjustAlignment(db); }
            else if (entity is MText mtext) mtext.Contents = content;
            else if (entity is MLeader leader)
                using (var textContent = leader.MText) { textContent.Contents = content; leader.MText = textContent; }
            else throw new ArgumentException("Неподдерживаемый тип текста.");
        }

        public static Entity[] Preview(Database db, NumberedCopyTarget target, NumberedCopyOptions options, int index)
        {
            var result = new List<Entity>();
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    string content = NumberedCopyText.Format(target.Sources[options.SourceIndex], options, index);
                    var original = (Entity)tr.GetObject(target.EntityId, OpenMode.ForRead);
                    var clone = (Entity)original.Clone(); result.Add(clone);
                    if (original is BlockReference block)
                    {
                        // Nonresident block clones do not own a drawable attribute collection.
                        foreach (ObjectId id in block.AttributeCollection)
                        {
                            var attribute = (AttributeReference)tr.GetObject(id, OpenMode.ForRead);
                            if (attribute.Invisible) continue;
                            var copy = (AttributeReference)attribute.Clone(); result.Add(copy);
                            if (id == target.ValueIds[options.SourceIndex]) SetText(db, copy, content);
                        }
                    }
                    else SetText(db, clone, content);
                }
                return result.ToArray();
            }
            catch { foreach (var entity in result) entity.Dispose(); throw; }
        }
    }
}
