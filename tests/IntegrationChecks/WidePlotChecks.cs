// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.IO;
using System.Linq;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Models;
using AutocadTools.Services;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

public sealed class WidePlotChecks
{
    [CommandMethod("AUTOCADTOOLS_WIDEPLOTTEST")]
    public void Run()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        var background = AcadApp.GetSystemVariable("BACKGROUNDPLOT");
        var fileDia = AcadApp.GetSystemVariable("FILEDIA");
        try
        {
            var directory = Path.Combine(Path.GetDirectoryName(doc.Name), "wide-pdf-net" + Environment.Version.Major + "-" + Path.GetFileNameWithoutExtension(doc.Name));
            Directory.CreateDirectory(directory);
            var fixture = GostPaperFixture.Build(doc.Database);
            foreach (var scale in new[] { 1, 100 })
            {
                var service = new BatchPlotService { PlotScale = scale };
                var frames = fixture.Where(i => i.PlotScale == scale).ToList();
                if (frames.Count != 50) throw new InvalidOperationException($"Expected 50 source frames at 1:{scale}, found {frames.Count}");
                frames.Add(CreateCustomFrame(doc.Database, scale));
                string dsd = Path.Combine(directory, $"frames-{scale}.dsd");
                string pdf = Path.Combine(directory, $"frames-{scale}.pdf");
                doc.Editor.Command("_.QSAVE");
                if (!BlockExportStage1Service.Run(doc, dsd, frames, pdf)) throw new InvalidOperationException("DSD preparation failed");
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                using (var data = new DsdData())
                {
                    data.ReadDsd(dsd);
                    var setups = (DBDictionary)tr.GetObject(doc.Database.PlotSettingsDictionaryId, OpenMode.ForRead);
                    var entries = data.GetDsdEntryCollection();
                    int index = 0;
                    foreach (DsdEntry entry in entries)
                    {
                        var setup = (PlotSettings)tr.GetObject(setups.GetAt(entry.Nps), OpenMode.ForRead);
                        var frame = frames[index++];
                        double ratio = setup.CustomPrintScale.Denominator / setup.CustomPrintScale.Numerator;
                        if (setup.UseStandardScale || Math.Abs(ratio - scale) > 1e-6 || setup.PlotCentered)
                            throw new InvalidOperationException("Frame was fitted or centered instead of using exact scale");
                        if (setup.PlotPaperSize.X + 0.01 < frame.PlotWidthMm ||
                            setup.PlotPaperSize.Y + 0.01 < frame.PlotHeightMm ||
                            (frame.FormatDefinition != null && (Math.Abs(setup.PlotPaperSize.X - frame.PlotWidthMm) > 0.01 ||
                            Math.Abs(setup.PlotPaperSize.Y - frame.PlotHeightMm) > 0.01)))
                            throw new InvalidOperationException($"Wrong physical paper for {frame.Format}: {setup.PlotPaperSize}");
                        var margins = setup.PlotPaperMargins;
                        if (margins.MinPoint.GetDistanceTo(Point2d.Origin) > 0.001 || margins.MaxPoint.GetDistanceTo(Point2d.Origin) > 0.001)
                            throw new InvalidOperationException("Nonzero paper margins");
                    }
                    if (index != frames.Count) throw new InvalidOperationException("DSD count mismatch");
                }
                doc.Editor.Command("_.QSAVE");
                AcadApp.SetSystemVariable("BACKGROUNDPLOT", 0);
                AcadApp.SetSystemVariable("FILEDIA", 0);
                using (var data = new DsdData())
                {
                    data.ReadDsd(dsd);
                    data.SheetType = SheetType.MultiPdf;
                    AcadApp.Publisher.PublishExecute(data, PlotConfigManager.SetCurrentConfig(PdfPlotterService.PlotterName));
                }
                if (!File.Exists(pdf) || new FileInfo(pdf).Length == 0) throw new InvalidOperationException("Native PDF missing");
                if (!PdfPostProcessingService.ProcessPdf(pdf, frames, doc.Editor)) throw new InvalidOperationException("PDF processing failed");
                var sheets = Path.Combine(directory, "sheets-" + scale);
                PdfPostProcessingService.DeliverPdf(pdf, sheets, frames, true);
                if (Directory.GetFiles(sheets, "*.pdf").Length != frames.Count) throw new InvalidOperationException("Separate PDF delivery failed");
                File.WriteAllLines(Path.Combine(directory, $"frames-{scale}.tsv"), frames.Select(i =>
                    string.Join("\t", i.SheetNumber, i.Format, i.PlotWidthMm.ToString(CultureInfo.InvariantCulture), i.PlotHeightMm.ToString(CultureInfo.InvariantCulture))));
                doc.Editor.WriteMessage($"\nWIDE_SCALE_PASS: 1:{scale}, 50 GOST orientation combinations + arbitrary 3000x1500, exact media/scale/origin, native binder and sheets\n");
            }
            CheckOrderAndWindow();
            doc.Editor.WriteMessage("\nAUTOCADTOOLS_WIDEPLOT_PASS: " + directory + "\n");
        }
        catch (System.Exception ex) { doc.Editor.WriteMessage("\nAUTOCADTOOLS_WIDEPLOT_FAIL: " + ex + "\n"); }
        finally
        {
            AcadApp.SetSystemVariable("BACKGROUNDPLOT", background);
            AcadApp.SetSystemVariable("FILEDIA", fileDia);
        }
    }

    private static BatchPlotItem CreateCustomFrame(Database database, int scale)
    {
        using var tr = database.TransactionManager.StartTransaction();
        var table = (BlockTable)tr.GetObject(database.BlockTableId, OpenMode.ForWrite);
        var definition = new BlockTableRecord { Name = "AUTOCADTOOLS_CUSTOM_PLOT_" + scale };
        table.Add(definition); tr.AddNewlyCreatedDBObject(definition, true);
        var rectangle = new Polyline();
        rectangle.AddVertexAt(0, Point2d.Origin, 0, 0, 0);
        rectangle.AddVertexAt(1, new Point2d(3000, 0), 0, 0, 0);
        rectangle.AddVertexAt(2, new Point2d(3000, 1500), 0, 0, 0);
        rectangle.AddVertexAt(3, new Point2d(0, 1500), 0, 0, 0);
        rectangle.Closed = true;
        definition.AppendEntity(rectangle); tr.AddNewlyCreatedDBObject(rectangle, true);
        var model = (BlockTableRecord)tr.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        var block = new BlockReference(new Point3d(0, -1000 * scale, 0), definition.ObjectId) { ScaleFactors = new Scale3d(scale) };
        model.AppendEntity(block); tr.AddNewlyCreatedDBObject(block, true);
        var item = new BatchPlotItem { BlockId = block.ObjectId, Extents = block.GeometricExtents, PlotScale = scale,
            Format = "Нестандартный", SheetNumber = "custom", Name = "3000x1500", IsSelected = true };
        tr.Commit();
        return item;
    }

    private static void CheckOrderAndWindow()
    {
        using var vm = new AutocadTools.ViewModels.BatchPlotViewModel();
        var sample = new[]
        {
            new BatchPlotItem { Name = "A", SheetNumber = "edited", IsSelected = false, Extents = new Extents3d(new Point3d(0, 10000, 0), new Point3d(420, 10297, 0)) },
            new BatchPlotItem { Name = "B", Extents = new Extents3d(new Point3d(10000, 10000, 0), new Point3d(10420, 10297, 0)) },
            new BatchPlotItem { Name = "C", Extents = new Extents3d(Point3d.Origin, new Point3d(420, 297, 0)) },
            new BatchPlotItem { Name = "D", Extents = new Extents3d(new Point3d(10000, 0, 0), new Point3d(10420, 297, 0)) }
        };
        vm.Items = new System.Collections.ObjectModel.ObservableCollection<BatchPlotItem>(sample);
        vm.IsBottomToTop = true; Expect("CDAB");
        vm.IsLeftToRight = true; Expect("ACBD");
        vm.IsRightToLeft = true; Expect("BDAC");
        vm.IsTopToBottom = true; Expect("ABCD");
        if (vm.Items[0].IsSelected || vm.Items[0].SheetNumber != "edited" || !ReferenceEquals(vm.Items[0], sample[0]))
            throw new InvalidOperationException("Reordering lost edits or selection");

        void Expect(string expected)
        {
            if (string.Concat(vm.Items.Select(i => i.Name)) != expected) throw new InvalidOperationException("Wrong search direction " + expected);
        }
    }
}
