// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using Autodesk.AutoCAD.Runtime;
using AutocadTools.Views;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutocadTools.Commands
{
    public class InfoCommand
    {
        [CommandMethod("AUTOCADTOOLS_INFO")]
        public void Execute()
        {
            var window = new AboutWindow();
            // Показываем как модальное окно поверх AutoCAD
            AcadApp.ShowModalWindow(window);
        }
    }
}