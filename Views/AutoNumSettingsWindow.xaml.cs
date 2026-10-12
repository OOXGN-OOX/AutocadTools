// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using System.Windows;
using AutocadTools.ViewModels;

namespace AutocadTools.Views
{
    /// <summary>
    /// Interaction logic for AutoNumSettingsWindow.xaml
    /// </summary>
    public partial class AutoNumSettingsWindow : Window
    {
        public AutoNumSettingsViewModel ViewModel { get; private set; }

        public AutoNumSettingsWindow(AutoNumSettingsViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = viewModel;

            // Subscribe to DialogResult changes
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(viewModel.DialogResult) && viewModel.DialogResult.HasValue)
                {
                    DialogResult = viewModel.DialogResult.Value;
                    Close();
                }
            };
        }
    }
}
