// Copyright (c) 2026 OOXGN (GitHub: OOXGN-OOX). All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
using System;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Services;
using AutocadTools.Models;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

public class Smoke
{
    [CommandMethod("AUTOCADTOOLS_SMOKETEST")]
    public void Run()
    {
        var editor = AcadApp.DocumentManager.MdiActiveDocument.Editor;
        try
        {
            if (typeof(SettingsService).Assembly.GetName().Version != new Version(1, 2, 1, 0))
                throw new InvalidOperationException("Unexpected plugin version loaded");
            if (SettingsService.Settings == null) throw new InvalidOperationException("Settings initialization failed");
            if (ReleaseVersion.FindUpdate("[]", new Version(1, 2, 1)) != null)
                throw new InvalidOperationException("GitHub JSON parsing failed");
            if (typeof(SettingsService).Assembly.GetReferencedAssemblies().Any(a => a.Name == "System.Text.Json"))
                throw new InvalidOperationException("Plugin still directly references System.Text.Json");
            editor.WriteMessage("\nPASS: settings and GitHub JSON; no direct System.Text.Json reference; CLR " + Environment.Version);
            using (var database = new Database(true, true))
            using (var transaction = database.TransactionManager.StartTransaction())
            {
                var table = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
                var model = (BlockTableRecord)transaction.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                var line = new Line(Point3d.Origin, new Point3d(300, 400, 0));
                var id = model.AppendEntity(line);
                transaction.AddNewlyCreatedDBObject(line, true);
                var result = new LengthService().CalculateLengths(transaction, new[] { id });
                var length = result.SelectMany(group => group.Items).Sum(item => item.RawLength);
                if (Math.Abs(length - 500) > 0.0001) throw new InvalidOperationException("Length calculation failed");
                var arc = new Arc(Point3d.Origin, Vector3d.ZAxis, 100, 0, Math.PI / 2);
                var arcId = model.AppendEntity(arc); transaction.AddNewlyCreatedDBObject(arc, true);
                var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, 100);
                var circleId = model.AppendEntity(circle); transaction.AddNewlyCreatedDBObject(circle, true);
                var polyline = new Polyline();
                polyline.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                polyline.AddVertexAt(1, new Point2d(100, 0), 0, 0, 0);
                var polylineId = model.AppendEntity(polyline); transaction.AddNewlyCreatedDBObject(polyline, true);
                var ids = new[] { id, arcId, circleId, polylineId };
                var expected = 600 + Math.PI * 50;
                if (Math.Abs(SelectionLengthService.Calculate(transaction, ids) - expected) > 0.0001)
                    throw new InvalidOperationException("Status length filter/sum failed");
                line.EndPoint = new Point3d(600, 800, 0);
                if (Math.Abs(SelectionLengthService.Calculate(transaction, ids) - (expected + 500)) > 0.0001)
                    throw new InvalidOperationException("Status length did not reflect geometry edits");
                if (SelectionLengthService.Calculate(transaction, Array.Empty<ObjectId>()) != 0)
                    throw new InvalidOperationException("Empty status selection failed");
                var text = new StripTextService(new StripTextOptions());
                if (text.StripMtextFormatting("{\\C1;Test}").Contains("\\C1;")) throw new InvalidOperationException("Text reset failed");
            }
            var commandCount = typeof(AutocadTools.App).Assembly.GetTypes()
                .SelectMany(type => type.GetMethods()).SelectMany(method => method.GetCustomAttributes(typeof(CommandMethodAttribute), false)).Count();
            if (commandCount != 19) throw new InvalidOperationException("Missing commands: " + commandCount);
            using (var library = new Database(false, true))
            {
                library.ReadDwgFile(FormatInsertService.TemplatePath, FileOpenMode.OpenForReadAndAllShare, true, null);
                library.CloseInput(true);
                using (var transaction = library.TransactionManager.StartTransaction())
                {
                    var table = (BlockTable)transaction.GetObject(library.BlockTableId, OpenMode.ForRead);
                    editor.WriteMessage("\nFORMAT_SOURCE_NAMED_BLOCK: " + table.Has("WM_FORMAT") + "\n");
                }
            }
            using (var formatDatabase = new Database(true, true))
            {
                var definitionId = FormatInsertService.EnsureDefinition(formatDatabase);
                if (FormatInsertService.EnsureDefinition(formatDatabase) != definitionId) throw new InvalidOperationException("Definition duplicated");
                var position = new Point3d(100, 200, 0);
                var referenceId = FormatInsertService.Insert(formatDatabase, definitionId, position);
                using (var transaction = formatDatabase.TransactionManager.StartTransaction())
                {
                    var definition = (BlockTableRecord)transaction.GetObject(definitionId, OpenMode.ForRead);
                    var block = (BlockReference)transaction.GetObject(referenceId, OpenMode.ForRead);
                    if (definition.Name != "WM_FORMAT" || block.Layer != "WM_FORMAT" || block.Position.DistanceTo(position) > 0.0001)
                        throw new InvalidOperationException("Format name, layer or position incorrect");
                    var attributes = definition.Cast<ObjectId>().Select(id => transaction.GetObject(id, OpenMode.ForRead))
                        .OfType<AttributeDefinition>().Count(attribute => !attribute.Constant);
                    if (block.AttributeCollection.Count != attributes) throw new InvalidOperationException("Format attributes lost");
                    var extents = block.GeometricExtents;
                    if ((extents.MaxPoint - extents.MinPoint).Length <= 0) throw new InvalidOperationException("Empty format geometry");
                    editor.WriteMessage($"\nFORMAT_INSERT_PASS: WM_FORMAT, layer WM_FORMAT, attributes={attributes}, dynamic={definition.IsDynamicBlock}.\n");
                }
            }
            var stampService = new TestStampService();
            var stamp = new AutocadTools.ViewModels.StampPropsViewModel(stampService);
            for (int length = 1; length <= 128; length++)
            {
                foreach (var value in new[] { new string('X', length), string.Join(" ", Enumerable.Repeat("Проект", 22)).Substring(0, length).Trim() })
                {
                    stamp.LoadValues();
                    stamp.НаименованиеОбъекта = value;
                    stamp.UpdateCommand.Execute(null);
                    if (stampService.Properties.GetValue("Наименование_объекта1").Length > 64 || stampService.Properties.GetValue("Наименование_объекта2").Length > 64)
                        throw new InvalidOperationException("Stamp part exceeded 64 characters");
                    stamp.LoadValues();
                    if (stamp.НаименованиеОбъекта != value) throw new InvalidOperationException("Stamp round-trip failed at " + length);
                }
            }
            editor.WriteMessage("\nAUTOCADTOOLS_SMOKE_PASS: 19 commands, format import/insertion/attributes, status length, text reset, stamp round-trip.\n");
        }
        catch (System.Exception ex) { editor.WriteMessage("\nAUTOCADTOOLS_SMOKE_FAIL: " + ex + "\n"); }
    }

    private sealed class TestStampService : IAutoCADService
    {
        public StampProperties Properties = new StampProperties();
        public StampProperties LoadStampProperties() => Properties;
        public void SaveStampProperties(StampProperties properties) => Properties = properties;
    }
}
