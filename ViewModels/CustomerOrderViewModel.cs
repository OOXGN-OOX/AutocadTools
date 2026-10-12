// Copyright (c) 2026 OOXGN (GitHub: OOXGN-OOX). All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AutocadTools.Models;

namespace AutocadTools.ViewModels
{
    public sealed class CustomerOrderViewModel : INotifyPropertyChanged
    {
        // Требование: "по умолчанию все чекбоксы должны стоять"
        private bool _createNewFile = true;
        private bool _explodeBlocks = true;
        private bool _explodePolylines = true;
        private bool _explodeText = true;
        private bool _purgeUnused = true;
        private bool _blockCopy = true;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool CreateNewFile
        {
            get => _createNewFile;
            set { _createNewFile = value; OnPropertyChanged(); }
        }

        public bool ExplodeBlocks
        {
            get => _explodeBlocks;
            set { _explodeBlocks = value; OnPropertyChanged(); }
        }

        public bool ExplodePolylines
        {
            get => _explodePolylines;
            set { _explodePolylines = value; OnPropertyChanged(); }
        }

        public bool ExplodeText
        {
            get => _explodeText;
            set { _explodeText = value; OnPropertyChanged(); }
        }

        public bool PurgeUnused
        {
            get => _purgeUnused;
            set { _purgeUnused = value; OnPropertyChanged(); }
        }

        public bool BlockCopy
        {
            get => _blockCopy;
            set { _blockCopy = value; OnPropertyChanged(); }
        }

        public CustomerOrderOptions BuildOptions() => new CustomerOrderOptions
        {
            CreateNewFile = CreateNewFile,
            ExplodeBlocks = ExplodeBlocks,
            ExplodePolylines = ExplodePolylines,
            ExplodeText = ExplodeText,
            PurgeUnused = PurgeUnused,
            BlockCopy = BlockCopy
        };

        private void OnPropertyChanged([CallerMemberName] string prop = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}
