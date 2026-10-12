// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

// File: Services/PdfPostProcessingService.cs

using Autodesk.AutoCAD.EditorInput;
using System;
using System.Collections.Generic;
using System.IO;
using AutocadTools.Models;

// aliases (должны быть тут, до namespace)
using IOPath = System.IO.Path;
using ITextPdfReader = iText.Kernel.Pdf.PdfReader;
using ITextPdfWriter = iText.Kernel.Pdf.PdfWriter;
using ITextPdfDocument = iText.Kernel.Pdf.PdfDocument;
using ITextPdfPage = iText.Kernel.Pdf.PdfPage;
using ITextRect = iText.Kernel.Geom.Rectangle;
using ITextPdfLinkAnnotation = iText.Kernel.Pdf.Annot.PdfLinkAnnotation;
using ITextPdfAction = iText.Kernel.Pdf.Action.PdfAction;
using ITextPdfArray = iText.Kernel.Pdf.PdfArray;

namespace AutocadTools.Services
{
    /// <summary>
    /// Постобработка многостраничного PDF (iText7):
    /// - Обрезка (CropBox) каждой страницы по размерам из FormatDefinition, от нижнего левого угла.
    /// - Добавление гиперссылки на весь лист.
    /// </summary>
    public static class PdfPostProcessingService
    {
        /// <summary>
        /// Важно: itemsInDsdOrder должен быть в том же порядке, что и страницы в итоговом PDF.
        /// </summary>
        public static bool ProcessPdf(
            string pdfPath,
            IReadOnlyList<BatchPlotItem> itemsInDsdOrder,
            Editor ed,
            string baseWebUrl = null)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                ed?.WriteMessage($"\n[Autocad Tools Error] PDF файл не найден: {pdfPath}\n");
                return false;
            }

            if (itemsInDsdOrder == null || itemsInDsdOrder.Count == 0)
            {
                ed?.WriteMessage("\n[Autocad Tools Warning] Нет листов для постобработки.\n");
                return false;
            }

            string webUrl = baseWebUrl;
            string tempPdfPath = pdfPath + ".tmp";

