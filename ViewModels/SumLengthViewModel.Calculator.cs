// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.Globalization;
using System.Windows;
using AutocadTools.Models;
using AutocadTools.Services;

namespace AutocadTools.ViewModels
{
    public partial class SumLengthViewModel
    {
        private string _calculatorExpression = "", _calculatorResult = "—", _calculatorError = "";
        private double? _calculatorValue;
        public string CalculatorExpression
        {
            get => _calculatorExpression;
            set { if (SetProperty(ref _calculatorExpression, value ?? "")) Calculate(false); }
        }
        public string CalculatorResult { get => _calculatorResult; private set => SetProperty(ref _calculatorResult, value); }
        public string CalculatorError { get => _calculatorError; private set => SetProperty(ref _calculatorError, value); }
        public RelayCommand CalculatorKeyCommand { get; private set; }
        public RelayCommand CalculateCommand { get; private set; }
        public RelayCommand CopyCalculatorCommand { get; private set; }
        private void InitializeCalculator()
        {
            CalculatorKeyCommand = new RelayCommand(key =>
            {
                var text = key?.ToString() ?? "";
                if (text == "C") CalculatorExpression = "";
                else if (text == "Back") CalculatorExpression = CalculatorExpression.Length > 0 ? CalculatorExpression[..^1] : "";
                else if (CalculatorExpression.Length < 512) CalculatorExpression += text;
            });
            CalculateCommand = new RelayCommand(_ => Calculate(true));
            CopyCalculatorCommand = new RelayCommand(_ => { if (_calculatorValue.HasValue) Clipboard.SetText(CalculatorResult); }, _ => _calculatorValue.HasValue);
        }
        private void Calculate(bool showError)
        {
            try
            {
                double length = TotalLength / (SelectedLengthUnit == LengthUnit.Meters ? 1000 : SelectedLengthUnit == LengthUnit.Kilometers ? 1000000 : 1);
                double area = TotalArea / (SelectedAreaUnit == AreaUnit.SquareMeters ? 1000000 : SelectedAreaUnit == AreaUnit.SquareKilometers ? 1000000000000 : 1);
                _calculatorValue = CalculatorEngine.Evaluate(CalculatorExpression, length, area);
                CalculatorResult = _calculatorValue.Value.ToString("G12", CultureInfo.CurrentCulture);
                CalculatorError = "";
            }
            catch (Exception ex) when (ex is FormatException || ex is ArithmeticException)
            {
                _calculatorValue = null; CalculatorResult = "—";
                CalculatorError = showError && CalculatorExpression.Length > 0 ? ex.Message : "";
            }
        }
    }
}
