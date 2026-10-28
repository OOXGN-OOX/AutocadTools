// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Autodesk.AutoCAD.PlottingServices;

namespace AutocadTools.Services
{
    internal sealed record DsdSheet(string DwgName, string Layout, string Nps, string Title);
    internal static class DsdFileWriter
    {
        [DllImport("kernel32.dll")]
        private static extern uint GetACP();

        internal static void Write(string path, string pdf, IReadOnlyList<DsdSheet> sheets)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using var entries = new DsdEntryCollection();
                foreach (var descriptor in sheets)
                {
                    using var entry = new DsdEntry
                    {
                        DwgName = descriptor.DwgName,
                        Layout = descriptor.Layout,
                        NpsSourceDwg = descriptor.DwgName,
                        Nps = descriptor.Nps,
                        Title = descriptor.Title
                    };
                    entries.Add(entry);
                }
                using (var data = new DsdData())
                {
                    data.DestinationName = pdf;
                    data.ProjectPath = Path.GetDirectoryName(Path.GetFullPath(pdf));
                    // Preserve each page setup's device. PDF mode in -PUBLISH
                    // overrides it with the user's PDF preset and can clip long media.
                    data.SheetType = SheetType.OriginalDevice;
                    data.NoOfCopies = 1;
                    data.SetDsdEntryCollection(entries);
                    data.WriteDsd(temporary);
                }
                // The native serializer can omit NPS and writes the Windows ANSI code page.
                // Preserve its schema and use the Windows INI Unicode format (UTF-16 LE).
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                var lines = File.ReadAllLines(temporary, Encoding.GetEncoding((int)GetACP()));
                int sheet = -1;
                bool inSheet = false;
                for (int line = 0; line < lines.Length; line++)
                {
                    if (lines[line].StartsWith("[", StringComparison.Ordinal))
                        inSheet = lines[line].StartsWith("[DWF6Sheet:", StringComparison.Ordinal);
                    if (inSheet && lines[line].StartsWith("[DWF6Sheet:", StringComparison.Ordinal))
                        lines[line] = "[DWF6Sheet:" + sheets[++sheet].Title + "]";
                    else if (inSheet && lines[line].StartsWith("DWG=", StringComparison.Ordinal)) lines[line] = "DWG=" + sheets[sheet].DwgName;
                    else if (inSheet && lines[line].StartsWith("Layout=", StringComparison.Ordinal)) lines[line] = "Layout=" + sheets[sheet].Layout;
                    else if (inSheet && lines[line].StartsWith("Setup=", StringComparison.Ordinal)) lines[line] = "Setup=" + sheets[sheet].Nps + "|" + sheets[sheet].DwgName;
                    else if (inSheet && lines[line].StartsWith("OriginalSheetPath=", StringComparison.Ordinal)) lines[line] = "OriginalSheetPath=" + sheets[sheet].DwgName;
                    else if (lines[line].StartsWith("PromptForDwfName=", StringComparison.Ordinal)) lines[line] = "PromptForDwfName=FALSE";
                    else if (lines[line].StartsWith("DWF=", StringComparison.Ordinal)) lines[line] = "DWF=" + pdf;
                    else if (lines[line].StartsWith("OUT=", StringComparison.Ordinal)) lines[line] = "OUT=" + Path.GetDirectoryName(Path.GetFullPath(pdf));
                }
                File.WriteAllLines(temporary, lines, Encoding.Unicode);
                File.AppendAllText(temporary, "[PdfOptions]\nIncludeHyperlinks=TRUE\nCreateBookmarks=TRUE\nCaptureFontsInDrawing=TRUE\nConvertTextToGeometry=FALSE\nVectorResolution=600\nRasterResolution=400\n", Encoding.Unicode);
                using (var verify = new DsdData())
                {
                    verify.ReadDsd(temporary);
                    using var loaded = verify.GetDsdEntryCollection();
                    if (loaded.Count != entries.Count) throw new InvalidDataException("AutoCAD не смог прочитать все листы созданного DSD.");
                    int index = 0;
                    foreach (DsdEntry entry in loaded)
                    {
                        var expected = sheets[index++];
                        if (entry.Nps != expected.Nps || entry.Title != expected.Title || entry.DwgName != expected.DwgName || entry.NpsSourceDwg != expected.DwgName)
                            throw new InvalidDataException("AutoCAD не смог прочитать настройки листа DSD.");
                    }
                }
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
