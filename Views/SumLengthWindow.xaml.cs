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
    public partial class SumLengthWindow : Window
    {
        public SumLengthWindow()
        {
            InitializeComponent();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Отписываемся от событий при закрытии окна
            if (DataContext is SumLengthViewModel vm)
            {
                vm.StopListening();
            }
        }
    }
}