// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AutocadTools.Models;
using AutocadTools.Services;

namespace AutocadTools.Views
{
    public sealed class NumberedCopyWindow : Window
    {
        private readonly IReadOnlyList<NumberedCopySource> _sources;
        private readonly ComboBox _source, _number, _mode, _orientation;
        private readonly TextBox _start, _step, _prefix, _suffix, _digits, _distance, _count, _x, _y;
        private readonly CheckBox _surrounding, _trim, _limit, _divide, _vertices, _onCurve;
        private readonly StackPanel _path;
        private readonly TextBlock _preview, _error;
        private readonly Button _accept;
        private bool _updating;
        public NumberedCopyOptions Options { get; private set; }

        public NumberedCopyWindow(IReadOnlyList<NumberedCopySource> sources, NumberedCopyOptions options)
        {
            _sources = sources; Options = options.Copy();
            Title = "Копирование с нумерацией";
            Width = 840; MinWidth = 760; Height = Math.Min(740, SystemParameters.WorkArea.Height - 48); MinHeight = 540;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/" + typeof(UiTheme).Assembly.GetName().Name
                + ";component/Views/Styles.xaml", UriKind.Relative) });
            Style = (Style)FindResource("ToolWindow"); UiTheme.Apply(this);
            var root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;
            var header = new StackPanel(); root.Children.Add(header);
            _source = Combo(sources.Cast<object>().ToArray(), Math.Min(options.SourceIndex, sources.Count - 1));
            AddField(header, "Текст / атрибут", _source);
            _number = Combo(Array.Empty<object>(), -1); AddField(header, "Изменяемое число", _number);
            var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition()); body.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetRow(body, 1); root.Children.Add(body);
            var fields = new StackPanel(); var placement = new StackPanel();
            var textScroll = new ScrollViewer { Content = fields, Margin = new Thickness(0, 0, 12, 0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var pathScroll = new ScrollViewer { Content = placement, Margin = new Thickness(12, 0, 0, 0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetColumn(pathScroll, 1); body.Children.Add(textScroll); body.Children.Add(pathScroll);
            var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            var right = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(right, 1); columns.Children.Add(left); columns.Children.Add(right); fields.Children.Add(columns);
            _start = Editor(options.Start.ToString(CultureInfo.CurrentCulture)); AddField(left, "Исходное число", _start);
            _step = Editor(options.Step.ToString(CultureInfo.CurrentCulture)); AddField(right, "Приращение номера", _step);
            _surrounding = Check("Сохранить текст до и после числа", options.KeepSurrounding, fields);
            _prefix = Editor(options.Prefix); _suffix = Editor(options.Suffix);
            var affixes = new Grid(); affixes.ColumnDefinitions.Add(new ColumnDefinition()); affixes.ColumnDefinitions.Add(new ColumnDefinition());
            var prefixPanel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            var suffixPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(suffixPanel, 1); affixes.Children.Add(prefixPanel); affixes.Children.Add(suffixPanel);
            AddField(prefixPanel, "Префикс", _prefix); AddField(suffixPanel, "Суффикс", _suffix); fields.Children.Add(affixes);
            _digits = Editor(options.Digits.ToString(CultureInfo.InvariantCulture)); AddField(fields, "Минимум цифр до запятой (0 — без дополнения)", _digits);
            _trim = Check("Убирать нули в конце дробной части", options.TrimFractionZeros, fields);
            _preview = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 16) };
            AutomationProperties.SetName(_preview, "Предпросмотр нумерации"); fields.Children.Add(_preview);
            _mode = Combo(new object[] { "Вручную по точкам", "Вдоль кривой" }, (int)options.Mode);
            AddField(placement, "Размещение", _mode);
            _path = new StackPanel(); placement.Children.Add(_path);
            _vertices = Check("В вершинах полилинии", options.Vertices, _path);
            _distance = Editor(options.Distance.ToString(CultureInfo.CurrentCulture)); AddField(_path, "Шаг вдоль кривой, ед. чертежа", _distance);
            _limit = Check("Задать количество копий", options.LimitCount, _path);
            _count = Editor(options.Count.ToString(CultureInfo.InvariantCulture)); AddField(_path, "Количество копий", _count);
            _divide = Check("Равномерно распределить по всей длине", options.Divide, _path);
            _onCurve = Check("На кривой: начать с исходного числа", options.OnCurve, _path);
            var hint = new TextBlock { Text = "При выключенном флажке сохраняется смещение исходника от начала кривой; нумерация начинается с приращения.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary"); _path.Children.Add(hint);
            _x = Editor(options.OffsetX.ToString(CultureInfo.CurrentCulture)); AddField(_path, "Смещение X в текущей ПСК", _x);
            _y = Editor(options.OffsetY.ToString(CultureInfo.CurrentCulture)); AddField(_path, "Смещение Y в текущей ПСК", _y);
            _orientation = Combo(new object[] { "Сохранить поворот", "По касательной", "Перпендикулярно влево", "Перпендикулярно вправо" }, (int)options.Orientation);
            AddField(_path, "Ориентация", _orientation);
            _error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            _error.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush"); Grid.SetRow(_error, 2); root.Children.Add(_error);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0) };
            Grid.SetRow(buttons, 3); root.Children.Add(buttons);
            var cancel = new Button { Content = "Отмена", IsCancel = true, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
            cancel.Click += (_, _) => DialogResult = false;
            _accept = new Button { Content = "Разместить копии", IsDefault = true, Style = (Style)FindResource("PrimaryButton") };
            _accept.Click += (_, _) => { if (ReadOptions()) DialogResult = true; };
            buttons.Children.Add(cancel); buttons.Children.Add(_accept);
            foreach (var input in new[] { _start, _step, _prefix, _suffix, _digits, _distance, _count, _x, _y }) input.TextChanged += (_, _) => ReadOptions();
            foreach (var check in new[] { _surrounding, _trim, _limit, _divide, _vertices, _onCurve })
            { check.Checked += (_, _) => ReadOptions(); check.Unchecked += (_, _) => ReadOptions(); }
            _source.SelectionChanged += (_, _) => SourceChanged();
            _number.SelectionChanged += (_, _) => NumberChanged();
            _mode.SelectionChanged += (_, _) => ReadOptions(); _orientation.SelectionChanged += (_, _) => ReadOptions();
            SourceChanged();
        }

        private TextBox Editor(string value) => new TextBox { Text = value, Margin = new Thickness(0, 4, 0, 12), Style = (Style)FindResource("ToolTextBox") };
        private static ComboBox Combo(object[] items, int selected) => new ComboBox { ItemsSource = items, SelectedIndex = selected, Margin = new Thickness(0, 4, 0, 12) };
        private static void AddField(Panel parent, string text, Control control)
        { parent.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }); AutomationProperties.SetName(control, text); parent.Children.Add(control); }
        private static CheckBox Check(string text, bool value, Panel parent)
        {
            var check = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = value, Margin = new Thickness(0, 0, 0, 10) };
            AutomationProperties.SetName(check, text); parent.Children.Add(check); return check;
        }

        private void SourceChanged()
        {
            _updating = true;
            var source = _source.SelectedItem as NumberedCopySource;
            _number.ItemsSource = source == null ? Array.Empty<NumberedCopyNumber>() : NumberedCopyText.Find(source.Text, source.Formatted);
            _number.SelectedIndex = 0; _updating = false; NumberChanged();
        }
        private void NumberChanged()
        {
            if (_updating) return;
            _updating = true;
            if (_number.SelectedItem is NumberedCopyNumber number)
            {
                _start.Text = number.Value.ToString(CultureInfo.CurrentCulture);
                _digits.Text = number.Digits.ToString(CultureInfo.InvariantCulture);
            }
            _updating = false; ReadOptions();
        }

        private bool ReadOptions()
        {
            if (_updating) return false;
            bool curve = _mode.SelectedIndex == 1, vertices = _vertices.IsChecked == true;
            bool limit = _limit.IsChecked == true, divide = limit && _divide.IsChecked == true;
            _prefix.IsEnabled = _suffix.IsEnabled = _surrounding.IsChecked != true;
            _path.Visibility = curve ? Visibility.Visible : Visibility.Collapsed;
            _limit.IsEnabled = !vertices; _count.IsEnabled = !vertices && limit;
            _divide.IsEnabled = !vertices && limit; _distance.IsEnabled = !vertices && !divide;
            try
            {
                if (!(_number.SelectedItem is NumberedCopyNumber) || _source.SelectedIndex < 0)
                    throw new ArgumentException("В выбранном тексте нет доступного числа. Выберите другой атрибут или объект.");
                if (!NumberedCopyText.TryDecimal(_start.Text, out var start) || !NumberedCopyText.TryDecimal(_step.Text, out var step))
                    throw new ArgumentException("Введите исходное число и приращение: например, 1 или 0,5.");
                var value = Options.Copy();
                value.SourceIndex = _source.SelectedIndex; value.NumberIndex = _number.SelectedIndex;
                value.Start = start; value.Step = step; value.KeepSurrounding = _surrounding.IsChecked == true;
                value.Prefix = _prefix.Text; value.Suffix = _suffix.Text; value.Digits = int.Parse(_digits.Text, CultureInfo.InvariantCulture);
                value.TrimFractionZeros = _trim.IsChecked == true; value.Mode = (NumberedCopyMode)_mode.SelectedIndex;
                value.Vertices = vertices; value.LimitCount = limit; value.Divide = divide; value.OnCurve = _onCurve.IsChecked == true;
                if (curve)
                {
                    if (!vertices && !divide) value.Distance = Number(_distance.Text);
                    if (!vertices && limit) value.Count = int.Parse(_count.Text, CultureInfo.InvariantCulture);
                    value.OffsetX = Number(_x.Text); value.OffsetY = Number(_y.Text);
                }
                value.Orientation = (NumberedCopyOrientation)_orientation.SelectedIndex;
                value.Validate();
                var source = _sources[value.SourceIndex];
                int first = curve && value.OnCurve ? 0 : 1;
                string[] previews = Enumerable.Range(first, 3).Select(i => NumberedCopyText.Format(source, value, i))
                    .Select(text => source.Formatted ? Plain(text) : text).ToArray();
                _preview.Text = "Первые копии: " + string.Join("  →  ", previews);
                Options = value; _error.Text = ""; _accept.IsEnabled = true; return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException)
            {
                _error.Text = ex is ArgumentException ? ex.Message : "Проверьте числовые значения и допустимый диапазон.";
                _preview.Text = ""; _accept.IsEnabled = false; return false;
            }
        }
        private static double Number(string text) => double.Parse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
        private static string Plain(string text)
        {
            text = Regex.Replace(text, @"\\[ACcFfHhQqTtWwp][^;]*;", "");
            text = text.Replace(@"\P", " · ").Replace(@"\~", " ");
            return text.Replace("{", "").Replace("}", "");
        }
    }
}
