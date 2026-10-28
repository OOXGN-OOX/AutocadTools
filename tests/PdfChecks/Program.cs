// Copyright (c) 2026 OOXGN (GitHub: OOXGN-OOX). All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
using AutocadTools.Models;
using AutocadTools.Services;
using iText.Kernel.Pdf;
using iText.Kernel.Geom;
using System.Security.Cryptography;

var directory = System.IO.Path.Combine(AppContext.BaseDirectory, "results", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var sourcePath = System.IO.Path.Combine(directory, "source.pdf");
using (var document = new PdfDocument(new PdfWriter(sourcePath)))
{
    document.AddNewPage(new PageSize(1200, 1000));
    document.AddNewPage(new PageSize(1000, 1200));
}
var items = new List<BatchPlotItem>
{
    new() { Name = "План 1", SheetNumber = "1", Format = "A4", FormatDefinition = new(297, 210) },
    new() { Name = "План 2", SheetNumber = "1", Format = "A4 v", FormatDefinition = new(297, 210) }
};
Assert(PdfPostProcessingService.ProcessPdf(sourcePath, items, null), "PDF processing failed");
using (var document = new PdfDocument(new PdfReader(sourcePath)))
{
    for (int i = 1; i <= document.GetNumberOfPages(); i++)
        Assert(document.GetPage(i).GetAnnotations().Count == 0, "Unexpected default PDF hyperlink");
}
Assert(PdfPostProcessingService.ProcessPdf(sourcePath, items, null, "https://example.org/projects"), "PDF processing with explicit links failed");
using (var document = new PdfDocument(new PdfReader(sourcePath)))
{
    var landscape = document.GetPage(1).GetCropBox();
    var portrait = document.GetPage(2).GetCropBox();
    Assert(Math.Abs(landscape.GetWidth() - 297 * 72.0 / 25.4) < 0.01, "Landscape width");
    Assert(Math.Abs(portrait.GetWidth() - 210 * 72.0 / 25.4) < 0.01, "Portrait width");
    Assert(Math.Abs(portrait.GetHeight() - 297 * 72.0 / 25.4) < 0.01, "Portrait height");
    for (int i = 1; i <= 2; i++)
    {
        var annotations = document.GetPage(i).GetAnnotations();
        Assert(annotations.Count == 1, "Missing sheet link");
        var action = annotations[0].GetPdfObject().GetAsDictionary(PdfName.A);
        Assert(action.GetAsString(PdfName.URI).ToUnicodeString().Contains(Uri.EscapeDataString(items[i - 1].Name)), "Wrong sheet link");
    }
}
var binder = System.IO.Path.Combine(directory, "binder.pdf");
PdfPostProcessingService.DeliverPdf(sourcePath, binder, items, false);
var sheets = System.IO.Path.Combine(directory, "sheets");
PdfPostProcessingService.DeliverPdf(sourcePath, sheets, items, true);
Assert(Directory.GetFiles(sheets, "*.pdf").Length == 2, "Duplicate sheet number overwrote output");
foreach (var file in Directory.GetFiles(sheets, "*.pdf"))
{
    using var document = new PdfDocument(new PdfReader(file));
    Assert(document.GetNumberOfPages() == 1 && document.GetPage(1).GetAnnotations().Count == 1, "Single sheet or annotation lost");
}
var originalHash = SHA256.HashData(File.ReadAllBytes(sourcePath));
Assert(!PdfPostProcessingService.ProcessPdf(sourcePath, items.Take(1).ToArray(), null), "Page-count mismatch was accepted");
Assert(originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(sourcePath))), "Failed processing changed source PDF");
PdfPostProcessingService.DeliverPdf(sourcePath, binder, items, false);
var combined = System.IO.Path.Combine(directory, "combined.pdf");
var orderedSheets = Directory.GetFiles(sheets, "*.pdf").OrderBy(p => p).Reverse().ToArray();
PdfPostProcessingService.CombinePages(orderedSheets, combined);
using (var document = new PdfDocument(new PdfReader(combined)))
{
    Assert(document.GetNumberOfPages() == 2, "Combined page count");
    Assert(document.GetPage(1).GetCropBox().GetHeight() > document.GetPage(1).GetCropBox().GetWidth(), "Combined sheet order changed");
    Assert(document.GetPage(2).GetAnnotations().Count == 1, "Combination lost annotation");
}
var combinedHash = SHA256.HashData(File.ReadAllBytes(combined));
try { PdfPostProcessingService.CombinePages(new[] { sourcePath }, combined); throw new Exception("Multi-page intermediate accepted"); }
catch (InvalidDataException) { }
Assert(combinedHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(combined))), "Failed combination changed previous output");
var binderHash = SHA256.HashData(File.ReadAllBytes(binder));
var second = PdfPostProcessingService.DeliverPdf(sourcePath, binder, items, false, true).Single();
var third = PdfPostProcessingService.DeliverPdf(sourcePath, binder, items, false, true).Single();
Assert(second == System.IO.Path.Combine(directory, "binder_2.pdf") && third == System.IO.Path.Combine(directory, "binder_3.pdf"), "Binder index sequence");
Assert(binderHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(binder))), "Indexed delivery overwrote original");
var moreSheets = PdfPostProcessingService.DeliverPdf(sourcePath, sheets, items, true, true);
Assert(moreSheets.All(p => p.EndsWith("_2.pdf")) && Directory.GetFiles(sheets, "*.pdf").Length == 4, "Sheet indexing failed");
PdfPostProcessingService.DeliverPdf(sourcePath, sheets, items, true, false);
Assert(Directory.GetFiles(sheets, "*.pdf").Length == 4, "Overwrite mode created extra files");
Console.WriteLine("PASS: indexed binder/sheets preserve originals; overwrite mode replaces exact names.");
var customPath = System.IO.Path.Combine(directory, "frames.pdf");
var frames = new List<BatchPlotItem>
{
    new() { SheetNumber = "wide", Format = "A0x3", FormatDefinition = new(2523, 1189), PlotWidthMm = 2523, PlotHeightMm = 1189 },
    new() { SheetNumber = "custom", Format = "Нестандартный", PlotWidthMm = 1234, PlotHeightMm = 456 },
    new() { SheetNumber = "rotated", Format = "A3x4", FormatDefinition = new(1189, 420), PlotWidthMm = 420, PlotHeightMm = 1189 }
};
using (var document = new PdfDocument(new PdfWriter(customPath)))
    foreach (var frame in frames)
        document.AddNewPage(new PageSize((float)(3000 * 72.0 / 25.4), (float)(3000 * 72.0 / 25.4)));
