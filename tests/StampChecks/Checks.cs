// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Models;
using AutocadTools.Services;
using AutocadTools.ViewModels;

public sealed class Checks
{
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    [CommandMethod("STAMP_CHECKS")]
    public void Run()
    {
        var log = new List<string>();
        string directory = Environment.GetEnvironmentVariable("AUTOCADTOOLS_STAMP_SCRATCH") ?? Path.Combine(Path.GetTempPath(), "AutocadTools-StampChecks");
        try
        {
            var fake = new MemoryService();
            fake.Properties.SetValue("Наименование_объекта1", "Старое"); fake.Properties.SetValue("Наименование_объекта2", "название");
            var model = new StampPropsViewModel(fake); model.LoadValues();
            Require(model.НаименованиеОбъекта == "Старое название", "Legacy title was not merged.");
            foreach (int length in new[] { 1, 64, 128, 256, 1000 })
            {
                model.НаименованиеОбъекта = new string('Ж', length); model.UpdateCommand.Execute(null); model.LoadValues();
                Require(model.НаименованиеОбъекта.Length == length, "Full title was truncated.");
                Require(fake.Properties.GetValue("Наименование_объекта") == model.НаименованиеОбъекта, "Canonical property is missing.");
                Require(fake.Properties.GetValue("Наименование_объекта1") == model.НаименованиеОбъекта, "Legacy alias was truncated.");
            }
            model.НаименованиеОбъекта = ""; model.UpdateCommand.Execute(null); model.LoadValues();
            Require(model.НаименованиеОбъекта == "", "Cleared canonical property restored a legacy value.");
            log.Add("PASS: canonical full properties, legacy aliases, empty values and 1000-character titles.");

            var visual = new DrawingVisual();
            using (var draw = visual.RenderOpen())
            {
                draw.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, 120, 36));
                draw.DrawLine(new Pen(Brushes.Black, 2), new System.Windows.Point(10, 28), new System.Windows.Point(80, 8));
                draw.DrawLine(new Pen(Brushes.Black, 2), new System.Windows.Point(40, 10), new System.Windows.Point(100, 26));
            }
            var bitmap = new RenderTargetBitmap(120, 36, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var signature = new StampSignature { Name = "Test", Png = SignatureLibrary.Normalize(bitmap) };
            var signatures = new Dictionary<string, StampSignature> { ["Signer1"] = signature, ["Revision1"] = signature };
            string drawingPath = Path.Combine(directory, "signed.dwg");
            using (var database = new Database(true, true))
            {
                ObjectId definition = FormatInsertService.EnsureDefinition(database, Environment.GetEnvironmentVariable("AUTOCADTOOLS_STAMP_TEMPLATE"));
                ObjectId inserted = FormatInsertService.Insert(database, definition, new Point3d(10000, 20000, 0));
                var originalWorking = HostApplicationServices.WorkingDatabase;
                HostApplicationServices.WorkingDatabase = database;
                try
                {
                    var typography = StampTextLayoutService.ReadTypography(database);
                    Require(typography.Code.Height == 5 && typography.Project.Height == 2.5 && typography.Company.Height == 2.5, "Unexpected native stamp heights.");
                    var summary = new DatabaseSummaryInfoBuilder(database.SummaryInfo);
                    summary.CustomPropertyTable["Обозначение_документа"] = "012.01-ОВ-Р";
                    summary.CustomPropertyTable["Наименование_объекта"] = new string('0', 180);
                    summary.CustomPropertyTable["Наименование_участка"] = "Отопление и вентиляция. Корпус 2";
                    database.SummaryInfo = summary.ToDatabaseSummaryInfo();
                    StampTextLayoutService.Apply(database);
                    using (var tr = database.TransactionManager.StartTransaction())
                    {
                        var frame = (BlockReference)tr.GetObject(inserted, OpenMode.ForRead);
                        foreach (ObjectId aid in frame.AttributeCollection)
                        {
                            var attribute = (AttributeReference)tr.GetObject(aid, OpenMode.ForRead);
                            if (attribute.Tag != "ОБЪЕКТ") continue;
                            Require(attribute.IsMTextAttribute, "Project name stayed single-line.");
                            using (var text = attribute.MTextAttribute)
                            {
                                log.Add("Layout: height=" + text.TextHeight + " width=" + text.Width + " actual=" + text.ActualWidth + "x" + text.ActualHeight);
                                Require(Math.Abs(text.TextHeight - 2.5) < 1e-6, "Native text height changed.");
                                Require(text.ActualWidth <= 118.001 && text.ActualHeight <= 14.001 && text.ActualHeight > 3, "Long field did not wrap within its cell.");
                                Require(text.Text.Contains(new string('0', 30)), "Field text was lost during multiline conversion.");
                            }
                        }
                    }
                    summary.CustomPropertyTable["Наименование_объекта"] = new string('0', 1000);
                    database.SummaryInfo = summary.ToDatabaseSummaryInfo();
                    bool rejected = false;
                    try { StampTextLayoutService.Apply(database); } catch (InvalidOperationException) { rejected = true; }
                    Require(rejected, "An oversized value escaped the title block instead of being rejected.");
                    summary.CustomPropertyTable["Наименование_объекта"] = new string('0', 180); database.SummaryInfo = summary.ToDatabaseSummaryInfo();
                    StampTextLayoutService.Apply(database);
                    using (var tr = database.TransactionManager.StartTransaction())
                    {
                        var frame = (BlockReference)tr.GetObject(inserted, OpenMode.ForRead);
                        var building = frame.AttributeCollection.Cast<ObjectId>().Select(a => (AttributeReference)tr.GetObject(a, OpenMode.ForRead)).First(a => a.Tag == "КОМПЛЕКТ");
                        Require(building.IsMTextAttribute && building.HasFields, "Normal words lost the property field link.");
                        using (var text = building.MTextAttribute) Require(text.ActualWidth <= 68.001 && text.TextHeight == 2.5, "Normal text exceeded its cell or changed font height.");
                    }
                    log.Add("PASS: native font heights, wrapping of an unbroken field, bounded cell and overflow rejection.");
                }
                finally { HostApplicationServices.WorkingDatabase = originalWorking; }
                StampSignatureDwgService.Save(database, signatures);
                CheckReferences(database, definition, 2);
                var before = CountSolids(database);
                Require(before > 0, "No embedded blue geometry was created.");
                StampSignatureDwgService.Save(database, signatures);
                CheckReferences(database, definition, 2);
                Require(CountSolids(database) == before, "Repeated save duplicated ink geometry.");
                log.Add("PASS: two signature slots, embedded blue geometry, shared signature definition, idempotent apply.");
                using (var transaction = database.TransactionManager.StartTransaction())
                {
                    var reference = (BlockReference)transaction.GetObject(inserted, OpenMode.ForWrite);
                    foreach (DynamicBlockReferenceProperty property in reference.DynamicBlockReferencePropertyCollection)
                    {
                        var values = property.GetAllowedValues();
                        if (!property.ReadOnly && values.Length > 1)
                        { property.Value = values[1]; log.Add("Dynamic variant: " + property.PropertyName + " = " + values[1]); break; }
                    }
                    transaction.Commit();
                }
                StampSignatureDwgService.Synchronize(database);
                HostApplicationServices.WorkingDatabase = database;
                try { StampTextLayoutService.Apply(database); }
                finally { HostApplicationServices.WorkingDatabase = originalWorking; }
                using (var transaction = database.TransactionManager.StartTransaction())
                {
                    var reference = (BlockReference)transaction.GetObject(inserted, OpenMode.ForRead);
                    CheckReferences(database, reference.BlockTableRecord, 2);
                }
                database.SaveAs(drawingPath, DwgVersion.Current);
            }
            using (var reopened = new Database(false, true))
            {
                reopened.ReadDwgFile(drawingPath, FileOpenMode.OpenForReadAndAllShare, false, null); reopened.CloseInput(true);
                var stored = StampSignatureDwgService.Load(reopened);
                Require(stored.Count == 2 && stored["Signer1"].Png.SequenceEqual(signature.Png), "Signature was lost after DWG reopen.");
                Require(CountSolids(reopened) > 0, "Embedded geometry was lost after DWG reopen.");
                StampSignatureDwgService.Save(reopened, new Dictionary<string, StampSignature>());
                using (var transaction = reopened.TransactionManager.StartTransaction())
                {
                    var table = (BlockTable)transaction.GetObject(reopened.BlockTableId, OpenMode.ForRead);
                    CheckReferences(reopened, table[FormatInsertService.BlockName], 0);
                }
            }
            log.Add("STAMP_PASS: dynamic format, DWG save/reopen, portable PNG data and signature removal.");
        }
        catch (System.Exception ex) { log.Add("STAMP_FAIL: " + ex); }
        File.WriteAllLines(Path.Combine(directory, "results.log"), log);
    }
    private static int CountSolids(Database database)
    {
        int count = 0;
        using (var transaction = database.TransactionManager.StartTransaction())
        {
            var table = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId id in table)
            {
                var block = (BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead);
                if (!block.Name.StartsWith("AT_SIG_")) continue;
                foreach (ObjectId entityId in block)
                {
                    var solid = transaction.GetObject(entityId, OpenMode.ForRead) as Solid;
                    Require(solid != null && solid.Color.Red == 30 && solid.Color.Green == 80 && solid.Color.Blue == 181, "Ink is not blue.");
                    count++;
                }
            }
        }
        return count;
    }
    private static void CheckReferences(Database database, ObjectId id, int expected)
    {
        using (var transaction = database.TransactionManager.StartTransaction())
        {
            var block = (BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead);
            var signs = block.Cast<ObjectId>().Select(e => transaction.GetObject(e, OpenMode.ForRead)).OfType<BlockReference>()
                .Where(b => b.Name.StartsWith("AT_SIG_")).ToArray();
            Require(signs.Length == expected, "Expected " + expected + " signature references in " + block.Name + "; got " + signs.Length);
            foreach (var sign in signs) Require(sign.Position.X > 200 && sign.Position.X < 1000 && sign.Position.Y < 100, "Signature escaped title block coordinates.");
        }
    }
    private sealed class MemoryService : IAutoCADService
    {
        public StampProperties Properties = new StampProperties();
        public StampProperties LoadStampProperties() => Properties;
        public void SaveStampProperties(StampProperties properties) { Properties = properties; }
    }
}
