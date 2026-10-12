// Copyright (c) 2026 OOXGN (GitHub: OOXGN-OOX). All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
using System;
namespace AutocadTools.Services
{
    public sealed class UpdateSchedule
    {
        public DateTime NextCheckUtc { get; private set; } = DateTime.MinValue;
        public bool IsDue(DateTime utcNow) => utcNow >= NextCheckUtc;
        public void Checked(DateTime utcNow) => NextCheckUtc = utcNow.AddHours(24);
        public void NewSession() => NextCheckUtc = DateTime.MinValue;
    }
}
