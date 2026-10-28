// Copyright (c) 2026 OOXGN (GitHub: OOXGN-OOX). All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
using System;
using System.IO;

namespace AutocadTools.Services
{
    internal static class PluginPaths
    {
        public static string UserDataDirectory
        {
            get
            {
                var configured = Environment.GetEnvironmentVariable("AUTOCADTOOLS_DATA_DIRECTORY");
                var directory = string.IsNullOrWhiteSpace(configured) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutocadTools") : Path.GetFullPath(configured);
                Directory.CreateDirectory(directory);
                return directory;
            }
        }

        public static string CreateDsdPath()
        {
            var directory = Path.Combine(UserDataDirectory, "Publish");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "Publish-" + Guid.NewGuid().ToString("N") + ".dsd");
        }
    }
}
