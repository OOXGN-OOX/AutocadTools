// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.Collections.Generic;
using System.IO;

namespace AutocadTools.Services
{
    internal static class PdfOutputPaths
    {
        internal static string SheetFileName(int index, string sheetNumber)
        {
            string number = sheetNumber ?? (index + 1).ToString();
            foreach (char invalid in Path.GetInvalidFileNameChars()) number = number.Replace(invalid, '_');
            return $"{index + 1:D3}_Лист_{number}.pdf";
        }
        internal static string[] OpenTargets(string output, bool separateSheets, bool openFolder, bool openFile, string firstSheetNumber, string firstDeliveredFile = null)
        {
            var targets = new List<string>();
            if (openFolder) targets.Add(separateSheets ? output : Path.GetDirectoryName(Path.GetFullPath(output)));
            if (openFile) targets.Add(firstDeliveredFile ?? (separateSheets ? Path.Combine(output, SheetFileName(0, firstSheetNumber)) : output));
            return targets.ToArray();
        }

        internal static string CompleteDelivery(string temporary, string destination, bool keepExisting)
        {
            if (!keepExisting) { File.Move(temporary, destination, overwrite: true); return destination; }
            string directory = Path.GetDirectoryName(destination);
            string name = Path.GetFileNameWithoutExtension(destination);
            string extension = Path.GetExtension(destination);
            for (int index = 1; ; index++)
            {
                string candidate = index == 1 ? destination : Path.Combine(directory, $"{name}_{index}{extension}");
                if (File.Exists(candidate) || Directory.Exists(candidate)) continue;
                // Moving without overwrite also protects against another process
                // taking the name after the existence check.
                try { File.Move(temporary, candidate, overwrite: false); return candidate; }
                catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate)) { }
            }
        }
    }
}
