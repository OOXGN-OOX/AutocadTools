// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AutocadTools.Services;

namespace AutocadTools.Views
{
    public sealed class CommandAliasesWindow : Window
    {
        private readonly CommandAliases _aliases;
        private readonly DataGrid _table;
        private readonly TextBlock _status;
        private readonly Button _apply;

        public CommandAliasesWindow(CommandAliases aliases)
        {
            _aliases = aliases;
            Title = "Команды и сокращения";
            Width = 1080; Height = 650; MinWidth = 880; MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/" + typeof(CommandAliasesWindow).Assembly.GetName().Name + ";component/Views/Styles.xaml", UriKind.Relative) });
            SetResourceReference(StyleProperty, "ToolWindow"); UiTheme.Apply(this);
            var root = new Grid { Margin = new Thickness(20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;
            var hint = new TextBlock { Text = "Оба имени запускают одну команду. Собственные сокращения вводите через запятую.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
            root.Children.Add(hint);
            var search = new TextBox { ToolTip = "Поиск по названию или команде" };
            search.SetResourceReference(StyleProperty, "ToolTextBox");
            System.Windows.Automation.AutomationProperties.SetName(search, "Поиск команды");
            var searchRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            searchRow.Children.Add(new TextBlock { Text = "Поиск", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
            searchRow.Children.Add(search);
            Grid.SetRow(searchRow, 1); root.Children.Add(searchRow);
            _table = new DataGrid { ItemsSource = aliases.Rows, AutoGenerateColumns = false, CanUserAddRows = false,
                CanUserDeleteRows = false, CanUserReorderColumns = false, SelectionMode = DataGridSelectionMode.Single };
            _table.Columns.Add(new DataGridTextColumn { Header = "Инструмент", MinWidth = 230, Binding = new Binding("Command.Title"), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            _table.Columns.Add(new DataGridTextColumn { Header = "English", Binding = new Binding("Command.English"), IsReadOnly = true, Width = 165 });
            _table.Columns.Add(new DataGridTextColumn { Header = "Русский", Binding = new Binding("Command.Russian"), IsReadOnly = true, Width = 220 });
            _table.Columns.Add(new DataGridTextColumn { Header = "Сокращения", EditingElementStyle = (Style)FindResource("ToolTextBox"), Binding = new Binding("Aliases") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 170 });
            Grid.SetRow(_table, 2); root.Children.Add(_table);
            var view = CollectionViewSource.GetDefaultView(aliases.Rows);
            view.Filter = item => item is CommandAlias row && new[] { row.Command.Title, row.Command.English, row.Command.Russian, row.Aliases }
                .Any(s => s.IndexOf(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
            search.TextChanged += (_, _) => { _table.CommitEdit(); view.Refresh(); };
            _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12), MinHeight = 36 };
            Grid.SetRow(_status, 3); root.Children.Add(_status);
            var actions = new DockPanel();
            var suggest = new Button { Content = "Предложить сокращения", Padding = new Thickness(12, 7, 12, 7), HorizontalAlignment = HorizontalAlignment.Left };
            suggest.Click += (_, _) => { _table.CommitEdit(); aliases.Suggest(); };
            actions.Children.Add(suggest);
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(8, 0, 8, 0), Padding = new Thickness(12, 7, 12, 7) };
            cancel.Click += (_, _) => DialogResult = false; right.Children.Add(cancel);
            _apply = new Button { Content = "Применить", Padding = new Thickness(16, 7, 16, 7) };
            _apply.SetResourceReference(StyleProperty, "PrimaryButton");
            _apply.Click += (_, _) => Save(); right.Children.Add(_apply);
            actions.Children.Add(right); Grid.SetRow(actions, 4); root.Children.Add(actions);
            foreach (var row in aliases.Rows) row.PropertyChanged += (_, _) => Validate();
            Validate();
        }

        private void Validate()
        {
            string error = _aliases.Validate();
            _status.Text = error ?? "Изменения действуют в текущем профиле AutoCAD. Пустая ячейка удаляет назначенное здесь сокращение.";
            _status.SetResourceReference(TextBlock.ForegroundProperty, error == null ? "TextSecondary" : "ErrorBrush");
            _apply.IsEnabled = error == null;
        }

        private void Save()
        {
            if (!_table.CommitEdit(DataGridEditingUnit.Cell, true) || !_table.CommitEdit(DataGridEditingUnit.Row, true)) return;
            try { _aliases.Save(); DialogResult = true; }
            catch (Exception ex) { _status.Text = ex.Message; _status.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush"); }
        }
    }
}
