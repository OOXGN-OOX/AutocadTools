// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace AutocadTools.Views
{
    public partial class AboutWindow : Window
    {
        private bool _settingsLoaded;
        public AboutWindow()
        {
            InitializeComponent();
            UiTheme.Apply(this);
            LoadVersion();
            AutoUpdateCheck.IsChecked = Services.SettingsService.Settings.AutoCheckUpdates;
            AutoInstallUpdateCheck.IsChecked = Services.SettingsService.Settings.AutoInstallUpdates;
            PrereleaseUpdateCheck.IsChecked = Services.SettingsService.Settings.IncludePrereleaseUpdates;
            _settingsLoaded = true;
        }

        private void UpdateSettingsChanged(object sender, RoutedEventArgs e)
        {
            if (!_settingsLoaded) return;
            bool wasAutomatic = Services.SettingsService.Settings.AutoInstallUpdates;
            Services.SettingsService.Settings.AutoCheckUpdates = AutoUpdateCheck.IsChecked == true;
            Services.SettingsService.Settings.AutoInstallUpdates = AutoInstallUpdateCheck.IsChecked == true;
            Services.SettingsService.Settings.IncludePrereleaseUpdates = PrereleaseUpdateCheck.IsChecked == true;
            Services.SettingsService.Save();
            if (wasAutomatic && !Services.SettingsService.Settings.AutoInstallUpdates) Services.UpdateCheckService.ApplySettings();
        }

        private void LoadVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = $"Версия {version?.Major}.{version?.Minor}.{version?.Build}";
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            }
            catch { }
        }
    }
}
