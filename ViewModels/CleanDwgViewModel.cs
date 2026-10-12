// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using Microsoft.Win32;
using System.Windows.Input;
using AutocadTools.Models;

namespace AutocadTools.ViewModels
{
    public class CleanDwgViewModel : BaseViewModel
    {
        private CleanDwgOptions _options;

        public CleanDwgOptions Options
        {
            get => _options;
            set { _options = value; OnPropertyChanged(); }
        }

        public ICommand ExecuteCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand ClearAllCommand { get; }

        public bool? DialogResult { get; private set; }
        public System.Action CloseAction { get; set; }

        private const string REG_KEY = @"Software\CleanDwg";

        public CleanDwgViewModel()
        {
            _options = LoadSettings();

            ExecuteCommand = new RelayCommand(x => Execute());
            CancelCommand = new RelayCommand(x => Cancel());
            SelectAllCommand = new RelayCommand(x => SetAll(true));
            ClearAllCommand = new RelayCommand(x => SetAll(false));
        }

        private void Execute()
        {
            if (Options.RememberSettings) SaveSettings();
            DialogResult = true;
            CloseAction?.Invoke();
        }

        private void Cancel()
        {
            DialogResult = false;
            CloseAction?.Invoke();
        }

        private void SetAll(bool val)
        {
            Options.Points = val;
            Options.Wipeouts = val;
            Options.BlankText = val;
            Options.Groups = val;
            Options.LayerFilters = val;
            Options.RegApps = val;
            Options.PurgeAll = val;
            Options.Audit = val;
            // ... (обновить остальные поля)

            var temp = Options; Options = null; Options = temp; // Hack update UI
        }

        private CleanDwgOptions LoadSettings()
        {
            var opt = new CleanDwgOptions();
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(REG_KEY))
                {
                    if (key != null)
                    {
                        // Пример загрузки (можно добавить для всех свойств)
                        opt.Points = int.Parse(key.GetValue("Points", "0").ToString()) == 1;
                        opt.Groups = int.Parse(key.GetValue("Groups", "1").ToString()) == 1;
                        opt.PurgeAll = int.Parse(key.GetValue("PurgeAll", "1").ToString()) == 1;
                        // ... загрузите остальные по аналогии
                    }
                }
            }
            catch { }
            return opt;
        }

        private void SaveSettings()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(REG_KEY))
                {
                    key.SetValue("Points", Options.Points ? 1 : 0);
                    key.SetValue("Groups", Options.Groups ? 1 : 0);
                    key.SetValue("PurgeAll", Options.PurgeAll ? 1 : 0);
                    // ... сохраните остальные
                }
            }
            catch { }
        }
    }
}