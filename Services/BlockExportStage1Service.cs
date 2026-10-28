// SPDX-License-Identifier: AGPL-3.0-only
// OOXGN - Copyright (c) 2026. 
// Author: OOXGN | Project: BotPlusAutocadTool
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    /// <summary>
    /// Сервис подготовки PageSetup и генерации DSD.
    /// - Нулевая область полей и точный масштаб для стандартных, кратных и произвольных рамок.
    /// - Размер бумаги проверяется по данным драйвера, а окно рамки переводится из WCS в DCS.
    /// </summary>
    public static class BlockExportStage1Service
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetLongPathName(string lpszShortPath, StringBuilder lpszLongPath, uint cchBuffer);

        private const string PlotterName = PdfPlotterService.PlotterName;



        /// <summary>
        /// Генерирует PageSetup'ы и DSD файл на основе выбранных элементов.
        /// ВАЖНО: порядок DSD = порядок itemsToPrint (без GroupBy по форматам).
        /// </summary>
        public static bool Run(Document doc, string dsdPath, List<BatchPlotItem> itemsToPrint, string pdfOutputPath = null)
        {
            if (doc == null) return false;
            if (itemsToPrint == null || !itemsToPrint.Any()) return false;

            var db = doc.Database;
            var ed = doc.Editor;

            var pageSetupCache = new Dictionary<string, string>(StringComparer.Ordinal);

            string dwgPathForDsd = GetBestDwgPath(doc);

            if (string.IsNullOrWhiteSpace(dwgPathForDsd) ||
                !Path.IsPathRooted(dwgPathForDsd) ||
                !File.Exists(dwgPathForDsd))
            {
                ed.WriteMessage("\n[Autocad Tools] Чертёж не сохранён или путь не найден. Сохраните DWG перед печатью.\n");
                return false;
            }

            string folder = Path.GetDirectoryName(dwgPathForDsd) ?? "";
            string baseNameNoExt = Path.GetFileNameWithoutExtension(dwgPathForDsd) ?? "Drawing";

            var printItems = itemsToPrint
                .Where(x => x.IsSelected && !x.BlockId.IsNull && !x.BlockId.IsErased)
                .ToList();

            if (!printItems.Any())
            {
                ed.WriteMessage("\n[Autocad Tools] Нет выбранных листов для печати.\n");
                return false;
            }

            PdfPlotterService.EnsureInstalled();
            var mediaOptions = ReadMediaOptions();
            ed.WriteMessage($"\n[Autocad Tools] Подготовка DSD для {printItems.Count} листов...\n");

            var entries = new List<DsdSheet>();

            int sheetIndex = 1;
            var usedSheetTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var psDict = (DBDictionary)tr.GetObject(db.PlotSettingsDictionaryId, OpenMode.ForRead);

                var lm = LayoutManager.Current;
                ObjectId modelLayoutId = lm.GetLayoutId("Model");
                var modelLayout = (Layout)tr.GetObject(modelLayoutId, OpenMode.ForRead);

                foreach (var item in printItems)
                {
                    string formatName = item.Format ?? "Unknown";
                    string setupName = $"AUTOCADTOOLS_Sheet{sheetIndex}_{SanitizeFileName(formatName)}";

                    // cacheKey включает координаты окна/контекст
                    string cacheKey =
                        $"{formatName}_{item.BlockId.Handle.Value}_{item.Extents.MinPoint.X:F3}_{item.Extents.MinPoint.Y:F3}_{item.Extents.MaxPoint.X:F3}_{item.Extents.MaxPoint.Y:F3}";

                    if (!pageSetupCache.ContainsKey(cacheKey))
                    {
                        PlotSettings ps;

                        if (psDict.Contains(setupName))
                        {
                            var psId = psDict.GetAt(setupName);
                            ps = (PlotSettings)tr.GetObject(psId, OpenMode.ForWrite);
                        }
                        else
                        {
                            ps = new PlotSettings(modelLayout.ModelType);
                            ps.CopyFrom(modelLayout);
                            ps.PlotSettingsName = setupName;
                            ps.AddToPlotSettingsDictionary(db);
                            tr.AddNewlyCreatedDBObject(ps, true);

                            psDict.UpgradeOpen();
                        }

                        ApplyPlotSettingsForItem(ps, item, ed, tr, mediaOptions);
                        pageSetupCache[cacheKey] = setupName;
                    }
                    else
                    {
                        setupName = pageSetupCache[cacheKey];
                    }

                    // Уникальное имя листа для DSD
                    string rawTitle = $"{item.SheetNumber}_{item.Name}";
                    if (string.IsNullOrWhiteSpace(rawTitle) || rawTitle == "_")
                        rawTitle = $"Sheet_{sheetIndex}";

                    string sheetTitle = SanitizeSheetTitle(rawTitle);
                    if (!usedSheetTitles.Add(sheetTitle))
                    {
                        int k = 2;
                        string candidate;
                        do
                        {
                            candidate = SanitizeSheetTitle($"{rawTitle}_({k})");
                            k++;
                        } while (!usedSheetTitles.Add(candidate));
                        sheetTitle = candidate;
                    }

                    entries.Add(new DsdSheet(dwgPathForDsd, modelLayout.LayoutName, setupName, sheetTitle));
                    sheetIndex++;
                }

                tr.Commit();
            }

            if (sheetIndex == 1)
            {
                ed.WriteMessage("\n[Autocad Tools] Не удалось подготовить ни одного листа.\n");
                return false;
            }

            // Хвост DSD
            string pdfName = pdfOutputPath ?? Path.Combine(folder, $"{baseNameNoExt}_Binder.pdf");
            string outputFolder = Path.GetDirectoryName(pdfName);
            Directory.CreateDirectory(outputFolder);

            try
            {
                DsdFileWriter.Write(dsdPath, pdfName, entries);
                ed.WriteMessage($"[Autocad Tools] ✓ DSD создан: {dsdPath}\n");
                ed.WriteMessage($"[Autocad Tools] ✓ PDF будет создан в: {pdfName}\n");
                ed.WriteMessage($"[Autocad Tools] ✓ Листов: {sheetIndex - 1}, уникальных PageSetup: {pageSetupCache.Count}\n");
                return true;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Autocad Tools] Не удалось создать DSD: {ex.Message}\n");
                return false;
            }
        }

        private static void ApplyPlotSettingsForItem(PlotSettings ps, BatchPlotItem item, Editor ed, Transaction tr,
            List<(string Name, double Width, double Height)> mediaOptions)
        {
            PlotSettingsValidator psv = PlotSettingsValidator.Current;

            try
            {
                psv.RefreshLists(ps);
                psv.SetPlotConfigurationName(ps, PlotterName, null);
                psv.RefreshLists(ps);
                psv.SetPlotPaperUnits(ps, PlotPaperUnit.Millimeters);
                psv.SetPlotRotation(ps, PlotRotation.Degrees000);

                if (!TryBuildPlotWindowDcsFromFramePolyline(tr, ed, item.BlockId, out Extents2d windowDcs))
                    windowDcs = BuildWindowDcs(ed, new[] { item.Extents.MinPoint, item.Extents.MaxPoint,
                        new Point3d(item.Extents.MinPoint.X, item.Extents.MaxPoint.Y, 0),
                        new Point3d(item.Extents.MaxPoint.X, item.Extents.MinPoint.Y, 0) });
                item.PlotWidthMm = (windowDcs.MaxPoint.X - windowDcs.MinPoint.X) / item.PlotScale;
                item.PlotHeightMm = (windowDcs.MaxPoint.Y - windowDcs.MinPoint.Y) / item.PlotScale;

                string media = mediaOptions.Where(m => m.Width + 0.01 >= item.PlotWidthMm && m.Height + 0.01 >= item.PlotHeightMm)
                    .OrderBy(m => m.Width * m.Height).Select(m => m.Name).FirstOrDefault();
                if (media == null)
                    throw new InvalidOperationException($"Нет бумаги для рамки {item.PlotWidthMm:F2}×{item.PlotHeightMm:F2} мм.");
                psv.SetCanonicalMediaName(ps, media);
                psv.SetPlotWindowArea(ps, windowDcs);
                psv.SetPlotType(ps, Autodesk.AutoCAD.DatabaseServices.PlotType.Window);
                // All supported frames use zero-margin paper, their exact print scale,
                // and a common lower-left origin. No fitting or centering of long sheets.
                psv.SetUseStandardScale(ps, false);
                psv.SetCustomPrintScale(ps, new CustomScale(1, item.PlotScale));
                psv.SetPlotCentered(ps, false);
                psv.SetPlotOrigin(ps, new Point2d(0, 0));
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Autocad Tools] Лист {item.SheetNumber}: {ex.Message}\n");
                throw new InvalidOperationException($"Не удалось подготовить настройки печати листа {item.SheetNumber}.", ex);
            }
        }

        // =========================
        // Frame polyline -> DCS window
        // =========================

        private static bool TryBuildPlotWindowDcsFromFramePolyline(
            Transaction tr,
            Editor ed,
            ObjectId blockRefId,
            out Extents2d windowDcs)
        {
            windowDcs = default;

            if (blockRefId.IsNull) return false;

            var br = tr.GetObject(blockRefId, OpenMode.ForRead) as BlockReference;
            if (br == null) return false;

            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);

            if (!TryFindFrameInBlockDefinition(tr, btr, out FrameCandidate frame))
                return false;

            var ptsWcs = new List<Point3d>(frame.PointsLocal.Count);
            foreach (var pLocal in frame.PointsLocal)
                ptsWcs.Add(pLocal.TransformBy(br.BlockTransform));

            windowDcs = BuildWindowDcs(ed, ptsWcs);
            return true;
        }

        private static Extents2d BuildWindowDcs(Editor ed, IEnumerable<Point3d> points)
        {
            using var view = ed.GetCurrentView();
            var worldToDcs = (Matrix3d.PlaneToWorld(view.ViewDirection) *
                Matrix3d.Displacement(view.Target - Point3d.Origin) *
                Matrix3d.Rotation(-view.ViewTwist, view.ViewDirection, view.Target)).Inverse();
            var transformed = points.Select(p => p.TransformBy(worldToDcs)).ToArray();
            return new Extents2d(transformed.Min(p => p.X), transformed.Min(p => p.Y),
                transformed.Max(p => p.X), transformed.Max(p => p.Y));
        }

        /// <summary>Границы рамки в WCS, без атрибутов и геометрии штампа.</summary>
        public static bool TryGetFrameExtents(Transaction tr, BlockReference block, out Extents3d extents)
        {
            extents = default;
            // BlockTableRecord is the evaluated anonymous definition for a dynamic block.
            var definition = (BlockTableRecord)tr.GetObject(block.BlockTableRecord, OpenMode.ForRead);
            if (!TryFindFrameInBlockDefinition(tr, definition, out var frame)) return false;
            var first = frame.PointsLocal[0].TransformBy(block.BlockTransform);
            extents = new Extents3d(first, first);
            foreach (var point in frame.PointsLocal)
                extents.AddPoint(point.TransformBy(block.BlockTransform));
            return true;
        }

        private enum FrameKind
        {
            Polyline,
            Polyline2d
        }

        private sealed class FrameCandidate
        {
            public FrameKind Kind;
            public List<Point3d> PointsLocal;
        }

        private static bool TryFindFrameInBlockDefinition(
            Transaction tr,
            BlockTableRecord btr,
            out FrameCandidate best)
        {
            best = null;
            double bestArea = 0.0;

            foreach (ObjectId id in btr)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity entity) || !entity.Visible)
                    continue;
                if (id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Polyline))))
                {
                    var pl = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                    if (pl == null) continue;

                    if (!IsRectFrame(pl)) continue;

                    double area = SafeArea(pl);
                    if (area <= 1e-6) continue;

                    if (area > bestArea)
                    {
                        bestArea = area;
                        best = new FrameCandidate
                        {
                            Kind = FrameKind.Polyline,
                            PointsLocal = GetPolylinePointsLocal(pl)
                        };
                    }
                    continue;
                }

                if (id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Polyline2d))))
                {
                    var p2d = tr.GetObject(id, OpenMode.ForRead) as Polyline2d;
                    if (p2d == null) continue;

                    if (!IsRectFrame(p2d, tr, out var ptsLocal)) continue;

                    double area = SafeArea(ptsLocal);
                    if (area <= 1e-6) continue;

                    if (area > bestArea)
                    {
                        bestArea = area;
                        best = new FrameCandidate
                        {
                            Kind = FrameKind.Polyline2d,
                            PointsLocal = ptsLocal
                        };
                    }
                    continue;
                }
            }

            return best != null;
        }

        private static bool IsRectFrame(Polyline pl)
        {
            if (!pl.Closed) return false;
            if (pl.NumberOfVertices != 4) return false;
            if (Math.Abs(pl.ConstantWidth) > 1e-9) return false;

            for (int i = 0; i < 4; i++)
            {
                if (Math.Abs(pl.GetBulgeAt(i)) > 1e-9) return false;
            }

            return true;
        }

        private static bool IsRectFrame(Polyline2d p2d, Transaction tr, out List<Point3d> ptsLocal)
        {
            ptsLocal = null;

            var pts = new List<Point3d>(8);
            foreach (ObjectId vId in p2d)
            {
                var v = tr.GetObject(vId, OpenMode.ForRead) as Vertex2d;
                if (v == null) continue;
                pts.Add(v.Position);
            }

            pts = NormalizeClosedPointList(pts);

            if (pts.Count != 4) return false;
            try { if (!p2d.Closed) return false; } catch { /* ignore */ }

            var bb = GetBoundingBox2d(pts);
            const double tol = 1e-6;

            int corners = 0;
            foreach (var p in pts)
            {
                bool isCorner =
                    (Math.Abs(p.X - bb.minX) < tol || Math.Abs(p.X - bb.maxX) < tol) &&
                    (Math.Abs(p.Y - bb.minY) < tol || Math.Abs(p.Y - bb.maxY) < tol);

                if (isCorner) corners++;
            }

            if (corners != 4) return false;

            ptsLocal = pts;
            return true;
        }

        private static List<Point3d> NormalizeClosedPointList(List<Point3d> pts)
        {
            if (pts == null || pts.Count < 2) return pts;

            var first = pts[0];
            var last = pts[pts.Count - 1];

            if (first.DistanceTo(last) < 1e-9)
                pts.RemoveAt(pts.Count - 1);

            return pts;
        }

        private static List<Point3d> GetPolylinePointsLocal(Polyline pl)
        {
            var pts = new List<Point3d>(pl.NumberOfVertices);
            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                Point2d p2 = pl.GetPoint2dAt(i);
                pts.Add(new Point3d(p2.X, p2.Y, 0));
            }
            return pts;
        }

        private static double SafeArea(Polyline pl)
        {
            try { return Math.Abs(pl.Area); } catch { return 0.0; }
        }

        private static double SafeArea(List<Point3d> pts)
        {
            if (pts == null || pts.Count < 3) return 0;

            double s = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                s += (a.X * b.Y - b.X * a.Y);
            }
            return Math.Abs(s) * 0.5;
        }

        private static (double minX, double minY, double maxX, double maxY) GetBoundingBox2d(List<Point3d> pts)
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;

            foreach (var p in pts)
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            return (minX, minY, maxX, maxY);
        }

        // =========================
        // Media selection
        // =========================

        private static List<(string Name, double Width, double Height)> ReadMediaOptions()
        {
            // Read driver capabilities once for the entire release, not once per frame.
            var result = new List<(string Name, double Width, double Height)>();
            var validator = PlotSettingsValidator.Current;
            using var settings = new PlotSettings(true);
            validator.RefreshLists(settings);
            validator.SetPlotConfigurationName(settings, PlotterName, null);
            validator.RefreshLists(settings);
            validator.SetPlotPaperUnits(settings, PlotPaperUnit.Millimeters);
            validator.SetPlotRotation(settings, PlotRotation.Degrees000);
            foreach (string media in validator.GetCanonicalMediaNameList(settings))
            {
                validator.SetCanonicalMediaName(settings, media);
                var paper = settings.PlotPaperSize;
                var margins = settings.PlotPaperMargins;
                // Read actual driver dimensions. Canonical/localized names may be
                // misleading, use inches, or disagree with the associated PMP.
                if (Math.Abs(margins.MinPoint.X) > 0.001 || Math.Abs(margins.MinPoint.Y) > 0.001 ||
                    Math.Abs(margins.MaxPoint.X) > 0.001 || Math.Abs(margins.MaxPoint.Y) > 0.001) continue;
                result.Add((media, paper.X, paper.Y));
            }
            return result;
        }

        // =========================
        // Utils
        // =========================

        private static string SanitizeSheetTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "Sheet";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(title.Length);

            foreach (char c in title)
            {
                if (invalid.Contains(c) || c == ',' || c == '=' || c == ';')
                    sb.Append('_');
                else
                    sb.Append(c);
            }

            return sb.ToString();
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unknown";

            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        private static string GetBestDwgPath(Document doc)
        {
            try
            {
                return ToLongPathIfPossible(doc.Database.Filename);
            }
            catch
            {
                return doc.Name;
            }
        }

        private static string ToLongPathIfPossible(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;

            try
            {
                var sb = new StringBuilder(1024);
                uint result = GetLongPathName(path, sb, (uint)sb.Capacity);

                if (result > 0 && result < sb.Capacity)
                    return sb.ToString();

                return path;
            }
            catch
            {
                return path;
            }
        }
    }
}
