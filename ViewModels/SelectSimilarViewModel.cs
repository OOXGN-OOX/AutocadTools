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
    public class SelectSimilarViewModel : BaseViewModel
    {
        private SelectSimilarOptions _options;

        public SelectSimilarOptions Options
        {
            get => _options;
            set { _options = value; OnPropertyChanged(); }
        }

        public ICommand ExecuteCommand { get; }
        public ICommand CancelCommand { get; }

        public bool? DialogResult { get; private set; }
        public System.Action CloseAction { get; set; }

        private const string REG_KEY = @"Software\SelectSimilar";

        public SelectSimilarViewModel()
        {
            _options = LoadSettings();
            ExecuteCommand = new RelayCommand(x => Execute());
            CancelCommand = new RelayCommand(x => Cancel());
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

        // Упрощенное сохранение/загрузка (пример для пары свойств, расширьте для остальных по аналогии)
        private SelectSimilarOptions LoadSettings()
        {
            var opts = new SelectSimilarOptions();
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(REG_KEY))
                {
                    if (key != null)
                    {
                        opts.MatchLayer = GetBool(key, "MatchLayer", true);
                        opts.MatchColor = GetBool(key, "MatchColor", false);
                        opts.BlockName = GetBool(key, "BlockName", true);
                        // ... добавьте загрузку остальных свойств ...
                    }
                }
            }
            catch { }
            return opts;
        }

        private void SaveSettings()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(REG_KEY))
                {
                    key.SetValue("MatchLayer", Options.MatchLayer ? 1 : 0);
                    key.SetValue("MatchColor", Options.MatchColor ? 1 : 0);
                    // ... добавьте сохранение остальных свойств ...
                }
            }
            catch { }
        }

        private bool GetBool(RegistryKey key, string name, bool def)
        {
            return int.Parse(key.GetValue(name, def ? 1 : 0).ToString()) == 1;
        }
    }
}