            try
            {
                using (var reader = new ITextPdfReader(pdfPath))
                using (var writer = new ITextPdfWriter(tempPdfPath))
                using (var pdfDoc = new ITextPdfDocument(reader, writer))
                {
                    int totalPages = pdfDoc.GetNumberOfPages();
                    if (totalPages != itemsInDsdOrder.Count)
                        throw new InvalidDataException($"Число страниц PDF ({totalPages}) не совпало с числом листов ({itemsInDsdOrder.Count}).");
                    int pagesToProcess = totalPages;

                    for (int pageIdx = 1; pageIdx <= pagesToProcess; pageIdx++)
                    {
                        BatchPlotItem item = itemsInDsdOrder[pageIdx - 1];
                        ITextPdfPage page = pdfDoc.GetPage(pageIdx);

                        // 1) Crop (обрезка) по размерам из таблицы
                        if (item != null && item.FormatDefinition != null)
                        {
                            TryCropPageToFormat(page, item);
                        }

                        // 2) Гиперссылка на весь лист
                        string linkUrl = BuildLinkUrl(webUrl, item);
                        if (!string.IsNullOrWhiteSpace(linkUrl))
                        {
                            AddFullPageLink(page, linkUrl);
                        }
                    }
                }

                // заменить исходный файл на обработанный
                File.Move(tempPdfPath, pdfPath, overwrite: true);

                ed?.WriteMessage($"\n[Autocad Tools] ✓ PDF постобработан: {IOPath.GetFileName(pdfPath)}\n");
                return true;
            }
            catch (System.Exception ex)
            {
                ed?.WriteMessage($"\n[Autocad Tools Error] Ошибка постобработки PDF: {ex.Message}\n");
                try { if (File.Exists(tempPdfPath)) File.Delete(tempPdfPath); } catch { }
                return false;
            }
        }

        /// <summary>
        /// Обрезка страницы PDF по размерам FormatDefinition (мм) от нижнего левого угла.
        /// </summary>
        private static void TryCropPageToFormat(ITextPdfPage page, BatchPlotItem item)
        {
            // мм -> pt (1 mm = 2.834645669 pt)
            double widthPt = item.FormatDefinition.Width * 2.834645669;
            double heightPt = item.FormatDefinition.Height * 2.834645669;
            if (item.Format?.EndsWith(" v", StringComparison.OrdinalIgnoreCase) == true)
                (widthPt, heightPt) = (heightPt, widthPt);

            ITextRect current = page.GetCropBox();
            if (current == null || current.GetWidth() <= 0 || current.GetHeight() <= 0)
                current = page.GetMediaBox();

            float llx = current.GetLeft();
            float lly = current.GetBottom();

            float urx = (float)(llx + widthPt);
            float ury = (float)(lly + heightPt);

            // не выходить за границы текущего бокса
            urx = Math.Min(urx, current.GetRight());
            ury = Math.Min(ury, current.GetTop());

            if (urx <= llx || ury <= lly)
                return;

            page.SetCropBox(new ITextRect(llx, lly, urx - llx, ury - lly));
        }

        /// <summary>
        /// Добавляет гиперссылку на всю видимую область (CropBox/MediaBox).
        /// </summary>
        private static void AddFullPageLink(ITextPdfPage page, string linkUrl)
        {
            ITextRect box = page.GetCropBox();
            if (box == null || box.GetWidth() <= 0 || box.GetHeight() <= 0)
                box = page.GetMediaBox();

            var linkAnnot = new ITextPdfLinkAnnotation(
                new ITextRect(box.GetLeft(), box.GetBottom(), box.GetWidth(), box.GetHeight())
            );

            linkAnnot.SetAction(ITextPdfAction.CreateURI(linkUrl));

            // скрыть рамку
            linkAnnot.SetBorder(new ITextPdfArray(new int[] { 0, 0, 0 }));

            page.AddAnnotation(linkAnnot);
        }

        /// <summary>
        /// Строит URL вида: baseWebUrl?sheet=...&format=...&id=...
        /// Не предполагает тип SheetNumber (строка/число) — безопасно приводит к string.
        /// </summary>
        private static string BuildLinkUrl(string baseWebUrl, BatchPlotItem item)
        {
            if (string.IsNullOrWhiteSpace(baseWebUrl))
                return null;

            string sheetName = item?.Name;
            if (string.IsNullOrWhiteSpace(sheetName))
                sheetName = "Sheet";

            string format = item?.Format;
            if (string.IsNullOrWhiteSpace(format))
                format = "Unknown";

            // SheetNumber у тебя, судя по ошибкам, может быть string (поэтому без ?? 0)
            string sheetNumberStr = "";
            try
            {
                // на случай если SheetNumber не string — ToString() отработает
                sheetNumberStr = item?.SheetNumber?.ToString();
            }
            catch
            {
                sheetNumberStr = "";
            }

            if (string.IsNullOrWhiteSpace(sheetNumberStr))
                sheetNumberStr = "0";

            string sheetParam = Uri.EscapeDataString(sheetName);
            string formatParam = Uri.EscapeDataString(format);
            string idParam = Uri.EscapeDataString(sheetNumberStr);

            return $"{baseWebUrl}?sheet={sheetParam}&format={formatParam}&id={idParam}";
        }

        public static void DeliverPdf(string sourcePath, string outputPath,
            IReadOnlyList<BatchPlotItem> items, bool separateSheets)
        {
            if (!separateSheets)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                var temporary = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.Copy(sourcePath, temporary); File.Move(temporary, outputPath, overwrite: true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                return;
            }
            Directory.CreateDirectory(outputPath);
            using (var source = new ITextPdfDocument(new ITextPdfReader(sourcePath)))
            {
                if (source.GetNumberOfPages() != items.Count) throw new InvalidDataException("Неверное количество листов PDF.");
                for (int i = 0; i < items.Count; i++)
                {
                    string number = items[i].SheetNumber ?? (i + 1).ToString();
                    foreach (var invalid in Path.GetInvalidFileNameChars()) number = number.Replace(invalid, '_');
                    string destination = Path.Combine(outputPath, $"{i + 1:D3}_Лист_{number}.pdf");
                    string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        using (var target = new ITextPdfDocument(new ITextPdfWriter(temporary)))
                            source.CopyPagesTo(i + 1, i + 1, target);
                        File.Move(temporary, destination, overwrite: true);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
            }
        }
    }
}
