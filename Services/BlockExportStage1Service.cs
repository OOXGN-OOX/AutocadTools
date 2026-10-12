// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. 
// Author: OOXGN | Project: AutocadTools
// ============================================================================

// File: Services/BlockExportStage1Service.cs

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    /// <summary>
    /// Сервис подготовки PageSetup и генерации DSD.
    /// - Media подбираем по ГОСТ размерам.
    /// - PlotWindowArea строим по рамочной прямоугольной полилинии внутри блока.
    /// - Вершины рамки переводим WCS->DCS через текущий view и отдаем SetPlotWindowArea.
    ///
    /// ВАЖНО (точность):
    /// Для стандартных форматов (A0-A4 без "x") при точном найденном media включаем режим:
    /// 1:1, PlotOrigin=(0,0), PlotCentered=false (без ScaleToFit) — чтобы убрать суб-мм сдвиги,
    /// которые часто появляются из-за Fit/Center и драйвера PDF.
    /// </summary>
    public static class BlockExportStage1Service
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetLongPathName(string lpszShortPath, StringBuilder lpszLongPath, uint cchBuffer);

        private const string PlotterName = "DWG To PDF.pc3";

        private static readonly Dictionary<string, string> _pageSetupCache = new Dictionary<string, string>();

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

            _pageSetupCache.Clear();

            string dwgPathForDsd = GetBestDwgPath(doc);

            if (string.IsNullOrWhiteSpace(dwgPathForDsd) ||
                !Path.IsPathRooted(dwgPathForDsd) ||
                !File.Exists(dwgPathForDsd))
            {
                ed.WriteMessage("\n[Autocad Tools Error] Чертёж не сохранён или путь не найден. Сохраните DWG перед печатью.\n");
                return false;
            }

            string folder = Path.GetDirectoryName(dwgPathForDsd) ?? "";
            string baseNameNoExt = Path.GetFileNameWithoutExtension(dwgPathForDsd) ?? "Drawing";

            var printItems = itemsToPrint
                .Where(x => x.IsSelected && !x.BlockId.IsNull && !x.BlockId.IsErased)
                .ToList();

            if (!printItems.Any())
            {
                ed.WriteMessage("\n[Autocad Tools Warning] Нет выбранных листов для печати.\n");
                return false;
            }

            ed.WriteMessage($"\n[Autocad Tools] Подготовка DSD для {printItems.Count} листов...\n");

            // Заголовок DSD
            var dsd = new StringBuilder(4096);
            dsd.AppendLine("[DWF6Version]");
            dsd.AppendLine("Ver=1");
            dsd.AppendLine("[DWF6MinorVersion]");
            dsd.AppendLine("MinorVer=1");

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

                    if (!_pageSetupCache.ContainsKey(cacheKey))
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

                        ApplyPlotSettingsForItem(ps, item, ed, tr);
                        _pageSetupCache[cacheKey] = setupName;
                    }
                    else
                    {
                        setupName = _pageSetupCache[cacheKey];
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

                    dsd.AppendLine($"[DWF6Sheet:{sheetTitle}]");
                    dsd.AppendLine($"DWG={dwgPathForDsd}");
                    dsd.AppendLine("Layout=Model");
                    dsd.AppendLine($"Setup={setupName}");
                    dsd.AppendLine($"OriginalSheetPath={dwgPathForDsd}");
                    dsd.AppendLine("Has Plot Port=0");
                    dsd.AppendLine("Has3DDWF=0");

                    sheetIndex++;
                }

                tr.Commit();
            }

            if (sheetIndex == 1)
            {
                ed.WriteMessage("\n[Autocad Tools Warning] Не удалось подготовить ни одного листа.\n");
                return false;
            }

            // Хвост DSD
            string pdfName = pdfOutputPath ?? Path.Combine(folder, $"{baseNameNoExt}_Binder.pdf");
            string outputFolder = Path.GetDirectoryName(pdfName);
            Directory.CreateDirectory(outputFolder);

            dsd.AppendLine("[Target]");
            dsd.AppendLine("Type=6"); // Multi-sheet PDF
            dsd.AppendLine($"DWF={pdfName}");
            dsd.AppendLine($"OUT={outputFolder}");
            dsd.AppendLine("PWD=");

            dsd.AppendLine("PromptForDwfName=FALSE");
            dsd.AppendLine("PrepareForPreview=FALSE");
            dsd.AppendLine("PreviewSettings=0");

            dsd.AppendLine("[MRU block template]");
            dsd.AppendLine("MRU=0");
            dsd.AppendLine("[MRU Local]");
            dsd.AppendLine("MRU=1");
            dsd.AppendLine("File0=");
            dsd.AppendLine("[MRU Sheet List]");
            dsd.AppendLine("MRU=1");
            dsd.AppendLine($"File0={dsdPath}");

            dsd.AppendLine("[PdfOptions]");
            dsd.AppendLine("IncludeHyperlinks=FALSE");
            dsd.AppendLine("CreateBookmarks=TRUE");
            dsd.AppendLine("CaptureFontsInDrawing=TRUE");
            dsd.AppendLine("ConvertTextToGeometry=FALSE");
            dsd.AppendLine("VectorResolution=1200");
            dsd.AppendLine("RasterResolution=400");

            dsd.AppendLine("[AutoCAD Block Data]");
            dsd.AppendLine("IncludeBlockInfo=0");
            dsd.AppendLine("BlockTmplFilePath=");

            dsd.AppendLine("[SheetSet Properties]");
            dsd.AppendLine("IsSheetSet=FALSE");
            dsd.AppendLine("IsHomogeneous=FALSE");
            dsd.AppendLine("SheetSet Name=");
            dsd.AppendLine("NoOfCopies=1");
            dsd.AppendLine("PlotStampOn=FALSE");
            dsd.AppendLine("ViewFile=FALSE");
            dsd.AppendLine("JobID=1");
            dsd.AppendLine("SelectionSetName=");
            dsd.AppendLine("AcadProfile=");
            dsd.AppendLine("CategoryName=");
            dsd.AppendLine("LogFilePath=");
            dsd.AppendLine("IncludeLayer=TRUE");
            dsd.AppendLine("LineMerge=FALSE");
            dsd.AppendLine("CurrentPrecision=");
            dsd.AppendLine("PromptForDwfName=FALSE");
            dsd.AppendLine("PwdProtectPublishedDWF=FALSE");
            dsd.AppendLine("PromptForPwd=FALSE");
            dsd.AppendLine("RepublishingMarkups=FALSE");
            dsd.AppendLine("PublishSheetSetMetadata=FALSE");
            dsd.AppendLine("PublishSheetMetadata=FALSE");
            dsd.AppendLine("3DDWFOptions=0 1");

            try
            {
                File.WriteAllText(dsdPath, dsd.ToString(), Encoding.Unicode);
                ed.WriteMessage($"[Autocad Tools] ✓ DSD создан: {dsdPath}\n");
                ed.WriteMessage($"[Autocad Tools] ✓ PDF будет создан в: {pdfName}\n");
                ed.WriteMessage($"[Autocad Tools] ✓ Листов: {sheetIndex - 1}, уникальных PageSetup: {_pageSetupCache.Count}\n");
                return true;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Autocad Tools Error] Не удалось создать DSD: {ex.Message}\n");
                return false;
            }
        }

        private static void ApplyPlotSettingsForItem(PlotSettings ps, BatchPlotItem item, Editor ed, Transaction tr)
        {
            PlotSettingsValidator psv = PlotSettingsValidator.Current;

            try
            {
                psv.RefreshLists(ps);

                // 1) Устройство
                psv.SetPlotConfigurationName(ps, PlotterName, null);
                psv.RefreshLists(ps);

                // 2) Точные размеры (ГОСТ) + ориентация
                double exactWidth;
                double exactHeight;

                if (item.FormatDefinition != null)
                {
                    exactWidth = item.FormatDefinition.Width;
                    exactHeight = item.FormatDefinition.Height;
                }
                else
                {
                    exactWidth = Math.Abs(item.Extents.MaxPoint.X - item.Extents.MinPoint.X);
                    exactHeight = Math.Abs(item.Extents.MaxPoint.Y - item.Extents.MinPoint.Y);
                }

                bool isPortrait = !string.IsNullOrWhiteSpace(item.Format) &&
                                  item.Format.IndexOf(" v", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isPortrait)
                {
                    double tmp = exactWidth;
                    exactWidth = exactHeight;
                    exactHeight = tmp;
                }

                // 3) Для стандартных форматов (A0-A4) предпочитаем "без полей"
                bool preferBorderless =
                    item.FormatDefinition != null &&
                    !item.FormatDefinition.Name.Contains("x", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(item.Format, "Нестандартный", StringComparison.OrdinalIgnoreCase);

                // 4) Выбор media по размерам
                string canonicalMedia = FindExactMediaFormat(psv, ps, exactWidth, exactHeight, preferBorderless, out bool exactFound);

                if (string.IsNullOrEmpty(canonicalMedia))
                {
                    canonicalMedia = FindClosestMediaFormat(psv, ps, exactWidth, exactHeight, preferBorderless);
                }

                if (string.IsNullOrEmpty(canonicalMedia))
                {
                    var list = psv.GetCanonicalMediaNameList(ps);
                    canonicalMedia = (list != null && list.Count > 0) ? list[0] : null;
                }

                // 5) Применяем media
                psv.SetPlotConfigurationName(ps, PlotterName, canonicalMedia);
                psv.RefreshLists(ps);

                // 6) Окно печати: по рамочной полилинии внутри блока (fallback - extents)
                if (TryBuildPlotWindowDcsFromFramePolyline(tr, ed, item.BlockId, out Extents2d windowDcs))
                {
                    psv.SetPlotWindowArea(ps, windowDcs);
                    psv.SetPlotType(ps, Autodesk.AutoCAD.DatabaseServices.PlotType.Window);
                }
                else
                {
                    var min2d = new Point2d(item.Extents.MinPoint.X, item.Extents.MinPoint.Y);
                    var max2d = new Point2d(min2d.X + exactWidth, min2d.Y + exactHeight);
                    psv.SetPlotWindowArea(ps, new Extents2d(min2d, max2d));
                    psv.SetPlotType(ps, Autodesk.AutoCAD.DatabaseServices.PlotType.Window);
                }

                // 7-9) Масштаб/позиционирование:
                // Для стандартных форматов при точном media: строго 1:1, без центрирования, origin=(0,0).
                // Иначе (кратные/нестандартные): ScaleToFit + Center.
                bool isStandardExact =
                    exactFound &&
                    item.FormatDefinition != null &&
                    !item.FormatDefinition.Name.Contains("x", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(item.Format, "Нестандартный", StringComparison.OrdinalIgnoreCase);

                if (isStandardExact)
                {
                    psv.SetUseStandardScale(ps, true);
                    psv.SetStdScaleType(ps, StdScaleType.StdScale1To1);
                    psv.SetPlotCentered(ps, false);

                    // Начало области печати (внутри полей). Обычно (0,0).
                    // В PlotSettingsValidator есть SetPlotOrigin, но установка свойства тоже работает для PlotSettings.
                    psv.SetPlotOrigin(ps, new Point2d(0, 0));

                }
                else
                {
                    psv.SetUseStandardScale(ps, true);
                    psv.SetStdScaleType(ps, StdScaleType.ScaleToFit);
                    psv.SetPlotCentered(ps, true);
                }

                // 10) Ротация фиксированно 0
                psv.SetPlotRotation(ps, PlotRotation.Degrees000);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Autocad Tools Error] Лист {item.SheetNumber}: {ex.Message}\n");
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

            using (var view = ed.GetCurrentView())
            {
                Matrix3d matWcsToDcs =
                    Matrix3d.PlaneToWorld(view.ViewDirection) *
                    Matrix3d.Displacement(view.Target - Point3d.Origin) *
                    Matrix3d.Rotation(-view.ViewTwist, view.ViewDirection, view.Target);

                matWcsToDcs = matWcsToDcs.Inverse();

                double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
                double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;

                foreach (var p in ptsWcs)
                {
                    var d = p.TransformBy(matWcsToDcs);
                    minX = Math.Min(minX, d.X);
                    minY = Math.Min(minY, d.Y);
                    maxX = Math.Max(maxX, d.X);
                    maxY = Math.Max(maxY, d.Y);
                }

                windowDcs = new Extents2d(minX, minY, maxX, maxY);
            }

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

        private static string FindExactMediaFormat(
            PlotSettingsValidator psv,
            PlotSettings ps,
            double targetW,
            double targetH,
            bool preferBorderless,
            out bool found)
        {
            found = false;

            var mediaList = psv.GetCanonicalMediaNameList(ps);
            if (mediaList == null || mediaList.Count == 0) return null;

            const double tol = 0.5;

            if (preferBorderless)
            {
                for (int i = 0; i < mediaList.Count; i++)
                {
                    string canonical = mediaList[i];
                    string locale = null;
                    try { locale = psv.GetLocaleMediaName(ps, canonical); } catch { }

                    if (!TryExtractMmSize(locale, out double w, out double h) &&
                        !TryExtractMmSize(canonical, out w, out h))
                        continue;

                    if (Math.Abs(w - targetW) >= tol || Math.Abs(h - targetH) >= tol)
                        continue;

                    bool isBorderless =
                        (!string.IsNullOrWhiteSpace(locale) && locale.Contains("без полей", StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(canonical) && canonical.Contains("full_bleed", StringComparison.OrdinalIgnoreCase));

                    if (!isBorderless) continue;

                    found = true;
                    return canonical;
                }
            }

            for (int i = 0; i < mediaList.Count; i++)
            {
                string canonical = mediaList[i];
                string locale = null;
                try { locale = psv.GetLocaleMediaName(ps, canonical); } catch { }

                if (!TryExtractMmSize(locale, out double w, out double h) &&
                    !TryExtractMmSize(canonical, out w, out h))
                    continue;

                if (Math.Abs(w - targetW) < tol && Math.Abs(h - targetH) < tol)
                {
                    found = true;
                    return canonical;
                }
            }

            return null;
        }

        private static string FindClosestMediaFormat(
            PlotSettingsValidator psv,
            PlotSettings ps,
            double targetW,
            double targetH,
            bool preferBorderless)
        {
            var mediaList = psv.GetCanonicalMediaNameList(ps);
            if (mediaList == null || mediaList.Count == 0) return null;

            string bestCanonical = null;
            double bestScore = double.MaxValue;
            bool bestIsBorderless = false;

            for (int i = 0; i < mediaList.Count; i++)
            {
                string canonical = mediaList[i];
                string locale = null;
                try { locale = psv.GetLocaleMediaName(ps, canonical); } catch { }

                if (!TryExtractMmSize(locale, out double w, out double h) &&
                    !TryExtractMmSize(canonical, out w, out h))
                    continue;

                double score = Math.Abs(w - targetW) + Math.Abs(h - targetH);

                bool isBorderless =
                    (!string.IsNullOrWhiteSpace(locale) && locale.Contains("без полей", StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(canonical) && canonical.Contains("full_bleed", StringComparison.OrdinalIgnoreCase));

                bool isBetter = score < bestScore;

                if (!isBetter && Math.Abs(score - bestScore) < 1e-9 && preferBorderless)
                {
                    if (isBorderless && !bestIsBorderless)
                        isBetter = true;
                }

                if (isBetter)
                {
                    bestScore = score;
                    bestCanonical = canonical;
                    bestIsBorderless = isBorderless;
                }
            }

            return bestCanonical;
        }

        private static bool TryExtractMmSize(string mediaName, out double w, out double h)
        {
            w = 0;
            h = 0;

            if (string.IsNullOrWhiteSpace(mediaName))
                return false;

            var regex = new Regex(@"\((\d+(?:[.,]\d+)?)\s*[xх×_\s]+\s*(\d+(?:[.,]\d+)?)\s*(?:мм|MM)\)",
                RegexOptions.IgnoreCase);

            var m = regex.Match(mediaName);
            if (!m.Success) return false;

            if (!TryParseInvariant(m.Groups[1].Value, out w)) return false;
            if (!TryParseInvariant(m.Groups[2].Value, out h)) return false;

            return true;
        }

        private static bool TryParseInvariant(string s, out double val)
        {
            val = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;

            s = s.Trim().Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out val);
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
