// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;

namespace AutocadTools.Models
{
    public enum NumberedCopyMode { Points, Curve }
    public enum NumberedCopyOrientation { Original, Tangent, Left, Right }

    public sealed class NumberedCopySource
    {
        public string Label { get; }
        public string Text { get; }
        public bool Formatted { get; }
        public NumberedCopySource(string label, string text, bool formatted)
        { Label = label; Text = text ?? ""; Formatted = formatted; }
        public override string ToString() => Label;
    }

    public sealed class NumberedCopyOptions
    {
        public const int MaximumCopies = 10000;
        public int SourceIndex { get; set; }
        public int NumberIndex { get; set; }
        public decimal Start { get; set; } = 1;
        public decimal Step { get; set; } = 1;
        public bool KeepSurrounding { get; set; } = true;
        public string Prefix { get; set; } = "";
        public string Suffix { get; set; } = "";
        public int Digits { get; set; }
        public bool TrimFractionZeros { get; set; }
        public NumberedCopyMode Mode { get; set; }
        public double Distance { get; set; } = 1000;
        public bool LimitCount { get; set; }
        public int Count { get; set; } = 5;
        public bool Divide { get; set; }
        public bool Vertices { get; set; }
        public bool OnCurve { get; set; } = true;
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public NumberedCopyOrientation Orientation { get; set; }
        public NumberedCopyOptions Copy() => (NumberedCopyOptions)MemberwiseClone();

        public void Validate()
        {
            if (SourceIndex < 0 || NumberIndex < 0) throw new ArgumentException("Выберите текст и число.");
            if (Digits < 0 || Digits > 28) throw new ArgumentException("Число разрядов: от 0 до 28.");
            if ((Prefix ?? "").Length > 256 || (Suffix ?? "").Length > 256)
                throw new ArgumentException("Префикс и суффикс: не более 256 знаков.");
            if (!Enum.IsDefined(typeof(NumberedCopyMode), Mode) || !Enum.IsDefined(typeof(NumberedCopyOrientation), Orientation))
                throw new ArgumentException("Выберите режим размещения и ориентацию.");
            if (Mode != NumberedCopyMode.Curve) return;
            if (!Finite(OffsetX) || !Finite(OffsetY)) throw new ArgumentException("Смещения должны быть конечными числами.");
            if (!Vertices && !(LimitCount && Divide) && (!Finite(Distance) || Distance <= 0))
                throw new ArgumentException("Шаг вдоль кривой должен быть положительным.");
            if (!Vertices && LimitCount && (Count < 1 || Count > MaximumCopies))
                throw new ArgumentException("Количество копий: от 1 до 10000.");
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
