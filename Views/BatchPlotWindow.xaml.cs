// Copyright (c) 2026 OOXGN (GitHub: OOXGN-OOX). All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// File: Views/BatchPlotWindow.xaml.cs

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AutocadTools.Models;
using AutocadTools.ViewModels;

namespace AutocadTools.Views
{
    public partial class BatchPlotWindow : Window
    {
        // Служебные поля для "excel‑протяжки" номера листа
        private DataGrid _sheetFillGrid;
        private int _sheetFillStartRowIndex = -1;
        private int _sheetFillLastRowIndex = -1;
        private int _sheetFillStartValue;
        private bool _sheetFillActive;

        public BatchPlotWindow()
        {
            InitializeComponent();
            UiTheme.Apply(this);

            // DataContext задаём тут
            var vm = new BatchPlotViewModel();
            DataContext = vm;

            vm.RequestClose += OnRequestClose;
            Closed += (_, _) => vm.RequestClose -= OnRequestClose;
        }

        private void OnRequestClose(object sender, EventArgs e)
        {
            // Если окно модальное (ShowModalWindow), Close() корректно возвращает управление в AutoCAD,
            // а публикация продолжает идти, показывая прогресс в AutoCAD.
            Close();
        }

        #region Excel-like fill for "SheetNumber"

        private void SheetFill_DragStarted(object sender, DragStartedEventArgs e)
        {
            _sheetFillActive = false;
            _sheetFillGrid = null;
            _sheetFillStartRowIndex = -1;
            _sheetFillLastRowIndex = -1;

            if (sender is not Thumb thumb)
                return;

            // Находим DataGridRow и DataGrid по визуальному дереву
            var row = FindParent<DataGridRow>(thumb);
            var grid = FindParent<DataGrid>(thumb);
            if (row == null || grid == null)
                return;

            int rowIndex = grid.ItemContainerGenerator.IndexFromContainer(row);
            if (rowIndex < 0)
                return;

            // Достаём модель строки и начальное значение SheetNumber
            if (row.Item is not BatchPlotItem item)
                return;

            if (!int.TryParse(item.SheetNumber, out _sheetFillStartValue))
                return; // если там не число — не активируем протяжку

            _sheetFillGrid = grid;
            _sheetFillStartRowIndex = rowIndex;
            _sheetFillLastRowIndex = rowIndex;
            _sheetFillActive = true;

            e.Handled = true;
        }

        private void SheetFill_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!_sheetFillActive || _sheetFillGrid == null)
                return;

            int currentRowIndex = GetRowIndexAtCurrentMousePosition(_sheetFillGrid);
            if (currentRowIndex < 0 || currentRowIndex == _sheetFillLastRowIndex)
                return;

            _sheetFillLastRowIndex = currentRowIndex;

            int from = Math.Min(_sheetFillStartRowIndex, currentRowIndex);
            int to = Math.Max(_sheetFillStartRowIndex, currentRowIndex);

            if (DataContext is not BatchPlotViewModel vm || vm.Items == null)
                return;

            // Заполняем номера листов инкрементами от стартового
            for (int i = from; i <= to && i < vm.Items.Count; i++)
            {
                int offset = i - _sheetFillStartRowIndex;
                int value = _sheetFillStartValue + offset;
                vm.Items[i].SheetNumber = value.ToString();
            }

            e.Handled = true;
        }

        private void SheetFill_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            _sheetFillActive = false;
            _sheetFillGrid = null;
            _sheetFillStartRowIndex = -1;
            _sheetFillLastRowIndex = -1;
            e.Handled = true;
        }

        #endregion

        #region Helpers

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject current = child;
            while (current != null)
            {
                if (current is T typed)
                    return typed;

                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static int GetRowIndexAtCurrentMousePosition(DataGrid grid)
        {
            Point pos = Mouse.GetPosition(grid);
            HitTestResult hit = VisualTreeHelper.HitTest(grid, pos);
            if (hit == null)
                return -1;

            DependencyObject dep = hit.VisualHit;
            var row = FindParent<DataGridRow>(dep);
            if (row == null)
                return -1;

            return grid.ItemContainerGenerator.IndexFromContainer(row);
        }

        #endregion
    }
}
