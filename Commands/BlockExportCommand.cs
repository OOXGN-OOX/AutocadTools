// Copyright (c) 2026 OOXGN (GitHub: OOXGN-OOX). All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Services;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutocadTools.Commands
{
    public sealed class BlockExportCommand
    {
        [CommandMethod("AUTOCADTOOLS_BLOCKS_STAGE1", CommandFlags.Modal)]
        public void Run()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                BlockExportService.Run(doc, PluginPaths.CreateDsdPath());
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage($"\n[Autocad Tools] Ошибка подготовки блоков: {ex.Message}\n");
            }
        }
    }
}
