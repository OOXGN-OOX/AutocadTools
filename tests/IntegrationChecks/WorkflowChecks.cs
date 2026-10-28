// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AutocadTools.Models;
using AutocadTools.Services;
using AutocadTools.UI;
using AutocadTools.ViewModels;
using AutocadTools.Views;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

internal static class WorkflowChecks
{
    internal static void Run()
    {
        Plan(); Stamp(); Preferences(); Lisp();
        AcadApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nWORKFLOW_PASS: plan base point/color, full/blank stamp transfer, persisted options, LISP catalogue\n");
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Plan()
    {
        using var db = new Database(true, true);
        using var tr = db.TransactionManager.StartTransaction();
        var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var model = (BlockTableRecord)tr.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        var start = new Point3d(100, 200, 0);
        var line = new Line(start, new Point3d(300, 400, 0));
        model.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
        var options = new PlanPrepOptions();
        foreach (var prop in typeof(PlanPrepOptions).GetProperties()) prop.SetValue(options, false);
        options.SetGrayColor = options.CreateBlock = options.PickBasePoint = true;
        var chosen = new Point3d(-25, 65, 0);
        new PlanPrepService(options, db, tr, chosen).Process(new[] { line.ObjectId });
        var block = model.Cast<ObjectId>().Where(id => !id.IsErased).Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<BlockReference>().Single();
        Assert(block.Position.DistanceTo(chosen) < 1e-8, "Plan base point was ignored");
        var definition = (BlockTableRecord)tr.GetObject(block.BlockTableRecord, OpenMode.ForRead);
        var clone = definition.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<Line>().Single();
        Assert(clone.ColorIndex == 252, "Plan uses wrong gray color");
        Assert(clone.StartPoint.TransformBy(block.BlockTransform).DistanceTo(start) < 1e-8, "Plan base point moved geometry");
    }
    private sealed class StampSource : IAutoCADService
    {
        public StampProperties LoadStampProperties() => new();
        public void SaveStampProperties(StampProperties properties) { }
    }
    private static void Stamp()
    {
        var vm = new StampPropsViewModel(new StampSource()); vm.LoadValues();
        var field = vm.FieldsMap["Компания"];
        field.Value = " Компания";
        Assert(field.Value == "Компания", "Initial placeholder survived typing");
        field.Value = "Компания "; field.Value = "Компания А";
        Assert(field.Value == "Компания А", "Normal interword spaces were removed");
        field.Value = ""; Assert(field.Value == " ", "Empty placeholder was not restored");
        field.Value = " Компания А";
        vm.НаименованиеОбъекта = "Объект";
        var values = vm.CaptureValues(); var empty = vm.CaptureValues(true);
        Assert(empty.Properties.Count == StampSchema.Keys.Count() && empty.Properties.Values.All(v => v == " "), "Empty transfer copied source values or missed fields");
        using var db = new Database(true, true);
        var builder = new DatabaseSummaryInfoBuilder(db.SummaryInfo);
        builder.CustomPropertyTable["Unrelated"] = "Preserve";
        db.SummaryInfo = builder.ToDatabaseSummaryInfo();
        StampDrawingTransfer.Write(db, values);
        builder = new DatabaseSummaryInfoBuilder(db.SummaryInfo);
        Assert((string)builder.CustomPropertyTable["Компания"] == "Компания А" && (string)builder.CustomPropertyTable["Наименование_объекта1"] == "Объект", "DWG stamp values lost");
        StampDrawingTransfer.Write(db, empty);
        builder = new DatabaseSummaryInfoBuilder(db.SummaryInfo);
        Assert(StampSchema.Keys.All(k => (string)builder.CustomPropertyTable[k] == " ") && (string)builder.CustomPropertyTable["Unrelated"] == "Preserve", "Blank DWG transfer is incomplete or changed unrelated data");
    }
    private static void Preferences()
    {
        var factories = new (Func<System.Windows.Window> Window, Func<object> Model)[]
        {
            (() => new PlanPrepWindow(), () => new PlanPrepViewModel()),
            (() => new CleanDwgWindow(), () => new CleanDwgViewModel()),
            (() => new StripTextWindow(), () => new StripTextViewModel()),
            (() => new SelectSimilarWindow(), () => new SelectSimilarViewModel()),
            (() => new CustomerOrderWindow(), () => new CustomerOrderViewModel()),
            (() => new AutoNumSettingsWindow(new AutoNumSettingsViewModel()), () => new AutoNumSettingsViewModel())
        };
        foreach (var pair in factories)
        {
            object Options(object vm) => vm.GetType().GetProperty("Options")?.GetValue(vm) ?? vm.GetType().GetProperty("Settings")?.GetValue(vm) ?? vm;
            var model = pair.Model(); var options = Options(model);
            if (options is AutoNumSettings numbering) { numbering.Mode = AutoNumSettings.NumberingMode.Table; numbering.Prefix = "PERSIST"; numbering.Step = 2; }
            foreach (var p in options.GetType().GetProperties().Where(p => p.PropertyType == typeof(bool))) p.SetValue(options, false);
            var window = pair.Window(); window.DataContext = model; WindowPreferences.Save(window); window.Close();
            // Reload from disk, including false values, as on a new process startup.
            typeof(UserPreferences).GetField("_values", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            var next = pair.Window(); var nextModel = pair.Model(); next.DataContext = nextModel;
            WindowPreferences.Restore(next);
            var nextOptions = Options(nextModel);
            Assert(nextOptions.GetType().GetProperties().Where(p => p.PropertyType == typeof(bool)).All(p => !(bool)p.GetValue(nextOptions)), "Unchecked options were reset: " + next.GetType().Name);
            if (nextOptions is AutoNumSettings restored) Assert(restored.Mode == AutoNumSettings.NumberingMode.Table && restored.Prefix == "PERSIST" && restored.Step == 2, "Numbering parameters did not persist");
            next.Close();
        }
    }
    private static void Lisp()
    {
        var commands = LispCatalog.GetCommands("; (defun c:fake())\n;| (defun c:fake2()) |;\n(setq s \"(defun c:fake3())\")\n(defun c:real (/ a) (princ))\n(defun c:Русская () (princ))");
        Assert(commands.SequenceEqual(new[] { "real", "Русская" }), "LISP comments/strings parsed as commands");
        var fixture = Path.Combine(Path.GetDirectoryName(AcadApp.DocumentManager.MdiActiveDocument.Name), "lisp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "sample.lsp"), "(defun c:test-command () (princ))");
        File.Copy(Path.Combine(fixture, "sample.lsp"), Path.Combine(fixture, "duplicate.lsp"));
        File.WriteAllBytes(Path.Combine(fixture, "compiled.fas"), new byte[] { 1, 2, 3 });
        File.WriteAllText(Path.Combine(fixture, "unicode.lsp"), "(defun c:Русская () (princ))", System.Text.Encoding.Unicode);
        var entries = LispCatalog.Read(fixture);
        Assert(entries.Count == 3 && entries.Any(e => e.Command == "Русская") && entries.Any(e => e.FilePath.EndsWith(".fas", StringComparison.OrdinalIgnoreCase)), "LISP discovery, UTF-16 or duplicate handling failed");
        var expression = LispCatalog.BuildExpression(entries.First(e => e.Command != null));
        Assert(expression.Contains("(load ") && expression.Contains("(eval ") && expression.Contains(fixture.Replace('\\', '/')), "LISP dispatch path or invocation is invalid");
        var bundled = LispCatalog.Read();
        Assert(bundled.Count > 100 && bundled.All(e => !e.Title.Contains(".lsp", StringComparison.OrdinalIgnoreCase) && !e.Title.Contains(".fas", StringComparison.OrdinalIgnoreCase)), "Bundled menu uses filenames instead of functions");
        Assert(File.Exists(Path.Combine(LispCatalog.Root, "acad_pan", "acad_pr", "!Подписи.dwg")), "Bundled block dependency is missing");
        Assert(!File.ReadAllText(Path.Combine(LispCatalog.Root, "Lisp", "prizma.lsp")).Contains(@"C:\\acad_pro"), "Bundled script still depends on C:\\acad_pro");
        AcadApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage($"\nLISP_CATALOG_PASS: fixture commands, UTF-16, FAS, duplicates; {bundled.Count} bundled functions and native dependencies\n");
    }
}
