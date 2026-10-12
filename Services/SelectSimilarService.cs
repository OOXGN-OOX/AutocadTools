// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

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
            _opts = options;
        }

        public List<ObjectId> FindSimilar(Transaction tr, BlockTableRecord space, List<Entity> sources)
        {
            List<ObjectId> result = new List<ObjectId>();

            // Проходим по всем объектам в пространстве
            foreach (ObjectId id in space)
            {
                // Если объект - сам источник, решаем добавлять или нет
                if (sources.Any(s => s.ObjectId == id))
                {
                    if (_opts.AddSourceObjects) result.Add(id);
                    continue;
                }

                Entity candidate = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (candidate == null) continue;

                // Сравниваем кандидата с каждым из источников.
                // Если он похож ХОТЯ БЫ НА ОДИН источник — выбираем его.
                foreach (var source in sources)
                {
                    if (IsSimilar(source, candidate, tr))
                    {
                        result.Add(id);
                        break; // Достаточно одного совпадения
                    }
                }
            }
            return result;
        }

        private bool IsSimilar(Entity source, Entity target, Transaction tr)
        {
            // 1. Общие проверки
            if (_opts.MatchType && source.GetType() != target.GetType()) return false;
            if (_opts.MatchLayer && source.Layer != target.Layer) return false;
            if (_opts.MatchColor && !source.Color.Equals(target.Color)) return false;
            if (_opts.MatchLinetype && source.LinetypeId != target.LinetypeId) return false;
            if (_opts.MatchLinetypeScale && Math.Abs(source.LinetypeScale - target.LinetypeScale) > Tolerance) return false;
            if (_opts.MatchLineWeight && source.LineWeight != target.LineWeight) return false;

            // 2. Специфичные проверки
            if (source is BlockReference srcBlk && target is BlockReference tgtBlk)
                return CheckBlock(srcBlk, tgtBlk, tr);

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

            // Если тип не специфичный, но общие проверки прошли -> true
            return true;
        }

        // --- Методы проверки ---

        private bool CheckBlock(BlockReference s, BlockReference t, Transaction tr)
        {
            if (_opts.BlockName && GetEffectiveName(s, tr) != GetEffectiveName(t, tr)) return false;

            if (_opts.BlockScale)
            {
                if (Math.Abs(s.ScaleFactors.X - t.ScaleFactors.X) > Tolerance ||
                    Math.Abs(s.ScaleFactors.Y - t.ScaleFactors.Y) > Tolerance ||
                    Math.Abs(s.ScaleFactors.Z - t.ScaleFactors.Z) > Tolerance) return false;
            }

            // Атрибуты (сложная логика)
            if (_opts.BlockAnyAttribute || _opts.BlockAllAttributes)
            {
                var sAtts = GetAttributes(s, tr);
                var tAtts = GetAttributes(t, tr);

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
            // Value для размеров вычисляется по-разному для разных типов, упростим до Measurement если есть
            if (_opts.DimValue)
            {
                // Это базовая проверка, для RotatedDimension и Aligned работает
                try
                {
                    double sVal = (double)s.GetType().GetProperty("Measurement").GetValue(s);
                    double tVal = (double)t.GetType().GetProperty("Measurement").GetValue(t);
                    if (Math.Abs(sVal - tVal) > Tolerance) return false;
                }
                catch { }
            }
            return true;
        }

        private bool CheckMLeader(MLeader s, MLeader t)
        {
            // Для MLeader контент сложнее, проверим если это MText
            if (_opts.MLeaderContent && s.ContentType == ContentType.MTextContent && t.ContentType == ContentType.MTextContent)
            {
                if (s.MText.Contents != t.MText.Contents) return false;
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

        private string GetEffectiveName(BlockReference blk, Transaction tr)
        {
            if (blk.IsDynamicBlock)
            {
                var btr = (BlockTableRecord)tr.GetObject(blk.DynamicBlockTableRecord, OpenMode.ForRead);
                return btr.Name;
            }
            return blk.Name;
        }

        private Dictionary<string, string> GetAttributes(BlockReference blk, Transaction tr)
        {
            var dict = new Dictionary<string, string>();
            foreach (ObjectId attId in blk.AttributeCollection)
            {
                var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                if (!dict.ContainsKey(att.Tag))
                    dict.Add(att.Tag, att.TextString);
            }
            return dict;
        }
    }
}