// Copyright (c) 2026 OOXGN (GitHub: OOXGN-OOX). All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Services;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutocadTools.Commands
{
    public sealed class InsertFormatCommand
    {
        [CommandMethod("ATFRAME", CommandFlags.Modal)]
        [CommandMethod("АТРАМКА", CommandFlags.Modal)]
        public void Run()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var point = doc.Editor.GetPoint("\nУкажите точку вставки форматки WM_FORMAT: ");
            if (point.Status != PromptStatus.OK) return;
            try
            {
                using (doc.LockDocument())
                {
                    var definition = FormatInsertService.EnsureDefinition(doc.Database);
                    FormatInsertService.Insert(doc.Database, definition, point.Value.TransformBy(doc.Editor.CurrentUserCoordinateSystem), SettingsService.Settings.FrameInsertionScale);
                }
                doc.Editor.Regen();
                doc.Editor.WriteMessage("\nФорматка WM_FORMAT вставлена на слой WM_FORMAT.\n");
            }
            catch (System.Exception ex) { doc.Editor.WriteMessage("\n[Autocad Tools] Не удалось вставить форматку: " + ex.Message + "\n"); }
        }
    }
}
