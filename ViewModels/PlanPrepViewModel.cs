// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using System.Windows.Input;
using AutocadTools.Models;

namespace AutocadTools.ViewModels
{
    public class PlanPrepViewModel : BaseViewModel
    {
        private PlanPrepOptions _options;

        public PlanPrepOptions Options
        {
            get => _options;
            set { _options = value; OnPropertyChanged(); }
        }

        public ICommand SelectAllCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand ExecuteCommand { get; }
        public ICommand CancelCommand { get; }

        public bool? DialogResult { get; private set; }
        public System.Action CloseAction { get; set; }

        public PlanPrepViewModel()
        {
            _options = new PlanPrepOptions();

            SelectAllCommand = new RelayCommand(param => SetAll(true));
            ClearAllCommand = new RelayCommand(param => SetAll(false));
            ExecuteCommand = new RelayCommand(Execute);
            CancelCommand = new RelayCommand(Cancel);
        }

        private void SetAll(bool val)
        {
            Options.ExplodeAllBlocks = val;
            Options.ExplodeHatches = val;
            Options.DeleteSolidHatches = val;

            // Новые опции
            Options.DeleteWipeouts = val;
            Options.DeletePoints = val;

            Options.JoinLines = val;
            Options.DeleteShortSegments = val;
            Options.SetGrayColor = val;
            Options.CreateBlock = val;

            var temp = Options;
            Options = null;
            Options = temp;
        }

        private void Execute(object param)
        {
            DialogResult = true;
            CloseAction?.Invoke();
        }

        private void Cancel(object param)
        {
            DialogResult = false;
            CloseAction?.Invoke();
        }
    }
}