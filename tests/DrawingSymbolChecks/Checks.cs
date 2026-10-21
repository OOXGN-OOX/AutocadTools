// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.PlottingServices;
using AutocadTools.Models;
using AutocadTools.Services;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

public sealed class DrawingSymbolChecks
{
    private int _count;
    private void Check(bool value, string message) { _count++; if (!value) throw new InvalidOperationException(message); }
    private void Reject(Action action, string message)
    {
        try { action(); } catch (ArgumentException) { Check(true, message); return; }
        throw new InvalidOperationException(message);
    }

    [CommandMethod("DRAWING_SYMBOL_CHECKS")]
    public void Run()
    {
        string scratch = Environment.GetEnvironmentVariable("AUTOCADTOOLS_SYMBOL_SCRATCH");
        string log = Path.Combine(scratch, "results.log");
        try
        {
            var document = AcadApp.DocumentManager.MdiActiveDocument;
            var database = document.Database;
            database.Insunits = UnitsValue.Millimeters;
            var points = new[] { Point2d.Origin, new Point2d(8000, 0) };
            var options = new DrawingSymbolOptions();
            var section = DrawingSymbolGeometry.Build(points, options, 1);
            Check(Math.Abs(section.Strokes[0].Points[1].X - 1000) < 1e-8, "10 mm section stroke at 1:100");
            Check(Math.Abs(section.Strokes[0].Width - 70) < 1e-8, "Section stroke thickness scales with drawing");
            Check(Math.Abs(section.TextHeight - 500) < 1e-8, "Paper text height converted to model");
            Check(section.Labels.Count == 2, "Paired section attributes");
            var arrow = section.Strokes[3];
            Check(Math.Abs(arrow.Points[0].X - 250) < 1e-8 && arrow.Points.Skip(1).All(p => p.Y < 0), "Arrow located 2.5 mm from outer stroke end");
            var reversed = DrawingSymbolGeometry.Build(points, options, 1, -1);
            Check(reversed.Strokes[3].Points.Skip(1).All(p => p.Y > 0), "Direction reverses arrow and label side");
            database.Insunits = UnitsValue.Meters;
            Check(Math.Abs(DrawingSymbolService.UnitsPerMillimeter(database) - 0.001) < 1e-10, "Meters conversion");
            var meters = DrawingSymbolGeometry.Build(new[] { Point2d.Origin, new Point2d(8, 0) }, options, 0.001);
            Check(Math.Abs(meters.TextHeight - 0.5) < 1e-8, "Text height in meter drawings");
            database.Insunits = UnitsValue.Undefined;
            Check(DrawingSymbolService.UnitsPerMillimeter(database) == 1, "Unitless drawing uses millimeters");
            database.Insunits = UnitsValue.Millimeters;
            Reject(() => DrawingSymbolGeometry.Build(new[] { Point2d.Origin, new Point2d(100, 0) }, options, 1), "Reject overlapping section strokes");
            var invalid = options.Copy(); invalid.Scale = double.NaN;
            Reject(() => invalid.Validate(), "Reject NaN scale");
            invalid = options.Copy(); invalid.Mark = "A\nB";
            Reject(() => invalid.Validate(), "Reject multiline designation");
            invalid = options.Copy(); invalid.Mark = "A";
            Reject(() => invalid.Validate(), "Reject Latin designation");
            invalid = options.Copy(); invalid.Mark = "О";
            Reject(() => invalid.Validate(), "Reject excluded section letter");
            invalid = options.Copy(); invalid.Mark = "а";
            Check(invalid.Designation == "А", "Section letters normalized to Russian uppercase");
            invalid = options.Copy(); invalid.Kind = DrawingSymbolKind.SchemeBreak; invalid.Mark = "А";
            Check(invalid.Designation == "а", "Break letters normalized to Russian lowercase");
            invalid.Mark = "1";
            Reject(() => invalid.Validate(), "Schematic break requires letters");
            invalid = options.Copy(); invalid.TextHeight = 6;
            Reject(() => invalid.Validate(), "Reject non-GOST font height");
            invalid = DrawingSymbolCatalog.Get(DrawingSymbolKind.Elevation).Defaults(); invalid.Mark = "-0,0001";
            Check(invalid.Designation == "0,000", "Rounded zero elevation has no sign");
            invalid.Mark = "3,6";
            Check(invalid.Designation == "+3,600", "Positive elevation has a plus sign and three decimals");
            invalid = DrawingSymbolCatalog.Get(DrawingSymbolKind.SlopeArrow).Defaults(); invalid.Mark = "1:0";
            Reject(() => invalid.Validate(), "Reject zero slope denominator");
            options.Kind = DrawingSymbolKind.SteppedSection;
            var steps = new[] { Point2d.Origin, new Point2d(3000, 0), new Point2d(3000, 3000), new Point2d(8000, 3000) };
            var stepped = DrawingSymbolGeometry.Build(steps, options, 1);
            Check(stepped.Strokes.Count(s => s.Width > 0) == 4, "Corner strokes at each step");
            Reject(() => DrawingSymbolGeometry.Build(steps.Take(3).ToArray(), options, 1), "Reject nonparallel end planes");

            int blocks = 0;
            var originalStyle = database.Textstyle;
            foreach (var kind in Enum.GetValues<DrawingSymbolKind>())
            {
                options = DrawingSymbolCatalog.Get(kind).Defaults();
                if (options.HasNote) options.Note = "5";
                var geometry = DrawingSymbolGeometry.Build(kind == DrawingSymbolKind.SteppedSection ? steps : points, options, 1);
                Check(geometry.Strokes.Count > 0 || kind == DrawingSymbolKind.ImageTitle, kind + " geometry");
                Check(geometry.Labels.Count >= 1, kind + " designation count");
                if (kind == DrawingSymbolKind.Zigzag) Check(geometry.Labels.Count == 2, "Zigzag has both designations");
                var placement = Matrix3d.Displacement(new Vector3d(blocks * 15000, 1000, 200)) *
                    Matrix3d.Rotation(0.4, new Vector3d(1, 1, 1), Point3d.Origin);
                ObjectId id = DrawingSymbolService.Insert(database, geometry, options, placement);
                using (var transaction = database.TransactionManager.StartTransaction())
                {
                    var reference = (BlockReference)transaction.GetObject(id, OpenMode.ForRead);
                    Check(reference.OwnerId == database.CurrentSpaceId, kind + " current space");
                    Check(reference.Position.DistanceTo(Point3d.Origin.TransformBy(placement)) < 1e-6, kind + " UCS placement");
                    Check(reference.AttributeCollection.Count == geometry.Labels.Count, kind + " editable attributes");
                    var definition = (BlockTableRecord)transaction.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
                    foreach (ObjectId entityId in definition)
                    {
                        var entity = (Entity)transaction.GetObject(entityId, OpenMode.ForRead);
                        Check(entity.Layer == "0" && entity.LinetypeId == database.ContinuousLinetype, kind + " continuous layer-0 contents");
                        if (entity is Polyline polyline && polyline.ConstantWidth == 0)
                            Check(polyline.LineWeight == LineWeight.LineWeight035, "Thin lines use s/2 paper weight");
                    }
                    int index = 0;
                    foreach (ObjectId attributeId in reference.AttributeCollection)
                    {
                        var attribute = (AttributeReference)transaction.GetObject(attributeId, OpenMode.ForWrite);
                        Check(attribute.TextString == (geometry.Labels[index].Text ?? options.Designation) && !attribute.Invisible, "Visible Cyrillic designation");
                        var style = (TextStyleTableRecord)transaction.GetObject(attribute.TextStyleId, OpenMode.ForRead);
                        Check(style.Font.TypeFace == "ISOCPEUR" && Path.GetFileName(style.FileName).Equals("isocpeur.ttf", StringComparison.OrdinalIgnoreCase), "ISOCPEUR stored in every attribute");
                        var expected = new Point3d(geometry.Labels[index].Position.X, geometry.Labels[index].Position.Y, 0).TransformBy(placement);
                        Check(attribute.AlignmentPoint.DistanceTo(expected) < 1e-5, "Transformed text alignment");
                        attribute.TextString = options.IsBreak ? "б" : "Б";
                        index++;
                    }
                    transaction.Commit();
                }
                blocks++;
            }
            Check(database.Textstyle == originalStyle, "Current text style is preserved");
            LayoutManager.Current.CreateLayout("SYMBOL_PAPER");
            database.TileMode = false;
            LayoutManager.Current.CurrentLayout = "SYMBOL_PAPER";
            database.Insunits = UnitsValue.Meters;
            Check(DrawingSymbolService.UnitsPerMillimeter(database) == 1, "Paper space uses millimeters independently of model INSUNITS");
            database.Insunits = UnitsValue.Millimeters;
            options = new DrawingSymbolOptions { Kind = DrawingSymbolKind.Zigzag, Scale = 1, Mark = "а" };
            var paper = DrawingSymbolGeometry.Build(new[] { Point2d.Origin, new Point2d(100, 0) }, options, 1);
            var paperId = DrawingSymbolService.Insert(database, paper, options, Matrix3d.Identity);
            using (var transaction = database.TransactionManager.StartTransaction())
            {
                var reference = (BlockReference)transaction.GetObject(paperId, OpenMode.ForRead);
                Check(reference.OwnerId == SymbolUtilityServices.GetBlockPaperSpaceId(database), "Paper-space insert");
            }
            ObjectId locked;
            using (var transaction = database.TransactionManager.StartTransaction())
            {
                var layers = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForWrite);
                var layer = new LayerTableRecord { Name = "SYMBOL_LOCKED", IsLocked = true };
                locked = layers.Add(layer); transaction.AddNewlyCreatedDBObject(layer, true); transaction.Commit();
            }
            var originalLayer = database.Clayer;
            database.Clayer = locked;
            int definitionsBefore = DefinitionCount(database);
            bool refused = false;
            try { DrawingSymbolService.Insert(database, paper, options, Matrix3d.Identity); }
            catch (InvalidOperationException) { refused = true; }
            database.Clayer = originalLayer;
            Check(refused && DefinitionCount(database) == definitionsBefore, "Locked-layer rejection leaves no block definition");
            string dwg = Path.Combine(scratch, "symbols.dwg");
            database.SaveAs(dwg, DwgVersion.Current);
            using (var reopened = new Database(false, true))
            {
                reopened.ReadDwgFile(dwg, FileOpenMode.OpenForReadAndAllShare, false, null);
                reopened.CloseInput(true);
                using (var transaction = reopened.TransactionManager.StartTransaction())
                {
                    var table = (BlockTable)transaction.GetObject(reopened.BlockTableId, OpenMode.ForRead);
                    var model = (BlockTableRecord)transaction.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    int found = 0;
                    foreach (ObjectId id in model)
                    {
                        if (!(transaction.GetObject(id, OpenMode.ForRead) is BlockReference block) || !block.Name.StartsWith("AT_")) continue;
                        found++;
                        foreach (ObjectId attributeId in block.AttributeCollection)
                        {
                            var attribute = (AttributeReference)transaction.GetObject(attributeId, OpenMode.ForRead);
                            Check(attribute.TextString == "Б" || attribute.TextString == "б", "Edited attributes survive DWG round trip");
                        }
                    }
                    Check(found == DrawingSymbolCatalog.All.Count, "Every catalog symbol survives DWG round trip");
                }
            }
            File.WriteAllText(log, "SYMBOL_PASS: " + _count + " checks; geometry, units, UCS, attributes, layers, model/paper space and DWG round trip.");
        }
        catch (System.Exception ex) { File.WriteAllText(log, "SYMBOL_FAIL: " + ex); }
    }

    private static int DefinitionCount(Database database)
    {
        using (var transaction = database.TransactionManager.StartTransaction())
            return ((BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead)).Cast<ObjectId>().Count();
    }

    [CommandMethod("DRAWING_SYMBOL_PREVIEW")]
    public void Preview()
    {
        string scratch = Environment.GetEnvironmentVariable("AUTOCADTOOLS_SYMBOL_SCRATCH");
        try
        {
            var document = AcadApp.DocumentManager.MdiActiveDocument;
            var database = document.Database;
            database.TileMode = true;
            using (var transaction = database.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in space) ((Entity)transaction.GetObject(id, OpenMode.ForWrite)).Erase();
                transaction.Commit();
            }
            int row = 0;
            foreach (var kind in Enum.GetValues<DrawingSymbolKind>())
            {
                var options = DrawingSymbolCatalog.Get(kind).Defaults();
                options.Scale = 1;
                if (options.HasNote) options.Note = "5";
                var points = kind == DrawingSymbolKind.SteppedSection
                    ? new[] { Point2d.Origin, new Point2d(40, 0), new Point2d(40, -25), new Point2d(100, -25) }
                    : options.IsLeader || kind >= DrawingSymbolKind.DetailCircle && kind <= DrawingSymbolKind.DetailRectangle
                    ? new[] { Point2d.Origin, new Point2d(30, 15) }
                    : kind == DrawingSymbolKind.Elevation ? new[] { Point2d.Origin, new Point2d(25, 12) }
                    : new[] { Point2d.Origin, new Point2d(100, 0) };
                var geometry = DrawingSymbolGeometry.Build(points, options, 1);
                int column = row / 7;
                int lineIndex = row % 7;
                var placement = Matrix3d.Displacement(new Vector3d(column * 180, -lineIndex * 75, 0));
                DrawingSymbolService.Insert(database, geometry, options, placement);
                using (var transaction = database.TransactionManager.StartTransaction())
                {
                    var space = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForWrite);
                    var title = new DBText { Position = new Point3d(column * 180, -lineIndex * 75 + 25, 0),
                        TextString = DrawingSymbolCatalog.Get(kind).Title, Height = 3,
                        TextStyleId = DrawingSymbolService.EnsureTextStyle(database) };
                    space.AppendEntity(title); transaction.AddNewlyCreatedDBObject(title, true);
                    transaction.Commit();
                }
                row++;
            }
            database.UpdateExt(true);
            database.SaveAs(Path.Combine(scratch, "symbol-preview.dwg"), DwgVersion.Current);
            AcadApp.SetSystemVariable("BACKGROUNDPLOT", 0);
            using (var transaction = database.TransactionManager.StartTransaction())
            {
                var layout = (Layout)transaction.GetObject(LayoutManager.Current.GetLayoutId("Model"), OpenMode.ForRead);
                using (var settings = new PlotSettings(layout.ModelType))
                using (var info = new PlotInfo())
                {
                    settings.CopyFrom(layout);
                    var validator = PlotSettingsValidator.Current;
                    validator.SetPlotConfigurationName(settings, "DWG To PDF.pc3", null);
                    validator.RefreshLists(settings);
                    var media = validator.GetCanonicalMediaNameList(settings).Cast<string>().First(s => s.Contains("A4") && s.Contains("210.00"));
                    validator.SetCanonicalMediaName(settings, media);
                    validator.SetPlotType(settings, Autodesk.AutoCAD.DatabaseServices.PlotType.Extents);
                    validator.SetPlotCentered(settings, true);
                    validator.SetUseStandardScale(settings, true);
                    validator.SetStdScaleType(settings, StdScaleType.ScaleToFit);
                    info.Layout = layout.ObjectId; info.OverrideSettings = settings;
                    using (var check = new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled }) check.Validate(info);
                    using (var engine = PlotFactory.CreatePublishEngine())
                    using (var page = new PlotPageInfo())
                    {
                        engine.BeginPlot(null, null);
                        engine.BeginDocument(info, document.Name, null, 1, true, Path.Combine(scratch, "symbol-preview.pdf"));
                        engine.BeginPage(page, info, true, null);
                        engine.BeginGenerateGraphics(null); engine.EndGenerateGraphics(null);
                        engine.EndPage(null); engine.EndDocument(null); engine.EndPlot(null);
                    }
                }
            }
            File.WriteAllText(Path.Combine(scratch, "preview.log"), "PREVIEW_PASS");
        }
        catch (System.Exception ex) { File.WriteAllText(Path.Combine(scratch, "preview.log"), "PREVIEW_FAIL: " + ex); }
    }
}
