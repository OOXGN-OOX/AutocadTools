// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutocadTools.Services;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

public static class FormatChecks
{
    public static void Run()
    {
        var doc = AcadApp.DocumentManager.MdiActiveDocument;
        var definition = FormatInsertService.EnsureDefinition(doc.Database);
        foreach (var scale in new[] { 1, 100 })
        {
            var id = FormatInsertService.Insert(doc.Database, definition, new Point3d(scale * 2000, 0, 0), scale);
            try
            {
                object[] forms;
                string propertyName;
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var block = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                    var properties = block.DynamicBlockReferencePropertyCollection.Cast<DynamicBlockReferenceProperty>().ToList();
                    foreach (var property in properties)
                        doc.Editor.WriteMessage("\nFORMAT_PROPERTY: " + property.PropertyName + " = " + property.Value + "; allowed=" + string.Join(",", property.GetAllowedValues()));
                    var form = properties.Single(p => p.PropertyName.Contains("форма", StringComparison.OrdinalIgnoreCase) && !p.PropertyName.Contains("формат", StringComparison.OrdinalIgnoreCase));
                    forms = form.GetAllowedValues();
                    propertyName = form.PropertyName;
                }
                if (forms.Length < 4) throw new InvalidOperationException("Expected at least four stamp forms");
                foreach (var value in forms)
                {
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        var block = (BlockReference)tr.GetObject(id, OpenMode.ForWrite);
                        var form = block.DynamicBlockReferencePropertyCollection.Cast<DynamicBlockReferenceProperty>().Single(p => p.PropertyName == propertyName);
                        form.Value = value;
                        tr.Commit();
                    }
                    var item = new BatchPlotService { PlotScale = scale }.GetFormatBlocks().Single(i => i.BlockId == id);
                    if (item.FormatDefinition?.Name != "A3")
                        throw new InvalidOperationException("Stamp form " + value + " at scale " + scale + " detected as " + item.Format);
                    doc.Editor.WriteMessage("\nFORMAT_FORM_PASS: " + value + ", 1:" + scale + ", " + item.Format);
                }
                // A field outside the paper must never change paper detection or plot bounds.
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var block = (BlockReference)tr.GetObject(id, OpenMode.ForWrite);
                    var attribute = new AttributeReference { Tag = "REGRESSION", TextString = "Outside paper", Position = new Point3d(scale * 5000, 0, 0), Height = scale * 10, Invisible = true };
                    block.AttributeCollection.AppendAttribute(attribute);
                    tr.AddNewlyCreatedDBObject(attribute, true);
                    tr.Commit();
                }
                var after = new BatchPlotService { PlotScale = scale }.GetFormatBlocks().Single(i => i.BlockId == id);
                if (after.FormatDefinition?.Name != "A3" || Math.Abs(after.Extents.MaxPoint.X - after.Extents.MinPoint.X - 420 * scale) > 0.01)
                    throw new InvalidOperationException("Outlying invisible attribute changed paper bounds");
            }
            finally
            {
                using var tr = doc.Database.TransactionManager.StartTransaction();
                tr.GetObject(id, OpenMode.ForWrite).Erase();
                tr.Commit();
            }
        }
        doc.Editor.WriteMessage("\nFORMAT_STAMP_PASS: all dynamic forms, scales 1:1/1:100 and outlying invisible attributes\n");
    }
}
