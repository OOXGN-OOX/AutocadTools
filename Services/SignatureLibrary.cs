// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    public static class SignatureLibrary
    {
        public static readonly Color InkColor = Color.FromRgb(30, 80, 181);
        private static string LibraryPath => Path.Combine(PluginPaths.UserDataDirectory, "Signatures");

        public static IReadOnlyList<StampSignature> Load()
        {
            if (!Directory.Exists(LibraryPath)) return Array.Empty<StampSignature>();
            return Directory.EnumerateFiles(LibraryPath, "*.json").Select(path =>
                JsonConvert.DeserializeObject<StampSignature>(File.ReadAllText(path))).Where(s => s != null).OrderBy(s => s.Name).ToArray();
        }

        public static void Save(StampSignature signature)
        {
            if (signature == null || signature.Png == null || signature.Png.Length == 0) throw new ArgumentException("Подпись пуста.");
            if (!Guid.TryParseExact(signature.Id, "N", out _)) throw new ArgumentException("Некорректный идентификатор подписи.");
            Directory.CreateDirectory(LibraryPath);
            var path = Path.Combine(LibraryPath, signature.Id + ".json");
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(signature));
            File.Move(temporary, path, true);
        }

        public static StampSignature Import(string path, string name)
        {
            if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new ArgumentException("Размер PNG превышает 20 МБ.");
            using (var stream = File.OpenRead(path))
            {
                var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                return new StampSignature { Name = name, Png = Normalize(decoder.Frames[0]) };
            }
        }

        // Both scans on white paper and transparent PNGs become blue ink on transparency.
        public static byte[] Normalize(BitmapSource input)
        {
            if (input.PixelWidth > 8192 || input.PixelHeight > 8192 || (long)input.PixelWidth * input.PixelHeight > 20000000)
                throw new ArgumentException("Изображение слишком большое. Максимум — 20 млн пикселей.");
            double scale = Math.Min(1, Math.Min(800.0 / input.PixelWidth, 300.0 / input.PixelHeight));
            if (scale < 1) input = new TransformedBitmap(input, new ScaleTransform(scale, scale));
            var bitmap = new FormatConvertedBitmap(input, PixelFormats.Bgra32, null, 0);
            int width = bitmap.PixelWidth, height = bitmap.PixelHeight, stride = width * 4;
            var pixels = new byte[stride * height];
            bitmap.CopyPixels(pixels, stride, 0);
            int left = width, right = -1, top = height, bottom = -1;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int p = y * stride + x * 4;
                int darkness = 255 - (pixels[p] * 11 + pixels[p + 1] * 59 + pixels[p + 2] * 30) / 100;
                byte alpha = (byte)(pixels[p + 3] * Math.Min(255, darkness * 2) / 255);
                if (alpha < 24) alpha = 0;
                pixels[p] = InkColor.B; pixels[p + 1] = InkColor.G; pixels[p + 2] = InkColor.R; pixels[p + 3] = alpha;
                if (alpha == 0) continue;
                left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
            if (right < left) throw new ArgumentException("На изображении не найдена подпись.");
            var blue = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            var cropped = new CroppedBitmap(blue, new Int32Rect(left, top, right - left + 1, bottom - top + 1));
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(cropped));
            using (var output = new MemoryStream()) { encoder.Save(output); return output.ToArray(); }
        }
    }
}
