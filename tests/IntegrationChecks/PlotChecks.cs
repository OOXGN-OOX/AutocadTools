// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.PlottingServices;
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Services;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

public sealed class PlotChecks
{
    [CommandMethod("AUTOCADTOOLS_PLOTTEST")]
    public void Run()
    {
        var document = AcadApp.DocumentManager.MdiActiveDocument;
        var oldBackground = AcadApp.GetSystemVariable("BACKGROUNDPLOT");
        var oldFileDia = AcadApp.GetSystemVariable("FILEDIA");
        try
        {
            var directory = Path.Combine(Path.GetDirectoryName(document.Name), "plot-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var definition = FormatInsertService.EnsureDefinition(document.Database);
            foreach (var scale in new[] { 1, 100 })
            {
            var firstId = FormatInsertService.Insert(document.Database, definition, new Autodesk.AutoCAD.Geometry.Point3d(0, scale * 1000, 0), scale);
            var secondId = FormatInsertService.Insert(document.Database, definition, new Autodesk.AutoCAD.Geometry.Point3d(scale * 1000, scale * 1000, 0), scale);
            using (var tr = document.Database.TransactionManager.StartTransaction())
            {
                foreach (var pair in new[] { (firstId, "Форма 5"), (secondId, "Форма 6") })
                {
                    var block = (BlockReference)tr.GetObject(pair.Item1, OpenMode.ForWrite);
                    var form = block.DynamicBlockReferencePropertyCollection.Cast<DynamicBlockReferenceProperty>()
                        .Single(p => p.GetAllowedValues().Any(value => Equals(value, pair.Item2)));
                    form.Value = pair.Item2;
                }
                tr.Commit();
            }
            var items = new BatchPlotService { PlotScale = scale }.GetFormatBlocks().Where(item => item.BlockId == firstId || item.BlockId == secondId).ToList();
            if (items.Count == 0) throw new InvalidOperationException("No frames detected after inserting WM_FORMAT");
            if (items.Any(item => item.FormatDefinition?.Name != "A3")) throw new InvalidOperationException("Scaled frame format was not detected");
            var selected = items.Take(2).ToList();
            if (selected.Count != 2) throw new InvalidOperationException("Two frames were not detected");
            foreach (var sheet in selected) sheet.IsSelected = true;
            var dsd = Path.Combine(directory, "test-" + scale + ".dsd");
            var pdf = Path.Combine(directory, "test-" + scale + ".pdf");
            document.Editor.Command("_.QSAVE");
            if (!BlockExportStage1Service.Run(document, dsd, selected, pdf)) throw new InvalidOperationException("DSD generation failed");
            using (var tr = document.Database.TransactionManager.StartTransaction())
            {
                using var checkDsd = new DsdData(); checkDsd.ReadDsd(dsd);
                var entries = checkDsd.GetDsdEntryCollection();
                var setups = (DBDictionary)tr.GetObject(document.Database.PlotSettingsDictionaryId, OpenMode.ForRead);
                foreach (DsdEntry entry in entries)
                {
                    var setup = (PlotSettings)tr.GetObject(setups.GetAt(entry.Nps), OpenMode.ForRead);
                    if (Math.Abs(setup.CustomPrintScale.Denominator / setup.CustomPrintScale.Numerator - scale) > 0.001)
                        throw new InvalidOperationException("Plot scale is not exact 1:" + scale);
                }
            }
            document.Editor.Command("_.QSAVE");
            AcadApp.SetSystemVariable("BACKGROUNDPLOT", 0);
            AcadApp.SetSystemVariable("FILEDIA", 0);
            using var data = new DsdData();
            data.ReadDsd(dsd);
            data.SheetType = SheetType.MultiPdf;
            var plotter = PlotConfigManager.SetCurrentConfig(PdfPlotterService.PlotterName);
            AcadApp.Publisher.PublishExecute(data, plotter);
            if (!File.Exists(pdf) || new FileInfo(pdf).Length == 0) throw new InvalidOperationException("Native PDF was not created");
            if (!PdfPostProcessingService.ProcessPdf(pdf, selected, document.Editor)) throw new InvalidOperationException("Native PDF postprocessing failed");
            var sheets = Path.Combine(directory, "sheets-" + scale);
            PdfPostProcessingService.DeliverPdf(pdf, sheets, selected, true);
            if (Directory.GetFiles(sheets, "*.pdf").Length != 2) throw new InvalidOperationException("Sheet output failed");
            document.Editor.WriteMessage("\nPLOT_SCALE_PASS: 1:" + scale + " detected A3, exact print scale, native PDF, crop/link, two sheets\n");
            }
            document.Editor.WriteMessage("\nAUTOCADTOOLS_PLOT_PASS: native PDF driver 1:1 and 1:100, DSD, WM_FORMAT, crop/link, separate sheet; " + directory + "\n");
        }
        catch (System.Exception ex) { document.Editor.WriteMessage("\nAUTOCADTOOLS_PLOT_FAIL: " + ex + "\n"); }
        finally
        {
            AcadApp.SetSystemVariable("BACKGROUNDPLOT", oldBackground);
            AcadApp.SetSystemVariable("FILEDIA", oldFileDia);
        }
    }
}
