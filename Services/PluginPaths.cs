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
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutocadTools");
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
