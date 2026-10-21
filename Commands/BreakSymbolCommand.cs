// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using Autodesk.AutoCAD.Runtime;

namespace AutocadTools.Commands
{
    public sealed class BreakSymbolCommand
    {
        [CommandMethod("ATBREAK", CommandFlags.Modal)]
        [CommandMethod("АТРАЗРЫВ", CommandFlags.Modal)]
        public void Run() => DrawingSymbolCommands.Run(false);
    }
}
