// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;

namespace AutocadTools.Models
{
    public enum DrawingSymbolKind
    {
        Section, SteppedSection, Zigzag, Wave, SchemeBreak,
        DetailCircle, DetailOval, DetailRectangle, DetailTitle,
        Elevation, PlanElevation, SlopeArrow, SlopeSign,
        LeaderDot, LeaderArrow, LeaderPlain, MultiLeader,
        CoordinateAxis, ViewDirection, Fragment, ImageTitle
    }

    public sealed class DrawingSymbolOptions
    {
        public DrawingSymbolKind Kind { get; set; }
        public string Mark { get; set; } = "А";
        public double Scale { get; set; } = 100;
        public double TextHeight { get; set; } = 5;
        public double StrokeLength { get; set; } = 10;
        public double MainThickness { get; set; } = 0.7;
        public string Note { get; set; } = "";
        public double Diameter { get; set; } = 12;
        public bool IsSection => Kind == DrawingSymbolKind.Section || Kind == DrawingSymbolKind.SteppedSection;
        public bool IsBreak => Kind >= DrawingSymbolKind.Zigzag && Kind <= DrawingSymbolKind.SchemeBreak;
        public bool IsLevel => Kind == DrawingSymbolKind.Elevation || Kind == DrawingSymbolKind.PlanElevation;
        public bool IsSlope => Kind == DrawingSymbolKind.SlopeArrow || Kind == DrawingSymbolKind.SlopeSign;
        public bool IsLeader => Kind >= DrawingSymbolKind.LeaderDot && Kind <= DrawingSymbolKind.MultiLeader;
        public bool HasOutline => Kind >= DrawingSymbolKind.DetailCircle && Kind <= DrawingSymbolKind.DetailTitle || Kind == DrawingSymbolKind.CoordinateAxis;
        public bool HasNote => Kind >= DrawingSymbolKind.DetailCircle && Kind <= DrawingSymbolKind.DetailTitle || IsLeader;
        public bool FreeText => IsLeader || Kind == DrawingSymbolKind.Fragment || Kind == DrawingSymbolKind.ImageTitle;
        public bool NeedsEndPoint => Kind != DrawingSymbolKind.DetailTitle && Kind != DrawingSymbolKind.PlanElevation && Kind != DrawingSymbolKind.ImageTitle;
        public string Designation
        {
            get
            {
                if (IsLevel && TryLevel(out double value))
                {
                    value = Math.Round(value, 3, MidpointRounding.AwayFromZero);
                    if (value == 0) value = 0;
                    return (value > 0 ? "+" : "") + value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
                }
                if (FreeText || IsSlope) return Mark;
                return IsBreak ? Mark?.ToLowerInvariant() : Mark?.ToUpperInvariant();
            }
        }
        public DrawingSymbolOptions Copy() => (DrawingSymbolOptions)MemberwiseClone();

        public void Validate()
        {
            if (!Enum.IsDefined(typeof(DrawingSymbolKind), Kind)) throw new ArgumentException("Выберите тип обозначения.");
            if (string.IsNullOrWhiteSpace(Mark) || Mark.Length > (FreeText ? 128 : 32) || Mark.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0)
                throw new ArgumentException("Введите обозначение или текст в одной строке.");
            if (Note == null || Note.Length > 128 || Note.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0)
                throw new ArgumentException("Нижняя строка должна содержать не более 128 символов без переносов.");
            if (IsLevel && !TryLevel(out _)) throw new ArgumentException("Введите числовую отметку в метрах, например +3,600.");
            if (IsSlope && !System.Text.RegularExpressions.Regex.IsMatch(Mark, @"^\d+(?:[,.]\d+)?(?:\s?%|:\d+(?:[,.]\d+)?)?$"))
                throw new ArgumentException("Введите уклон: 2%, 1:50 или 0,02.");
            if (IsSlope && Mark.Contains(":"))
            {
                string denominator = Mark.Substring(Mark.IndexOf(':') + 1).Replace(',', '.');
                if (!double.TryParse(denominator, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double ratio) || ratio <= 0 || !double.IsFinite(ratio))
                    throw new ArgumentException("Знаменатель уклона должен быть больше нуля.");
            }
            if (!FreeText && !IsLevel && !IsSlope) foreach (char letter in Mark)
            {
                char upper = char.ToUpperInvariant(letter);
                if (!(upper >= 'А' && upper <= 'Я') && upper != 'Ё' && !(letter >= '0' && letter <= '9') && " ()-".IndexOf(letter) < 0)
                    throw new ArgumentException("Для буквенных обозначений используйте русский алфавит.");
                if ((IsSection || Kind == DrawingSymbolKind.CoordinateAxis || Kind == DrawingSymbolKind.ViewDirection) && "ЕЁЗЙОХЦЧЩЪЫЬ".IndexOf(upper) >= 0)
                    throw new ArgumentException("Буква «" + upper + "» исключена из обозначений по ГОСТ Р 21.101.");
                if (Kind == DrawingSymbolKind.SchemeBreak && !(upper >= 'А' && upper <= 'Я') && upper != 'Ё')
                    throw new ArgumentException("Разрыв на схеме обозначают строчными русскими буквами.");
            }
            if (!double.IsFinite(Diameter) || Diameter < 6 || Diameter > 100)
                throw new ArgumentException("Габарит обозначения должен быть от 6 до 100 мм на бумаге.");
            if (Kind == DrawingSymbolKind.CoordinateAxis && Diameter > 12)
                throw new ArgumentException("Диаметр обозначения оси по ГОСТ: 6–12 мм.");
            if (Kind == DrawingSymbolKind.DetailTitle && (Diameter < 12 || Diameter > 14))
                throw new ArgumentException("Диаметр обозначения узла над изображением: 12–14 мм.");
            if (!double.IsFinite(Scale) || Scale < 0.01 || Scale > 100000)
                throw new ArgumentException("Масштаб должен быть от 0,01 до 100000.");
            if (Array.IndexOf(new[] { 1.8, 2.5, 3.5, 5, 7, 10, 14, 20 }, TextHeight) < 0)
                throw new ArgumentException("Высоты шрифта по ГОСТ: 1,8; 2,5; 3,5; 5; 7; 10; 14; 20 мм.");
            if (!double.IsFinite(StrokeLength) || StrokeLength < 8 || StrokeLength > 20)
                throw new ArgumentException("Длина штриха должна быть от 8 до 20 мм на бумаге.");
            if (!double.IsFinite(MainThickness) || MainThickness < 0.5 || MainThickness > 1.4)
                throw new ArgumentException("Толщина основной линии должна быть от 0,5 до 1,4 мм.");
        }

        private bool TryLevel(out double value) => double.TryParse(Mark?.Replace(',', '.'),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) &&
            double.IsFinite(value) && Math.Abs(value) < 1000000;
    }
}
