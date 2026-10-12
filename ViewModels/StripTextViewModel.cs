// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using Microsoft.Win32; // Для работы с Registry
using System.Windows.Input;
using AutocadTools.Models;

namespace AutocadTools.ViewModels
{
    public class StripTextViewModel : BaseViewModel
    {
        private StripTextOptions _options;

        public StripTextOptions Options
        {
            get => _options;
            set { _options = value; OnPropertyChanged(); }
        }

        public ICommand SelectAllCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand OkCommand { get; }
        public ICommand CancelCommand { get; }

        public bool? DialogResult { get; private set; }
        public System.Action CloseAction { get; set; }

        public StripTextViewModel()
        {
            _options = LoadSettingsFromRegistry();

            SelectAllCommand = new RelayCommand(param => SetAll(true));
            ClearAllCommand = new RelayCommand(param => SetAll(false));
            OkCommand = new RelayCommand(ExecuteOk);
            CancelCommand = new RelayCommand(ExecuteCancel);
        }

        private void SetAll(bool value)
        {
            // Обновляем все свойства модели и уведомляем View
            Options.Alignment = value;
            Options.Color = value;
            Options.Font = value;
            Options.Height = value;
            Options.LineFeed = value;
            Options.NonBreakingSpace = value;
            Options.Oblique = value;
            Options.Overline = value;
            Options.Paragraph = value;
            Options.Stacking = value;
            Options.Tabs = value;
            Options.Tracking = value;
            Options.Underline = value;
            Options.Width = value;
            Options.BackgroundMask = value;
            Options.Fields = value;
            Options.Columns = value;

            // Трюк для обновления UI, так как Options - это тот же объект, 
            // но его поля изменились. Проще всего переприсвоить или вызвать OnPropertyChanged для всех.
            // Для простоты в WPF можно сделать так:
            var temp = Options;
            Options = null;
            Options = temp;
        }

        private void ExecuteOk(object param)
        {
            if (Options.RememberSettings)
            {
                SaveSettingsToRegistry();
            }
            DialogResult = true;
            CloseAction?.Invoke();
        }

        private void ExecuteCancel(object param)
        {
            DialogResult = false;
            CloseAction?.Invoke();
        }

        // --- Registry Logic (Matching Lisp path HKEY_CURRENT_USER\SOFTWARE\StripMtext\) ---
        private const string REG_KEY = @"Software\StripMtext";
        private const string REG_VAL_SETTINGS = "Settings"; // Строка типа "ACF..."
        private const string REG_VAL_SAVE = "Save";

        private StripTextOptions LoadSettingsFromRegistry()
        {
            var opts = new StripTextOptions();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(REG_KEY))
                {
                    if (key != null)
                    {
                        object saveVal = key.GetValue(REG_VAL_SAVE);
                        if (saveVal != null && saveVal.ToString() == "1")
                        {
                            opts.RememberSettings = true;
                            string flags = key.GetValue(REG_VAL_SETTINGS) as string;
                            if (!string.IsNullOrEmpty(flags))
                            {
                                // Парсим строку флагов как в Lisp (A, C, F...)
                                opts.Alignment = flags.Contains("A");
                                opts.Tabs = flags.Contains("B");
                                opts.Color = flags.Contains("C");
                                opts.Fields = flags.Contains("D");
                                opts.Font = flags.Contains("F");
                                opts.Height = flags.Contains("H");
                                opts.LineFeed = flags.Contains("L");
                                opts.BackgroundMask = flags.Contains("M");
                                opts.Columns = flags.Contains("N");
                                opts.Overline = flags.Contains("O");
                                opts.Paragraph = flags.Contains("P");
                                opts.Oblique = flags.Contains("Q");
                                opts.Stacking = flags.Contains("S");
                                opts.Tracking = flags.Contains("T");
                                opts.Underline = flags.Contains("U");
                                opts.Width = flags.Contains("W");
                                opts.NonBreakingSpace = flags.Contains("~");
                            }
                        }
                        else
                        {
                            opts.RememberSettings = false;
                        }
                    }
                }
            }
            catch { /* Игнорируем ошибки доступа к реестру */ }
            return opts;
        }

        private void SaveSettingsToRegistry()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(REG_KEY))
                {
                    if (key != null)
                    {
                        key.SetValue(REG_VAL_SAVE, Options.RememberSettings ? "1" : "0");

                        // Генерируем строку флагов
                        string flags = "";
                        if (Options.Alignment) flags += "A";
                        if (Options.Tabs) flags += "B";
                        if (Options.Color) flags += "C";
                        if (Options.Fields) flags += "D";
                        if (Options.Font) flags += "F";
                        if (Options.Height) flags += "H";
                        if (Options.LineFeed) flags += "L";
                        if (Options.BackgroundMask) flags += "M";
                        if (Options.Columns) flags += "N";
                        if (Options.Overline) flags += "O";
                        if (Options.Paragraph) flags += "P";
                        if (Options.Oblique) flags += "Q";
                        if (Options.Stacking) flags += "S";
                        if (Options.Tracking) flags += "T";
                        if (Options.Underline) flags += "U";
                        if (Options.Width) flags += "W";
                        if (Options.NonBreakingSpace) flags += "~";

                        key.SetValue(REG_VAL_SETTINGS, flags);
                    }
                }
            }
            catch { }
        }
    }
}