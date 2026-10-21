// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AutocadTools.Models;

namespace AutocadTools.Views
{
    public sealed class DrawingSymbolWindow : Window
    {
        private readonly ComboBox _kind;
        private readonly TextBox _mark, _scale, _height, _stroke, _thickness, _note, _diameter;
        private readonly TextBlock _error;
        private readonly Button _draw;
        private readonly bool _section;
        public DrawingSymbolOptions Options { get; private set; }

        public DrawingSymbolWindow(DrawingSymbolOptions options)
        {
            Options = options.Copy();
            _section = options.IsSection;
            Title = DrawingSymbolCatalog.Get(options.Kind).Title;
            Width = 460; MinWidth = 420; MinHeight = options.IsSection || options.IsBreak ? 460 : 380;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/" + typeof(UiTheme).Assembly.GetName().Name + ";component/Views/Styles.xaml", UriKind.Relative)
            });
            Style = (Style)FindResource("ToolWindow");
            UiTheme.Apply(this);
            var root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;
            var fields = new StackPanel();
            var scroll = new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            root.Children.Add(scroll);
            _kind = new ComboBox { Margin = new Thickness(0, 4, 0, 12), SelectedValuePath = "Tag" };
            if (_section)
            {
                _kind.Items.Add(new ComboBoxItem { Content = "Прямой разрез", Tag = DrawingSymbolKind.Section });
                _kind.Items.Add(new ComboBoxItem { Content = "Ступенчатый разрез", Tag = DrawingSymbolKind.SteppedSection });
            }
            else if (options.IsBreak)
            {
                _kind.Items.Add(new ComboBoxItem { Content = "Линия с изломом", Tag = DrawingSymbolKind.Zigzag });
                _kind.Items.Add(new ComboBoxItem { Content = "Волнистая линия", Tag = DrawingSymbolKind.Wave });
                _kind.Items.Add(new ComboBoxItem { Content = "Пунктирный разрыв на схеме", Tag = DrawingSymbolKind.SchemeBreak });
            }
            else _kind.Items.Add(new ComboBoxItem { Content = Title, Tag = options.Kind });
            _kind.SelectedValue = options.Kind;
            if (_section || options.IsBreak) AddField(fields, "Тип обозначения", _kind);
            _mark = Editor(options.Mark);
            _mark.FontFamily = new System.Windows.Media.FontFamily("ISOCPEUR, GOST Common");
            _mark.MaxLength = options.FreeText ? 128 : 32;
            if (options.Kind >= DrawingSymbolKind.DetailCircle && options.Kind <= DrawingSymbolKind.DetailRectangle)
                _mark.ToolTip = "Для узла на другом листе укажите номер листа в скобках: 1 (5).";
            AddField(fields, options.IsLevel ? "Отметка, м" : options.IsSlope ? "Уклон" : options.FreeText ? "Текст" : "Обозначение", _mark);
            _note = Editor(options.Note);
            if (options.HasNote) AddField(fields, options.Kind == DrawingSymbolKind.DetailTitle
                ? "Номер листа (необязательно)" : "Нижняя строка: документ / примечание", _note);
            _scale = Editor(options.Scale.ToString(CultureInfo.CurrentCulture));
            AddField(fields, "Масштаб 1 :", _scale);
            var dimensions = new Grid();
            dimensions.ColumnDefinitions.Add(new ColumnDefinition());
            dimensions.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            var right = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(right, 1); dimensions.Children.Add(left); dimensions.Children.Add(right);
            fields.Children.Add(dimensions);
            _height = Editor(options.TextHeight.ToString(CultureInfo.CurrentCulture));
            AddField(left, "Текст на бумаге, мм", _height);
            _thickness = Editor(options.MainThickness.ToString(CultureInfo.CurrentCulture));
            AddField(right, "Основная линия s, мм", _thickness);
            _stroke = Editor(options.StrokeLength.ToString(CultureInfo.CurrentCulture));
            if (_section) AddField(fields, "Крайний штрих на бумаге, мм (8–20)", _stroke);
            _diameter = Editor(options.Diameter.ToString(CultureInfo.CurrentCulture));
            if (options.HasOutline) AddField(fields, "Диаметр / габарит на бумаге, мм", _diameter);
            var note = new TextBlock { Text = _section
                ? "Русские прописные буквы. Шрифт: ISOCPEUR / GOST Common. Затем укажите точки и сторону взгляда."
                : options.IsBreak ? "Русские строчные буквы. Шрифт: ISOCPEUR / GOST Common. Затем укажите две точки."
                : "Шрифт: ISOCPEUR / GOST Common. Размеры — на бумаге; отметки — в метрах.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            note.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            fields.Children.Add(note);
            _error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            _error.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
            fields.Children.Add(_error);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0) };
            Grid.SetRow(buttons, 1); root.Children.Add(buttons);
            var cancel = new Button { Content = "Отмена", IsCancel = true, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
            cancel.Click += (_, _) => DialogResult = false;
            _draw = new Button { Content = "Указать точки", IsDefault = true, Style = (Style)FindResource("PrimaryButton") };
            _draw.Click += (_, _) => { if (ReadOptions()) DialogResult = true; };
            buttons.Children.Add(cancel); buttons.Children.Add(_draw);
            foreach (var editor in new[] { _mark, _scale, _height, _stroke, _thickness, _note, _diameter }) editor.TextChanged += (_, _) => ReadOptions();
            _kind.SelectionChanged += (_, _) => ReadOptions();
            ReadOptions();
        }

        private TextBox Editor(string value) => new TextBox { Text = value, Margin = new Thickness(0, 4, 0, 12),
            Style = (Style)FindResource("ToolTextBox") };

        private static void AddField(Panel panel, string caption, Control control)
        {
            panel.Children.Add(new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap });
            AutomationProperties.SetName(control, caption);
            panel.Children.Add(control);
        }

        private bool ReadOptions()
        {
            try
            {
                if (!(_kind.SelectedValue is DrawingSymbolKind kind)) throw new ArgumentException("Выберите тип обозначения.");
                var value = new DrawingSymbolOptions
                {
                    Kind = kind,
                    Mark = _mark.Text.Trim(), Scale = Number(_scale.Text), TextHeight = Number(_height.Text),
                    StrokeLength = Number(_stroke.Text), MainThickness = Number(_thickness.Text),
                    Note = _note.Text.Trim(), Diameter = Number(_diameter.Text)
                };
                value.Validate(); Options = value; _error.Text = ""; _draw.IsEnabled = true;
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException)
            {
                _error.Text = ex is ArgumentException ? ex.Message : "Введите числовые размеры и масштаб.";
                _draw.IsEnabled = false;
                return false;
            }
        }

        private static double Number(string text) => double.Parse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
