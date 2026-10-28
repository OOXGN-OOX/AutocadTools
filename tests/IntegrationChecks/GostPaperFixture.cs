// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutocadTools.Models;
using AutocadTools.Services;

public static class GostPaperFixture
{
    // Independent transcription of GOST 2.301-68 tables 1/2, including optional A5.
    private static readonly (string Name, double Width, double Height)[] Papers =
    {
        ("A0",1189,841), ("A1",841,594), ("A2",594,420), ("A3",420,297), ("A4",297,210), ("A5",210,148),
        ("A0x2",1682,1189), ("A0x3",2523,1189), ("A1x3",1783,841), ("A1x4",2378,841),
        ("A2x3",1261,594), ("A2x4",1682,594), ("A2x5",2102,594),
        ("A3x3",891,420), ("A3x4",1189,420), ("A3x5",1486,420), ("A3x6",1783,420), ("A3x7",2080,420),
        ("A4x3",630,297), ("A4x4",841,297), ("A4x5",1051,297), ("A4x6",1261,297),
        ("A4x7",1471,297), ("A4x8",1682,297), ("A4x9",1892,297)
    };

    public static List<BatchPlotItem> Build(Database database)
    {
        var result = new List<BatchPlotItem>();
        var catalog = new BatchPlotService();
        using var tr = database.TransactionManager.StartTransaction();
        var table = (BlockTable)tr.GetObject(database.BlockTableId, OpenMode.ForWrite);
        var model = (BlockTableRecord)tr.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        foreach (int scale in new[] { 1, 100 })
        foreach (var paper in Papers)
        foreach (bool portrait in new[] { false, true })
        {
            var format = catalog.GetFormatDefinition(paper.Name);
            if (format == null || format.Width != paper.Width || format.Height != paper.Height)
                throw new InvalidOperationException("GOST catalog mismatch: " + paper.Name);
            double width = portrait ? paper.Height : paper.Width, height = portrait ? paper.Width : paper.Height;
            string name = paper.Name + (portrait ? " v" : "");
            var definition = new BlockTableRecord { Name = "AUTOCADTOOLS_GOST14_" + result.Count };
            table.Add(definition); tr.AddNewlyCreatedDBObject(definition, true);
            var outline = new Polyline();
            outline.AddVertexAt(0, Point2d.Origin, 0, 0, 0);
            outline.AddVertexAt(1, new Point2d(width, 0), 0, 0, 0);
            outline.AddVertexAt(2, new Point2d(width, height), 0, 0, 0);
            outline.AddVertexAt(3, new Point2d(0, height), 0, 0, 0);
            outline.Closed = true;
            definition.AppendEntity(outline); tr.AddNewlyCreatedDBObject(outline, true);
            foreach (double x in new[] { 10.0, width - 12 })
            foreach (double y in new[] { 10.0, height - 12 })
            {
                var marker = new Solid(new Point3d(x, y, 0), new Point3d(x + 2, y, 0),
                    new Point3d(x, y + 2, 0), new Point3d(x + 2, y + 2, 0));
                definition.AppendEntity(marker); tr.AddNewlyCreatedDBObject(marker, true);
            }
            var label = new DBText { Position = new Point3d(20, height / 2, 0), Height = 4, TextString = name + " 1:" + scale };
            definition.AppendEntity(label); tr.AddNewlyCreatedDBObject(label, true);
            var block = new BlockReference(new Point3d(-(result.Count + 1) * 6000.0 * scale, -10000 * scale, 0), definition.ObjectId)
            { ScaleFactors = new Scale3d(scale) };
            model.AppendEntity(block); tr.AddNewlyCreatedDBObject(block, true);
            result.Add(new BatchPlotItem { BlockId = block.ObjectId, Extents = block.GeometricExtents, PlotScale = scale,
                Format = name, FormatDefinition = format, Name = name, SheetNumber = (result.Count + 1).ToString(), IsSelected = true });
        }
        tr.Commit();
        return result;
    }
}
