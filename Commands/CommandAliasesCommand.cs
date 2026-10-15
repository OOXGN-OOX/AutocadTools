// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Services;
using AutocadTools.Views;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutocadTools.Commands
{
    public sealed class CommandAliasesCommand
    {
        [CommandMethod("ATALIASES", CommandFlags.Modal)]
        [CommandMethod("АТСОКРАЩЕНИЯ", CommandFlags.Modal)]
        public void Execute()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                string path = HostApplicationServices.Current.FindFile("acad.pgp", doc.Database, FindFileHint.Default);
                var window = new CommandAliasesWindow(new CommandAliases(path));
                if (AcadApp.ShowModalWindow(window) != true) return;
                try
                {
                    AcadApp.SetSystemVariable("RE-INIT", 16);
                    doc.Editor.WriteMessage("\nСокращения сохранены и доступны в текущем сеансе.");
                }
                catch (System.Exception ex)
                {
                    doc.Editor.WriteMessage("\nPGP сохранён. Выполните REINIT и выберите «Файл PGP», чтобы применить сокращения. " + ex.Message);
                }
            }
            catch (System.Exception ex) { doc.Editor.WriteMessage("\nНе удалось открыть сокращения: " + ex.Message); }
        }
    }
}
