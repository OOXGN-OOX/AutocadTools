// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.IO;

namespace AutocadTools.Infrastructure
{
    internal static class AppDataPaths
    {
        internal static string UserDirectory
        {
            get
            {
                // Allows an isolated data folder for headless validation/managed deployments.
                var configured = Environment.GetEnvironmentVariable("AUTOCADTOOLS_DATA_DIRECTORY");
                var path = string.IsNullOrWhiteSpace(configured)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutocadTools")
                    : Path.GetFullPath(configured);
                Directory.CreateDirectory(path);
                return path;
            }
        }
    }
}
