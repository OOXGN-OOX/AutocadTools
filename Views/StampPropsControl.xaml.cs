// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

// File: AutocadTools/Views/StampWindow.xaml.cs
using System.Windows;
using System.Linq;
using System.Windows.Controls;

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

        private void Signature_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = (ViewModels.StampPropsViewModel)DataContext;
            var key = (sender as Button)?.Tag as string ?? "Signer1";
            var dialog = new SignatureWindow(viewModel.Signatures.Values.ToArray(), key) { Owner = this };
            if (dialog.ShowDialog() == true) viewModel.Signatures[dialog.SlotKey].Signature = dialog.Result;
        }
    }
}
