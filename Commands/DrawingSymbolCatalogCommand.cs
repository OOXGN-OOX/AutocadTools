// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Views;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutocadTools.Commands
{
    public sealed class DrawingSymbolCatalogCommand
    {
        private static AutocadTools.Models.DrawingSymbolKind _last;
        [CommandMethod("ATSYMBOL", CommandFlags.Modal)]
        [CommandMethod("АТУСЛОВНЫЕ", CommandFlags.Modal)]
        public void Run()
        {
            if (AcadApp.DocumentManager.MdiActiveDocument == null) return;
            var window = new DrawingSymbolCatalogWindow(_last);
            if (AcadApp.ShowModalWindow(window) == true)
            {
                _last = window.Selection.Kind;
                DrawingSymbolCommands.Run(window.Selection.Defaults().IsSection, window.Selection.Defaults());
            }
        }
    }
}
