// Copyright (c) 2026 OOXGN (GitHub: OOXGN-OOX). All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
using Autodesk.AutoCAD.Runtime;
using AutocadTools.Services;
namespace AutocadTools.Commands
{
    public sealed class CheckUpdatesCommand
    {
        [CommandMethod("AUTOCADTOOLS_CHECKUPDATES")]
        public void Run() => UpdateCheckService.Check(true);
    }
}
