// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

// File: AutocadTools/Views/StampWindow.xaml.cs
using System.Windows;
using System;
using System.IO;
using AutocadTools.Models;
using AutocadTools.Services;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
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
        private void WriteDrawing(object sender, RoutedEventArgs args)
        {
            var targets = AcadApp.DocumentManager.Cast<Autodesk.AutoCAD.ApplicationServices.Document>()
                .Select(document => new StampTransferTarget
                {
                    Name = Path.GetFileName(document.Name), Location = document.Name,
                    Write = properties => StampDrawingTransfer.Write(document, properties)
                }).ToList();
            new StampTransferWindow((ViewModels.StampPropsViewModel)DataContext, targets, false) { Owner = this }.ShowDialog();
        }

        private void WriteWord(object sender, RoutedEventArgs args)
        {
            try
            {
                var targets = WordStampTransfer.GetOpenDocuments();
                new StampTransferWindow((ViewModels.StampPropsViewModel)DataContext, targets, true) { Owner = this }.ShowDialog();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Записать в Word"); }
        }
    }
}
