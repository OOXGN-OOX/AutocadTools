// Copyright (c) 2026 OOXGN. All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace AutocadTools.Views
{
    internal static class ToolIcons
    {
        // All glyphs share a 32-DIP grid. Their shapes describe the command, not the publisher.
        private static readonly Dictionary<string, string> Paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Align_Den"] = "M4,22 L13,7 28,7 19,22 Z M4,27 L20,27 M25,18 L29,24 25,28 M29,24 L22,24",
            ["AUTONUM"] = "M4,8 L6,6 6,12 M3,13 L9,13 M3,20 C3,15 9,15 9,19 L3,25 9,25 M15,8 L28,8 M15,16 L28,16 M15,24 L28,24",
            ["WSELECT"] = "M3,11 L3,3 11,3 M21,3 L29,3 29,11 M29,21 L29,29 21,29 M11,29 L3,29 3,21 M10,10 L22,10 22,22 10,22 Z",
            ["SUMLENGTH"] = "M4,8 L28,8 28,24 4,24 Z M9,8 L9,15 M14,8 L14,12 M19,8 L19,15 M24,8 L24,12",
            ["MULTICOPY"] = "M3,3 L15,3 15,15 3,15 Z M9,19 L9,21 21,21 21,9 19,9 M15,25 L15,27 27,27 27,15 25,15",
            ["PLANPREP"] = "M4,4 L28,4 28,28 4,28 Z M4,13 L15,13 15,4 M15,13 L15,21 M15,26 L15,28 M20,13 L28,13 M8,20 L11,20",
            ["AUTOCADTOOLS_TABLEEXCEL"] = "M4,5 L28,5 28,27 4,27 Z M4,12 L28,12 M12,5 L12,27 M20,12 L20,27 M4,19 L28,19",
            ["CLEANDWG"] = "M11,8 L21,18 M19,3 L9,13 M10,12 L3,21 11,29 21,22 10,12 Z M17,28 L29,28",
            ["CLEANUNSELECTED"] = "M3,10 L3,3 10,3 M22,3 L29,3 29,10 M3,22 L3,29 10,29 M22,29 L29,29 29,22 M10,10 L22,22 M22,10 L10,22",
            ["STRIPTEXT"] = "M4,5 L22,5 M13,5 L13,25 M8,25 L18,25 M22,16 L29,23 M29,16 L22,23",
            ["AUTOCADTOOLS_STAMP"] = "M3,6 L29,6 29,26 3,26 Z M3,19 L29,19 M14,6 L14,26 M14,13 L29,13 M23,19 L23,26 M7,10 L10,10 M7,14 L10,14",
            ["AUTOCADTOOLS_INSERTFORMAT"] = "M5,3 L20,3 27,10 27,29 5,29 Z M20,3 L20,10 27,10 M11,20 L21,20 M16,15 L16,25",
            ["AUTOCADTOOLS_BATCHPLOT"] = "M9,11 L9,3 23,3 23,11 M9,24 L4,24 4,11 28,11 28,24 23,24 M9,20 L23,20 23,29 9,29 Z M23,15 L25,15",
            ["AUTOCADTOOLS_BLOCKS_STAGE1"] = "M4,10 L16,4 28,10 16,16 Z M4,10 L4,23 16,29 28,23 28,10 M16,16 L16,29",
            ["AUTOCADTOOLS_CUSTOMERORDER"] = "M5,3 L20,3 25,8 25,13 M5,3 L5,29 25,29 25,25 M13,19 L29,19 M24,14 L29,19 24,24",
            ["AUTOCADTOOLS_INFO"] = "M16,3 A13,13 0 1 1 15.99,3 M16,8 L16,10 M14,14 L17,14 17,24 M13,24 L21,24",
            ["AUTOCADTOOLS_CHECKUPDATES"] = "M26,12 A11,11 0 0 0 7,7 M6,3 L6,9 12,9 M6,20 A11,11 0 0 0 25,25 M26,29 L26,23 20,23"
        };

        public static IEnumerable<string> Commands => Paths.Keys;

        public static ImageSource Create(string command, bool dark)
        {
            if (!Paths.TryGetValue(command, out var path)) throw new ArgumentException("Unknown tool icon", nameof(command));
            Brush brush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush :
                new SolidColorBrush(dark ? Color.FromRgb(250, 249, 247) : Color.FromRgb(43, 43, 43));
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
            drawing.Children.Add(new GeometryDrawing(null, new Pen(brush, 1.6) { LineJoin = PenLineJoin.Round }, Geometry.Parse(path)));
            var image = new DrawingImage(drawing);
            image.Freeze();
            return image;
        }
    }
}
