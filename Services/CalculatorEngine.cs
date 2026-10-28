// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.Globalization;

namespace AutocadTools.Services
{
    /// <summary>Bounded arithmetic parser. L and A are values, never executable code.</summary>
    internal sealed class CalculatorEngine
    {
        private readonly string _text;
        private readonly double _length, _area;
        private int _position, _depth;
        private CalculatorEngine(string text, double length, double area)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 512) throw new FormatException("Введите выражение длиной до 512 символов.");
            _text = text.Replace(',', '.').Replace('×', '*').Replace('÷', '/').Replace('−', '-');
            _length = length; _area = area;
        }
        internal static double Evaluate(string text, double length = 0, double area = 0)
        {
            var parser = new CalculatorEngine(text, length, area);
            double value = parser.Sum();
            parser.Space();
            if (parser._position != parser._text.Length) throw new FormatException("Неизвестный символ или пропущен знак операции.");
            return Finite(value);
        }
        private static double Finite(double value) => double.IsFinite(value) ? value : throw new ArithmeticException("Результат выходит за допустимый диапазон.");
        private void Space() { while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++; }
        private bool Take(char c) { Space(); if (_position >= _text.Length || _text[_position] != c) return false; _position++; return true; }
        private double Sum()
        {
            double value = Product();
            while (true) { if (Take('+')) value = Finite(value + Product()); else if (Take('-')) value = Finite(value - Product()); else return value; }
        }
        private double Product()
        {
            double value = Unary();
            while (true)
            {
                if (Take('*')) value = Finite(value * Unary());
                else if (Take('/')) { double divisor = Unary(); if (divisor == 0) throw new DivideByZeroException("Деление на ноль."); value = Finite(value / divisor); }
                else return value;
            }
        }
        private double Unary()
        {
            if (++_depth > 32) throw new FormatException("Слишком глубокая вложенность выражения.");
            try { if (Take('+')) return Unary(); if (Take('-')) return -Unary(); return Power(); }
            finally { _depth--; }
        }
        private double Power()
        {
            double value = Atom();
            while (Take('%')) value /= 100;
            return Take('^') ? Finite(Math.Pow(value, Unary())) : value;
        }
        private double Atom()
        {
            if (Take('(')) { double value = Sum(); if (!Take(')')) throw new FormatException("Не закрыта скобка."); return value; }
            Space();
            if (_position < _text.Length && char.ToUpperInvariant(_text[_position]) is 'L' or 'A')
                return char.ToUpperInvariant(_text[_position++]) == 'L' ? _length : _area;
            int start = _position;
            while (_position < _text.Length && (char.IsDigit(_text[_position]) || _text[_position] == '.')) _position++;
            if (_position < _text.Length && _text[_position] is 'e' or 'E')
            {
                _position++; if (_position < _text.Length && _text[_position] is '+' or '-') _position++;
                while (_position < _text.Length && char.IsDigit(_text[_position])) _position++;
            }
            if (!double.TryParse(_text.Substring(start, _position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                throw new FormatException("Ожидается число, L, A или скобка.");
            return Finite(number);
        }
    }
}
