// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using AutocadTools.Models;
using AutocadTools.Services;
using AutocadTools.ViewModels;
using Newtonsoft.Json.Linq;

namespace AutocadTools.Views
{
    public partial class StampTransferWindow : Window
    {
        private readonly StampPropsViewModel _source;
        private readonly List<StampTransferTarget> _targets;
        private readonly string _section;
        private bool _ready;
        public StampTransferWindow(StampPropsViewModel source, IEnumerable<StampTransferTarget> targets, bool word)
        {
            InitializeComponent();
            UiTheme.Apply(this);
            _source = source;
            _targets = targets.ToList();
            _section = word ? "StampTransferWord" : "StampTransferDwg";
            Title = word ? "Записать в Word" : "Записать в DWG";
            TargetsGrid.ItemsSource = _targets;
            EmptyValues.IsChecked = UserPreferences.Read(_section).Value<bool?>("Empty") ?? false;
            _ready = true;
            if (_targets.Count == 0) ResultText.Text = "Нет доступных открытых документов.";
            Closed += (_, _) => { foreach (var target in _targets) target.Dispose(); };
        }
        private void EmptyChanged(object sender, RoutedEventArgs args)
        {
            if (_ready) UserPreferences.Write(_section, new JObject { ["Empty"] = EmptyValues.IsChecked == true });
        }
        private void WriteSelected(object sender, RoutedEventArgs args)
        {
            TargetsGrid.CommitEdit();
            TargetsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
            var selected = _targets.Where(t => t.IsSelected).ToList();
            if (selected.Count == 0) { ResultText.Text = "Выберите документы галочками в списке."; return; }
            StampProperties properties;
            try { properties = _source.CaptureValues(EmptyValues.IsChecked == true); }
            catch (ArgumentException ex) { ResultText.Text = ex.Message; return; }
            int count = 0;
            foreach (var target in selected)
            {
                try { target.Write(properties); target.SetStatus("Записано"); count++; }
                catch (Exception ex) { target.SetStatus(ex.Message); }
            }
            ResultText.Text = $"Записано: {count} из {selected.Count}. Сохраните изменённые документы.";
        }
        private void CloseClick(object sender, RoutedEventArgs args) => Close();
    }
}
