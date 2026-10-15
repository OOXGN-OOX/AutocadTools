// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

// File: AutocadTools/Views/StampWindow.xaml.cs
using System.Windows;

namespace AutocadTools.Views
{
    public partial class StampWindow : Window
    {
        public StampWindow(ViewModels.StampPropsViewModel viewModel)
        {
            InitializeComponent();
            UiTheme.Apply(this);
            DataContext = viewModel;
        }
    }
}
