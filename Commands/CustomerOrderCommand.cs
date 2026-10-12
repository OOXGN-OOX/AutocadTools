// Copyright (c) 2026 OOXGN (GitHub: OOXGN-OOX). All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using System;
using System.Windows.Interop;
using AutocadTools.Services;
using AutocadTools.ViewModels;
using AutocadTools.Views;

// Псевдоним
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutocadTools.Commands
{
    public sealed class CustomerOrderCommand
    {
        [CommandMethod("AUTOCADTOOLS_CUSTOMERORDER")]
        public void Run()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
            {
                var vm = new CustomerOrderViewModel();
                var win = new CustomerOrderWindow { DataContext = vm };

                // Привязка Owner к окну AutoCAD (чтобы модалка была нормальной)
                new WindowInteropHelper(win).Owner = AcadApp.MainWindow.Handle;

                AcadApp.ShowModalWindow(win);

                if (win.DialogResult != true)
                    return;

                var options = vm.BuildOptions();
                var service = new CustomerOrderService();
                service.Execute(doc, options);
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage($"\n[Autocad Tools CustomerOrder] Ошибка: {ex}\n");
            }
        }
    }
}
