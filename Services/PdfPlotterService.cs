// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.PlottingServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutocadTools.Services
{
    public static class PdfPlotterService
    {
        public const string PlotterName = "AutocadTools PDF.pc3";

        // Resolve the active profile at runtime, including other languages, custom
        // printer search paths, new users and isolated Core Console profiles.
        public static string EnsureInstalled()
        {
            string plotters = GetPlotterFolder();
            plotters = plotters.Split(';').First(p => !string.IsNullOrWhiteSpace(p)).Trim();
            var assets = Path.Combine(Path.GetDirectoryName(typeof(PdfPlotterService).Assembly.Location), "Assets", "Plotters");
            var pmpPath = Path.Combine(plotters, "PMP Files", "AutocadTools PDF.pmp");
            Directory.CreateDirectory(Path.GetDirectoryName(pmpPath));
            var pmp = ReadConfiguration(Path.Combine(assets, "PMP Files", "AutocadTools PDF.pmp"));
            bool changed = WriteIfChanged(pmpPath, CompressConfiguration(pmp));
            var template = ReadConfiguration(Path.Combine(assets, PlotterName));
            var drivers = Path.Combine(Path.GetDirectoryName(typeof(PlotConfigManager).Assembly.Location), "drv");
            var driver = Directory.GetFiles(drivers, "pdfplot*.hdi").OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase).First();
            template = template.Replace("PMP Files\\AutocadTools PDF.pmp", pmpPath)
                .Replace("driver_pathname=\"pdfplot.hdi", "driver_pathname=\"" + driver);
            var configPath = Path.Combine(plotters, PlotterName);
            changed |= WriteIfChanged(configPath, CompressConfiguration(template));
            if (changed) PlotConfigManager.RefreshList(RefreshCode.All);
            return configPath;
        }

        private static string GetPlotterFolder()
        {
            // Read the native configuration search results, rather than guessing
            // from the UI language or the roaming folder (which differs in Core Console).
            var configurations = PlotConfigManager.Devices.Cast<PlotConfigInfo>()
                .Where(c => !string.IsNullOrWhiteSpace(c.FullPath) &&
                    string.Equals(Path.GetExtension(c.FullPath), ".pc3", StringComparison.OrdinalIgnoreCase)).ToList();
            var reference = configurations.FirstOrDefault(c =>
                c.DeviceName.Equals("DWG To PDF.pc3", StringComparison.OrdinalIgnoreCase)) ?? configurations.FirstOrDefault();
            return reference != null ? Path.GetDirectoryName(reference.FullPath) :
                Path.Combine((string)AcadApp.GetSystemVariable("ROAMABLEROOTPREFIX"), "Plotters");
        }

        private static bool WriteIfChanged(string path, byte[] bytes)
        {
            if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes)) return false;
            Infrastructure.AtomicFile.WriteAllBytes(path, bytes);
            return true;
        }

        private static string ReadConfiguration(string path)
        {
            using var input = File.OpenRead(path);
            input.Position = 60;
            using var codec = new ZLibStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(codec, Encoding.ASCII);
            return reader.ReadToEnd().TrimEnd('\0');
        }

        private static byte[] CompressConfiguration(string text)
        {
            var payload = Encoding.UTF8.GetBytes(text + '\0');
            using var compressed = new MemoryStream();
            using (var codec = new ZLibStream(compressed, CompressionLevel.SmallestSize, true)) codec.Write(payload);
            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output);
            writer.Write(Encoding.ASCII.GetBytes("PIAFILEVERSION_2.0,PC3VER1,compress\r\npmzlibcodec"));
            uint a = 1, b = 0;
            foreach (byte value in compressed.ToArray()) { a = (a + value) % 65521; b = (b + a) % 65521; }
            writer.Write((b << 16) | a);
            writer.Write(payload.Length);
            writer.Write((int)compressed.Length);
            writer.Write(compressed.ToArray());
            return output.ToArray();
        }
    }
}
