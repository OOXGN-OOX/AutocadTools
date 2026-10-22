// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using System.Globalization;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    public sealed class NumberedCopyNumber
    {
        public int Start { get; }
        public int Length => Text.Length;
        public string Text { get; }
        public decimal Value { get; }
        public int Digits { get; }
        public int FractionDigits { get; }
        public char Separator { get; }
        internal NumberedCopyNumber(int start, string text, decimal value)
        {
            Start = start; Text = text; Value = value;
            var unsigned = text.TrimStart('+', '-');
            int point = unsigned.IndexOfAny(new[] { '.', ',' });
            Separator = point < 0 ? ',' : unsigned[point];
            FractionDigits = point < 0 ? 0 : unsigned.Length - point - 1;
            int integerLength = point < 0 ? unsigned.Length : point;
            Digits = integerLength > 1 && unsigned[0] == '0' ? integerLength : 0;
        }
        public override string ToString() => Text + "  (позиция " + (Start + 1) + ")";
    }

    public static class NumberedCopyText
    {
        public static IReadOnlyList<NumberedCopyNumber> Find(string text, bool formatted = false)
        {
            text = text ?? "";
            var result = new List<NumberedCopyNumber>();
            for (int i = 0; i < text.Length;)
            {
                // Fields and MText control parameters are metadata, not visible number candidates.
                if (i + 1 < text.Length && text[i] == '%' && text[i + 1] == '<')
                {
                    int depth = 1; i += 2;
                    while (i < text.Length && depth > 0)
                    {
                        if (i + 1 < text.Length && text[i] == '%' && text[i + 1] == '<') { depth++; i += 2; }
                        else if (i + 1 < text.Length && text[i] == '>' && text[i + 1] == '%') { depth--; i += 2; }
                        else i++;
                    }
                    continue;
                }
                if (formatted && text[i] == '\\' && i + 1 < text.Length)
                {
                    char code = text[i + 1]; i += 2;
                    if ("ACcFfHhQqTtWwp".IndexOf(code) >= 0)
                    { while (i < text.Length && text[i] != ';') i++; if (i < text.Length) i++; }
                    else if (code == 'U' && i < text.Length && text[i] == '+') i = Math.Min(text.Length, i + 5);
                    // Stacked fractions (\\S) contain visible numbers and remain selectable.
                    continue;
                }
                if (i + 2 < text.Length && text[i] == '%' && text[i + 1] == '%')
                {
                    i += 2;
                    if (Digit(text[i])) { while (i < text.Length && Digit(text[i])) i++; }
                    else i++;
                    continue;
                }
                int start = i;
                bool sign = (text[i] == '-' || text[i] == '+') && i + 1 < text.Length && Digit(text[i + 1])
                    && (i == 0 || (!char.IsLetterOrDigit(text[i - 1]) && text[i - 1] != '_' && text[i - 1] != '.'));
                if (sign) i++;
                if (i >= text.Length || !Digit(text[i])) { i++; continue; }
                while (i < text.Length && Digit(text[i])) i++;
                if (i + 1 < text.Length && (text[i] == '.' || text[i] == ',') && Digit(text[i + 1]))
                { i++; while (i < text.Length && Digit(text[i])) i++; }
                string token = text.Substring(start, i - start);
                if (decimal.TryParse(token.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var value)) result.Add(new NumberedCopyNumber(start, token, value));
            }
            return result;
        }

        public static string Format(NumberedCopySource source, NumberedCopyOptions options, int index)
        {
            options.Validate();
            var numbers = Find(source.Text, source.Formatted);
            if (options.NumberIndex >= numbers.Count) throw new ArgumentException("Выбранное число отсутствует в тексте.");
            var number = numbers[options.NumberIndex];
            decimal value = checked(options.Start + options.Step * index);
            int precision = Math.Max(number.FractionDigits, Math.Max(Scale(options.Start), Scale(options.Step)));
            string text = value.ToString("F" + precision, CultureInfo.InvariantCulture);
            if (options.TrimFractionZeros && precision > 0) text = text.TrimEnd('0').TrimEnd('.');
            bool negative = text.StartsWith("-", StringComparison.Ordinal);
            string unsigned = negative ? text.Substring(1) : text;
            int point = unsigned.IndexOf('.');
            int integerLength = point < 0 ? unsigned.Length : point;
            unsigned = new string('0', Math.Max(0, options.Digits - integerLength)) + unsigned;
            text = (negative ? "-" : number.Text.StartsWith("+", StringComparison.Ordinal) ? "+" : "") + unsigned;
            if (number.Separator == ',') text = text.Replace('.', ',');
            if (!options.KeepSurrounding) return (options.Prefix ?? "") + text + (options.Suffix ?? "");
            return source.Text.Substring(0, number.Start) + text + source.Text.Substring(number.Start + number.Length);
        }

        public static bool TryDecimal(string text, out decimal value) => decimal.TryParse((text ?? "").Trim().Replace(',', '.'),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
        private static int Scale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0x7F;
        private static bool Digit(char c) => c >= '0' && c <= '9';
    }
}
