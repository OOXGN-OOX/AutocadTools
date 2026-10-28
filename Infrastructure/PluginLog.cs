// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.IO;
using System.Text;

namespace AutocadTools.Infrastructure
{
    /// <summary>Local diagnostic log. Logging must never throw into AutoCAD callbacks.</summary>
    internal static class PluginLog
    {
        private const long MaximumBytes = 1024 * 1024;
        private static readonly object Gate = new object();

        public static void Error(string operation, Exception error)
        {
            try
            {
                lock (Gate)
                {
                    var directory = Path.Combine(AppDataPaths.UserDirectory, "Logs");
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, "plugin.log");
                    if (File.Exists(path) && new FileInfo(path).Length >= MaximumBytes)
                        File.Move(path, path + ".previous", overwrite: true);
                    File.AppendAllText(path, $"{DateTime.UtcNow:O} [{operation}] {error}\n", Encoding.UTF8);
                }
            }
            catch { /* A diagnostic failure must not replace the original failure. */ }
        }
    }
}
