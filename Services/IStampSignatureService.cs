// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System.Collections.Generic;
using AutocadTools.Models;
namespace AutocadTools.Services
{
    public interface IStampSignatureService
    {
        Dictionary<string, StampSignature> LoadSignatures();
        void SaveStamp(StampProperties properties, Dictionary<string, StampSignature> signatures);
    }
}
