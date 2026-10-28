// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.Diagnostics;

namespace AutocadTools.Services
{
    internal static class PublishedOutputService
    {
        internal static void Open(string output, bool separateSheets, bool openFolder, bool openFile, string firstSheetNumber, Action<string> warning, string firstDeliveredFile = null)
        {
            foreach (var target in PdfOutputPaths.OpenTargets(output, separateSheets, openFolder, openFile, firstSheetNumber, firstDeliveredFile))
            {
                try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose(); }
                catch (Exception ex)
                {
                    Infrastructure.PluginLog.Error("Open published output", ex);
                    warning?.Invoke("PDF сохранён, но не удалось открыть: " + target + ". " + ex.Message);
                }
            }
        }
    }
}
