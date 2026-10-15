// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Windows;
using System.Windows.Media;

namespace AutocadTools.Models
{
    public sealed class StampTextStyle
    {
        // The spatial editor uses 703 units for the 185 mm title block.
        public const double UnitsPerMillimetre = 3.8;
        public double Height { get; set; } = 2.5;
        public double WidthFactor { get; set; } = 1;
        public string FamilyName { get; set; } = "ISOCPEUR";
        private string _resolvedName;
        private FontFamily _fontFamily;
        public FontFamily FontFamily
        {
            get
            {
                if (_fontFamily != null && _resolvedName == FamilyName) return _fontFamily;
                var face = ResolveTypeface();
                if (!face.TryGetGlyphTypeface(out var glyph)) return face.FontFamily;
                // AutoCAD's default MTEXT baseline spacing is 5/3 of the text height.
                // A composite family adjusts line metrics without scaling or replacing any glyphs.
                double spacing = glyph.CapsHeight * 5.0 / 3;
                var family = new FontFamily { LineSpacing = spacing, Baseline = Math.Max(glyph.CapsHeight, spacing - (glyph.Height - glyph.Baseline)) };
                family.FamilyMaps.Add(new FontFamilyMap { Target = face.FontFamily.Source });
                _resolvedName = FamilyName;
                return _fontFamily = family;
            }
        }
        public double FontSize
        {
            get
            {
                var face = ResolveTypeface();
                double caps = face.TryGetGlyphTypeface(out var glyph) && glyph.CapsHeight > 0 ? glyph.CapsHeight : 0.7;
                return Height * UnitsPerMillimetre / caps;
            }
        }
        private Typeface ResolveTypeface()
        {
            foreach (var family in new[] { FamilyName, "ISOCPEUR", "GOST Common", "Segoe UI" })
            {
                if (string.IsNullOrWhiteSpace(family)) continue;
                var face = new Typeface(new FontFamily(family), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                if (face.TryGetGlyphTypeface(out _)) return face;
            }
            return new Typeface("Segoe UI");
        }
    }
    public sealed class StampTypography
    {
        public StampTextStyle Body { get; set; } = new StampTextStyle();
        public StampTextStyle Code { get; set; } = new StampTextStyle { Height = 5 };
        public StampTextStyle Project { get; set; } = new StampTextStyle();
        public StampTextStyle Building { get; set; } = new StampTextStyle();
        public StampTextStyle Company { get; set; } = new StampTextStyle();
    }
}
