// SPDX-License-Identifier: AGPL-3.0-only
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: BotPlusAutocadTool
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    public class SelectSimilarService
    {
        private readonly SelectSimilarOptions _opts;
        private const double Tolerance = 0.001; // Допуск для сравнения чисел

        public SelectSimilarService(SelectSimilarOptions options)
        {
            _opts = options ?? throw new ArgumentNullException(nameof(options));
        }

        public List<ObjectId> FindSimilar(Transaction tr, BlockTableRecord space, List<Entity> sources)
        {
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (space == null) throw new ArgumentNullException(nameof(space));
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            var result = new List<ObjectId>();
            if (sources.Count == 0) return result;

            // Only compare sources that can pass the mandatory type/layer filters.
            var buckets = new Dictionary<(Type, ObjectId), List<Entity>>();
            var sourceIds = new HashSet<ObjectId>();
            var cache = new ComparisonCache();
            foreach (var source in sources)
            {
                if (source == null || source.IsErased) continue;
                sourceIds.Add(source.ObjectId);
                var key = GetKey(source);
                if (!buckets.TryGetValue(key, out var bucket)) buckets[key] = bucket = new List<Entity>();
                bucket.Add(source);
            }
            foreach (ObjectId id in space)
            {
                if (id.IsNull || !id.IsValid || id.IsErased) continue;
                if (sourceIds.Contains(id))
                {
                    if (_opts.AddSourceObjects) result.Add(id);
                    continue;
                }
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity candidate) continue;
                if (!buckets.TryGetValue(GetKey(candidate), out var candidates)) continue;
                foreach (var source in candidates)
                {
                    if (!IsSimilar(source, candidate, tr, cache)) continue;
                    result.Add(id);
                    break;
                }
            }
            return result;
        }

        private (Type, ObjectId) GetKey(Entity entity) =>
            (_opts.MatchType ? entity.GetType() : null, _opts.MatchLayer ? entity.LayerId : ObjectId.Null);
        private bool IsSimilar(Entity source, Entity target, Transaction tr, ComparisonCache cache)
        {
            // 1. Общие проверки
            if (_opts.MatchType && source.GetType() != target.GetType()) return false;
            if (_opts.MatchLayer && source.LayerId != target.LayerId) return false;
            if (_opts.MatchColor && !source.Color.Equals(target.Color)) return false;
            if (_opts.MatchLinetype && source.LinetypeId != target.LinetypeId) return false;
            if (_opts.MatchLinetypeScale && Math.Abs(source.LinetypeScale - target.LinetypeScale) > Tolerance) return false;
            if (_opts.MatchLineWeight && source.LineWeight != target.LineWeight) return false;

            // 2. Специфичные проверки
            if (source is BlockReference srcBlk && target is BlockReference tgtBlk)
                return CheckBlock(srcBlk, tgtBlk, tr, cache);

            if (source is Circle srcCirc && target is Circle tgtCirc)
                return CheckCircle(srcCirc, tgtCirc);

            if (source is Line srcLine && target is Line tgtLine)
                return CheckLine(srcLine, tgtLine);

            if (source is Polyline srcPoly && target is Polyline tgtPoly)
                return CheckPolyline(srcPoly, tgtPoly);

            if (source is DBText srcTxt && target is DBText tgtTxt)
                return CheckText(srcTxt, tgtTxt);

            if (source is MText srcMtx && target is MText tgtMtx)
                return CheckMText(srcMtx, tgtMtx);

            if (source is Dimension srcDim && target is Dimension tgtDim)
                return CheckDimension(srcDim, tgtDim);

            if (source is MLeader srcMld && target is MLeader tgtMld)
                return CheckMLeader(srcMld, tgtMld);

            if (source is Hatch srcHatch && target is Hatch tgtHatch)
                return CheckHatch(srcHatch, tgtHatch);

            if (_opts.ImagePath && source is RasterImage sourceImage && target is RasterImage targetImage)
            {
                var sourceDefinition = (RasterImageDef)tr.GetObject(sourceImage.ImageDefId, OpenMode.ForRead);
                var targetDefinition = (RasterImageDef)tr.GetObject(targetImage.ImageDefId, OpenMode.ForRead);
                return string.Equals(sourceDefinition.SourceFileName, targetDefinition.SourceFileName, StringComparison.OrdinalIgnoreCase);
            }

            // Other types use the enabled general filters.
            return true;
        }

        // --- Методы проверки ---

        private bool CheckBlock(BlockReference s, BlockReference t, Transaction tr, ComparisonCache cache)
        {
            if (_opts.BlockName && GetEffectiveName(s, tr, cache) != GetEffectiveName(t, tr, cache)) return false;

            if (_opts.BlockScale)
            {
                if (Math.Abs(s.ScaleFactors.X - t.ScaleFactors.X) > Tolerance ||
                    Math.Abs(s.ScaleFactors.Y - t.ScaleFactors.Y) > Tolerance ||
                    Math.Abs(s.ScaleFactors.Z - t.ScaleFactors.Z) > Tolerance) return false;
            }

            // Атрибуты (сложная логика)
            if (_opts.BlockAnyAttribute || _opts.BlockAllAttributes)
            {
                var sAtts = GetAttributes(s, tr, cache);
                var tAtts = GetAttributes(t, tr, cache);

                if (_opts.BlockAllAttributes)
                {
                    // Все значения источника должны быть в цели (строгое совпадение)
                    if (sAtts.Count != tAtts.Count) return false;
                    foreach (var kvp in sAtts)
                    {
                        if (!tAtts.ContainsKey(kvp.Key) || tAtts[kvp.Key] != kvp.Value) return false;
                    }
                }
                else if (_opts.BlockAnyAttribute)
                {
                    // Хотя бы одно совпадение
                    bool anyMatch = false;
                    foreach (var kvp in sAtts)
                    {
                        if (tAtts.ContainsKey(kvp.Key) && tAtts[kvp.Key] == kvp.Value)
                        {
                            anyMatch = true; break;
                        }
                    }
                    if (!anyMatch && sAtts.Count > 0) return false;
                }
            }

            return true;
        }

        private bool CheckCircle(Circle s, Circle t)
        {
            if (_opts.CircleRadius && Math.Abs(s.Radius - t.Radius) > Tolerance) return false;
            return true;
        }

        private bool CheckLine(Line s, Line t)
        {
            if (_opts.LineLength && Math.Abs(s.Length - t.Length) > Tolerance) return false;
            return true;
        }

        private bool CheckPolyline(Polyline s, Polyline t)
        {
            if (_opts.PolylineLength && Math.Abs(s.Length - t.Length) > Tolerance) return false;
            if (_opts.PolylineGlobalWidth && Math.Abs(s.ConstantWidth - t.ConstantWidth) > Tolerance) return false;
            return true;
        }

        private bool CheckText(DBText s, DBText t)
        {
            if (_opts.TextContent && s.TextString != t.TextString) return false;
            if (_opts.TextStyle && s.TextStyleId != t.TextStyleId) return false;
            if (_opts.TextHeight && Math.Abs(s.Height - t.Height) > Tolerance) return false;
            if (_opts.TextLength && s.TextString.Length != t.TextString.Length) return false;
            return true;
        }

        private bool CheckMText(MText s, MText t)
        {
            if (_opts.MTextContent && s.Contents != t.Contents) return false; // Сравниваем содержимое с кодами
            if (_opts.MTextStyle && s.TextStyleId != t.TextStyleId) return false;
            return true;
        }

        private bool CheckDimension(Dimension s, Dimension t)
        {
            if (_opts.DimStyle && s.DimensionStyle != t.DimensionStyle) return false;
            if (_opts.DimTextOverride && s.DimensionText != t.DimensionText) return false;
            if (_opts.DimValue && Math.Abs(s.Measurement - t.Measurement) > Tolerance) return false;
            if (_opts.DimTextRotation && Math.Abs(s.TextRotation - t.TextRotation) > Tolerance) return false; return true;
        }

        private bool CheckMLeader(MLeader s, MLeader t)
        {
            // Для MLeader контент сложнее, проверим если это MText
            if (_opts.MLeaderContent && s.ContentType == ContentType.MTextContent && t.ContentType == ContentType.MTextContent)
            {
                using var sourceText = s.MText;
                using var targetText = t.MText;
                if (sourceText?.Contents != targetText?.Contents) return false;
            }
            return true;
        }

        private bool CheckHatch(Hatch s, Hatch t)
        {
            if (_opts.HatchPattern && s.PatternName != t.PatternName) return false;
            if (_opts.HatchScale && Math.Abs(s.PatternScale - t.PatternScale) > Tolerance) return false;
            return true;
        }

        // --- Вспомогательные ---

        private string GetEffectiveName(BlockReference blk, Transaction tr, ComparisonCache cache)
        {
            if (cache.BlockNames.TryGetValue(blk.ObjectId, out var name)) return name;
            if (blk.IsDynamicBlock)
            {
                var btr = (BlockTableRecord)tr.GetObject(blk.DynamicBlockTableRecord, OpenMode.ForRead);
                name = btr.Name;
            }
            else name = blk.Name;
            cache.BlockNames[blk.ObjectId] = name;
            return name;
        }

        private Dictionary<string, string> GetAttributes(BlockReference blk, Transaction tr, ComparisonCache cache)
        {
            if (cache.Attributes.TryGetValue(blk.ObjectId, out var existing)) return existing;
            var dict = new Dictionary<string, string>();
            foreach (ObjectId attId in blk.AttributeCollection)
            {
                var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                if (!dict.ContainsKey(att.Tag))
                    dict.Add(att.Tag, att.TextString);
            }
            cache.Attributes[blk.ObjectId] = dict;
            return dict;
        }

        private sealed class ComparisonCache
        {
            internal readonly Dictionary<ObjectId, string> BlockNames = new();
            internal readonly Dictionary<ObjectId, Dictionary<string, string>> Attributes = new();
        }
    }
}
