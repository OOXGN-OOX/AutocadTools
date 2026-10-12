// Copyright (c) 2026 OOXGN (GitHub: OOXGN-OOX). All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
namespace AutocadTools.Models
{
    public sealed class CustomerOrderOptions
    {
        public bool CreateNewFile { get; set; }
        public bool ExplodeBlocks { get; set; }
        public bool ExplodePolylines { get; set; }
        public bool ExplodeText { get; set; }
        public bool PurgeUnused { get; set; }
        public bool BlockCopy { get; set; }
    }
}
