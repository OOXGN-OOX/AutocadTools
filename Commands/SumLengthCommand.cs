// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using Autodesk.AutoCAD.Runtime;
using AutocadTools.ViewModels;
using AutocadTools.Views;

namespace AutocadTools.Commands
{
    public sealed class SumLengthCommand
    {
        private static SumLengthWindow _window;

        [CommandMethod("SUMLENGTH")]
        public void ShowWindow()
        {
            // Если окно уже открыто - просто активируем его
            if (_window != null && _window.IsLoaded)
            {
                _window.Activate();
                // Обновляем данные при активации
                if (_window.DataContext is SumLengthViewModel vm)
                {
                    vm.CalculateFromCurrentSelection();
                }
                return;
            }

            // Создаем ViewModel и окно
            var viewModel = new SumLengthViewModel();
            _window = new SumLengthWindow { DataContext = viewModel };

            // Обнуляем ссылку при закрытии
            _window.Closed += (s, e) => _window = null;

            _window.Show();
        }
    }
}