Assert(PdfPostProcessingService.ProcessPdf(customPath, frames, null), "Frame crop failed");
using (var document = new PdfDocument(new PdfReader(customPath)))
    for (int i = 0; i < frames.Count; i++)
    {
        var page = document.GetPage(i + 1);
        foreach (var bounds in new[] { page.GetMediaBox(), page.GetCropBox(), page.GetTrimBox(), page.GetBleedBox(), page.GetArtBox() })
        {
            Assert(Math.Abs(bounds.GetWidth() * 25.4 / 72 - frames[i].PlotWidthMm) < 0.01, "Physical frame width mismatch");
            Assert(Math.Abs(bounds.GetHeight() * 25.4 / 72 - frames[i].PlotHeightMm) < 0.01, "Physical frame height mismatch");
        }
    }
var undersizedPath = System.IO.Path.Combine(directory, "undersized.pdf");
using (var document = new PdfDocument(new PdfWriter(undersizedPath))) document.AddNewPage(PageSize.A4);
var undersizedHash = SHA256.HashData(File.ReadAllBytes(undersizedPath));
Assert(!PdfPostProcessingService.ProcessPdf(undersizedPath, frames.Take(1).ToArray(), null), "Undersized paper silently clipped frame");
Assert(undersizedHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(undersizedPath))), "Undersized error changed source");

Console.WriteLine("PASS: all PDF page boxes, arbitrary/wide/rotated formats, combination, collision protection and undersized-source recovery.");

Console.WriteLine("PASS: landscape/portrait crop, no implicit links, explicit sheet links, binder, individual sheets, duplicate numbers, mismatch preserves original, overwrite.");
Console.WriteLine(directory);
static void Assert(bool value, string message) { if (!value) throw new Exception(message); }

namespace Autodesk.AutoCAD.EditorInput
{
    public class Editor { public void WriteMessage(string message) => Console.Write(message); }
}
namespace AutocadTools.Models
{
    public sealed class BatchPlotItem
    {
        public string Name { get; set; }
        public string SheetNumber { get; set; }
        public string Format { get; set; }
        public TestFormat FormatDefinition { get; set; }
        public double PlotWidthMm { get; set; }
        public double PlotHeightMm { get; set; }
    }
    public sealed record TestFormat(double Width, double Height);
}
