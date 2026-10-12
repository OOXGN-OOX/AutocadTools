// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using AutocadTools.Models;
using AutocadTools.Services;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutocadTools.ViewModels
{
    public class SumLengthViewModel : BaseViewModel
    {
        #region Fields

        private double _rawTotalLength;
        private double _rawTotalArea;
        private int _scaleNumerator = 1;
        private int _scaleDenominator = 1;
        private LengthUnit _selectedLengthUnit = LengthUnit.Millimeters;
        private AreaUnit _selectedAreaUnit = AreaUnit.SquareMillimeters;
        private ObservableCollection<LengthGroup> _groups;
        private bool _isListening;

        #endregion

        #region Properties

        public ObservableCollection<LengthGroup> Groups
        {
            get => _groups;
            set { _groups = value; OnPropertyChanged(); OnPropertyChanged(nameof(GroupCount)); }
        }

        public double TotalLength => _rawTotalLength * ScaleFactor;
        public double TotalArea => _rawTotalArea * ScaleFactor * ScaleFactor;
        public bool HasTotalArea => _rawTotalArea > 0;

        public string TotalLengthFormatted => LengthItem.FormatLength(TotalLength, SelectedLengthUnit);
        public string TotalAreaFormatted => LengthItem.FormatArea(TotalArea, SelectedAreaUnit);

        public int TotalCount => Groups?.Sum(g => g.Count) ?? 0;
        public int GroupCount => Groups?.Count ?? 0;

        public int ScaleNumerator
        {
            get => _scaleNumerator;
            set
            {
                if (value < 1) value = 1;
                _scaleNumerator = value;
                OnPropertyChanged();
                UpdateScaleForGroups();
                SaveSettings();
            }
        }

        public int ScaleDenominator
        {
            get => _scaleDenominator;
            set
            {
                if (value < 1) value = 1;
                _scaleDenominator = value;
                OnPropertyChanged();
                UpdateScaleForGroups();
                SaveSettings();
            }
        }

        public double ScaleFactor => (double)_scaleNumerator / _scaleDenominator;

        public string ScaleInfo
        {
            get
            {
                if (_scaleNumerator == _scaleDenominator) return "";
                if (_scaleNumerator == 1 && _scaleDenominator == 1000) return "(мм > м)";
                if (_scaleNumerator == 1000 && _scaleDenominator == 1) return "(м > мм)";
                return $"(?{ScaleFactor:G4})";
            }
        }

        public LengthUnit SelectedLengthUnit
        {
            get => _selectedLengthUnit;
            set
            {
                _selectedLengthUnit = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalLengthFormatted));
                UpdateLengthUnitForGroups();
                SaveSettings();
            }
        }

        public AreaUnit SelectedAreaUnit
        {
            get => _selectedAreaUnit;
            set
            {
                _selectedAreaUnit = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalAreaFormatted));
                UpdateAreaUnitForGroups();
                SaveSettings();
            }
        }

        public Array LengthUnits => Enum.GetValues(typeof(LengthUnit));
        public Array AreaUnits => Enum.GetValues(typeof(AreaUnit));

        #endregion

        #region Commands

        public ICommand ZoomToObjectCommand { get; }
        public ICommand CopyLengthCommand { get; }
        public ICommand CopyAreaCommand { get; }

        #endregion

        #region Events

        public Action WindowClosing;

        #endregion

        #region Constructor

        public SumLengthViewModel()
        {
            Groups = new ObservableCollection<LengthGroup>();
            ZoomToObjectCommand = new RelayCommand(ZoomToObject);
            CopyLengthCommand = new RelayCommand(CopyLength);
            CopyAreaCommand = new RelayCommand(CopyArea);

            LoadSettings();
            StartListening();
            CalculateFromCurrentSelection();
        }

        #endregion

        #region Settings

        private void LoadSettings()
        {
            var settings = SettingsService.Settings;
            _scaleNumerator = settings.ScaleNumerator;
            _scaleDenominator = settings.ScaleDenominator;
            _selectedLengthUnit = (LengthUnit)settings.LengthUnit;
            _selectedAreaUnit = (AreaUnit)settings.AreaUnit;
        }

        private void SaveSettings()
        {
            var settings = SettingsService.Settings;
            settings.ScaleNumerator = _scaleNumerator;
            settings.ScaleDenominator = _scaleDenominator;
            settings.LengthUnit = (int)_selectedLengthUnit;
            settings.AreaUnit = (int)_selectedAreaUnit;
            SettingsService.Save();
        }

        #endregion

        #region Live Selection Listening

        public void StartListening()
        {
            if (_isListening) return;

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                doc.ImpliedSelectionChanged += OnSelectionChanged;
                _isListening = true;
            }
        }

        public void StopListening()
        {
            if (!_isListening) return;

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                doc.ImpliedSelectionChanged -= OnSelectionChanged;
                _isListening = false;
            }
        }

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            CalculateFromCurrentSelection();
        }

        #endregion

        #region Methods

        public void CalculateFromCurrentSelection()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var psr = doc.Editor.SelectImplied();
            if (psr.Status == PromptStatus.OK)
            {
                ProcessSelection(psr.Value.GetObjectIds());
            }
            else
            {
                Groups.Clear();
                _rawTotalLength = 0;
                _rawTotalArea = 0;
                UpdateTotals();
            }
        }

        private void ProcessSelection(ObjectId[] ids)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var service = new LengthService();
                var results = service.CalculateLengths(tr, ids);

                Groups.Clear();
                _rawTotalLength = 0;
                _rawTotalArea = 0;

                foreach (var group in results)
                {
                    group.ScaleFactor = ScaleFactor;
                    group.LengthUnit = SelectedLengthUnit;
                    group.AreaUnit = SelectedAreaUnit;
                    Groups.Add(group);
                    _rawTotalLength += group.RawTotalLength;
                    _rawTotalArea += group.RawTotalArea;
                }

                UpdateTotals();
                tr.Commit();
            }
        }

        private void UpdateScaleForGroups()
        {
            OnPropertyChanged(nameof(ScaleFactor));
            OnPropertyChanged(nameof(ScaleInfo));
            OnPropertyChanged(nameof(TotalLength));
            OnPropertyChanged(nameof(TotalArea));
            OnPropertyChanged(nameof(TotalLengthFormatted));
            OnPropertyChanged(nameof(TotalAreaFormatted));

            if (Groups != null)
            {
                foreach (var group in Groups)
                {
                    group.ScaleFactor = ScaleFactor;
                }
            }
        }

        private void UpdateLengthUnitForGroups()
        {
            if (Groups != null)
            {
                foreach (var group in Groups)
                {
                    group.LengthUnit = SelectedLengthUnit;
                }
            }
        }

        private void UpdateAreaUnitForGroups()
        {
            if (Groups != null)
            {
                foreach (var group in Groups)
                {
                    group.AreaUnit = SelectedAreaUnit;
                }
            }
        }

        private void UpdateTotals()
        {
            OnPropertyChanged(nameof(TotalLength));
            OnPropertyChanged(nameof(TotalArea));
            OnPropertyChanged(nameof(TotalLengthFormatted));
            OnPropertyChanged(nameof(TotalAreaFormatted));
            OnPropertyChanged(nameof(HasTotalArea));
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(GroupCount));
        }

        private void ZoomToObject(object parameter)
        {
            if (parameter is not LengthItem item) return;

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
            {
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var ent = tr.GetObject(item.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent != null)
                    {
                        var ext = ent.GeometricExtents;
                        var minPt = ext.MinPoint;
                        var maxPt = ext.MaxPoint;

                        var margin = (maxPt - minPt).Length * 0.2;
                        if (margin < 100) margin = 100;

                        var viewMin = new Point3d(minPt.X - margin, minPt.Y - margin, 0);
                        var viewMax = new Point3d(maxPt.X + margin, maxPt.Y + margin, 0);

                        doc.Editor.Zoom(new Extents3d(viewMin, viewMax));
                    }
                    tr.Commit();
                }
            }
            catch { }
        }

        private void CopyLength(object parameter)
        {
            try
            {
                Clipboard.SetText(TotalLengthFormatted);
            }
            catch { }
        }

        private void CopyArea(object parameter)
        {
            try
            {
                Clipboard.SetText(TotalAreaFormatted);
            }
            catch { }
        }

        #endregion
    }

    public static class EditorExtensions
    {
        public static void Zoom(this Editor ed, Extents3d ext)
        {
            using (var view = ed.GetCurrentView())
            {
                var center = new Point2d(
                    (ext.MinPoint.X + ext.MaxPoint.X) / 2,
                    (ext.MinPoint.Y + ext.MaxPoint.Y) / 2);

                view.CenterPoint = center;
                view.Height = ext.MaxPoint.Y - ext.MinPoint.Y;
                view.Width = ext.MaxPoint.X - ext.MinPoint.X;

                ed.SetCurrentView(view);
            }
        }
    }
}