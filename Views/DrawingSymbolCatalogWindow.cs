// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AutocadTools.Models;

namespace AutocadTools.Views
{
    public sealed class DrawingSymbolCatalogWindow : Window
    {
        private readonly ListBox _list;
        private readonly TextBlock _title, _standard, _sample;
        private readonly System.Windows.Shapes.Path _shape;
        private readonly Button _insert;
        public DrawingSymbolDefinition Selection => _list.SelectedItem as DrawingSymbolDefinition;

        public DrawingSymbolCatalogWindow(DrawingSymbolKind selected = DrawingSymbolKind.Section)
        {
            Title = "Условные обозначения — оформление чертежей";
            Width = 820; Height = 570; MinWidth = 660; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("/" + typeof(UiTheme).Assembly.GetName().Name + ";component/Views/Styles.xaml", UriKind.Relative) });
            Style = (Style)FindResource("ToolWindow"); UiTheme.Apply(this);
            var root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;
            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(columns);
            var left = new DockPanel { Margin = new Thickness(0, 0, 20, 0) }; columns.Children.Add(left);
            var searchLabel = new TextBlock { Text = "Поиск по названию или группе", Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(searchLabel, Dock.Top); left.Children.Add(searchLabel);
            var search = new TextBox { Style = (Style)FindResource("ToolTextBox"), Margin = new Thickness(0, 0, 0, 12),
                ToolTip = "Поиск по названию или группе: отметка, узел, выноска…" };
            AutomationProperties.SetName(search, "Поиск условных обозначений");
            DockPanel.SetDock(search, Dock.Top); left.Children.Add(search);
            _list = new ListBox { DisplayMemberPath = "Title", ItemsSource = DrawingSymbolCatalog.All };
            _list.SetResourceReference(Control.BackgroundProperty, "InputBackground");
            _list.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
            var itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 7, 8, 7)));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            itemStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding("Group")));
            var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("SelectionBackground")));
            selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextPrimary")));
            itemStyle.Triggers.Add(selectedTrigger); _list.ItemContainerStyle = itemStyle;
            left.Children.Add(_list);
            var right = new StackPanel(); Grid.SetColumn(right, 1); columns.Children.Add(right);
            _title = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
            right.Children.Add(_title);
            var diagram = new Canvas { Width = 200, Height = 110 };
            _shape = new System.Windows.Shapes.Path { StrokeThickness = 1.4 };
            _shape.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "TextPrimary");
            diagram.Children.Add(_shape);
            right.Children.Add(new Viewbox { Child = diagram, Height = 150, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 12) });
            _sample = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("ISOCPEUR, GOST Common"),
                Margin = new Thickness(0, 0, 0, 20) };
            right.Children.Add(_sample);
            _standard = new TextBlock { TextWrapping = TextWrapping.Wrap };
            _standard.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary"); right.Children.Add(_standard);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0) };
            Grid.SetRow(buttons, 1); root.Children.Add(buttons);
            var cancel = new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
            cancel.Click += (_, _) => DialogResult = false; buttons.Children.Add(cancel);
            _insert = new Button { Content = "Настроить и вставить", IsDefault = true, Style = (Style)FindResource("PrimaryButton") };
            _insert.Click += (_, _) => { if (Selection != null) DialogResult = true; }; buttons.Children.Add(_insert);
            _list.SelectionChanged += (_, _) => { if (_list.SelectedItem != null) _list.ScrollIntoView(_list.SelectedItem); Refresh(); };
            _list.MouseDoubleClick += (_, _) => { if (Selection != null) DialogResult = true; };
            search.TextChanged += (_, _) =>
            {
                string query = search.Text.Trim();
                _list.ItemsSource = DrawingSymbolCatalog.All.Where(d => (d.Title + " " + d.Group).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                _list.SelectedIndex = _list.Items.Count > 0 ? 0 : -1; Refresh();
            };
            _list.SelectedItem = DrawingSymbolCatalog.Get(selected); Refresh();
        }

        private void Refresh()
        {
            var selection = Selection;
            _insert.IsEnabled = selection != null;
            _title.Text = selection?.Title ?? "Ничего не найдено";
            _shape.Data = selection == null || string.IsNullOrEmpty(selection.SamplePath) ? null : Geometry.Parse(selection.SamplePath);
            _sample.Text = selection == null ? "" : "Обозначение: " + selection.SampleText;
            _standard.Text = selection == null ? "" : selection.Group + "\n\n" + selection.Standard;
        }
    }
}
