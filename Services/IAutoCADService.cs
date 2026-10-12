// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using AutocadTools.Models;

namespace AutocadTools.Services
{
    /// <summary>
    /// Service interface for AutoCAD database operations
    /// </summary>
    public interface IAutoCADService
    {
        StampProperties LoadStampProperties();
        void SaveStampProperties(StampProperties properties);
    }
}

