// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Services;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

public sealed class LibraryChecks
{
    [CommandMethod("AUTOCADTOOLS_LIBRARYSETUP")]
    public void Setup()
    {
        var folder = Path.GetDirectoryName(AcadApp.DocumentManager.MdiActiveDocument.Name);
        var root = Path.Combine(folder, "library-fixture");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "helper.lsp"), "(setvar \"USERS5\" \"helper-loaded\")(princ)");
        var file = Path.Combine(root, "entry.lsp");
        File.WriteAllText(file, "(defun c:bp-library-test () (load \"helper.lsp\") (princ))\n(defun c:bp-library-error () (/ 1 0))");
        string invoke = LispCatalog.BuildExpression(new LispEntry(file, "entry.lsp", "bp-library-test"));
        string fail = LispCatalog.BuildExpression(new LispEntry(file, "entry.lsp", "bp-library-error"));
        var bundled = LispCatalog.Read().Single(e => e.Command == "Подписи");
        string loadBundled = LispCatalog.BuildExpression(bundled with { Command = null, Symbol = null });
        string script = "(setq botplus:test-paths (getenv \"ACAD\"))\n" + invoke +
            "(if (/= (getvar \"USERS5\") \"helper-loaded\") (setvar \"USERS5\" \"failure\"))\n" + fail + loadBundled +
            "(if (and (= (getvar \"USERS5\") \"helper-loaded\") (equal botplus:test-paths (getenv \"ACAD\"))) (setvar \"USERS5\" \"library-ok\") (setvar \"USERS5\" \"failure\"))\n" +
            "(defun c:bp-library-insert () (eval (list (read \"c:Подписи\"))))\n";
        File.WriteAllText(Path.Combine(folder, "library-dispatch.lsp"), script, new UTF8Encoding(false));
    }
    [CommandMethod("AUTOCADTOOLS_LIBRARYVERIFY")]
    public void Verify()
    {
        var document = AcadApp.DocumentManager.MdiActiveDocument;
        try
        {
            if ((string)AcadApp.GetSystemVariable("USERS5") != "library-ok") throw new InvalidOperationException("LISP dispatch/helper/error cleanup failed");
            using var tr = document.Database.TransactionManager.StartTransaction();
            var table = (BlockTable)tr.GetObject(document.Database.BlockTableId, OpenMode.ForRead);
            var model = (BlockTableRecord)tr.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            bool inserted = model.Cast<ObjectId>().Select(id => tr.GetObject(id, OpenMode.ForRead)).OfType<BlockReference>()
                .Any(b => b.Name == "!Подписи");
            if (!inserted) throw new InvalidOperationException("Native insert from bundled DWG did not complete");
            document.Editor.WriteMessage("\nLIBRARY_PASS: bundled DWG inserted; LISP execution, helper resolution, failure handling and support-path restoration\n");
        }
        catch (System.Exception ex) { document.Editor.WriteMessage("\nLIBRARY_FAIL: " + ex + "\n"); }
    }
}
