// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System.Collections.ObjectModel;
using AutocadTools.ViewModels;

namespace AutocadTools.Models
{
    /// <summary>
    /// Единицы измерения длины
    /// </summary>
    public enum LengthUnit
    {
        Millimeters = 0,
        Meters = 1,
        Kilometers = 2
    }

    /// <summary>
    /// Единицы измерения площади
    /// </summary>
    public enum AreaUnit
    {
        SquareMillimeters = 0,
        SquareMeters = 1,
        SquareKilometers = 2
    }

    /// <summary>
    /// Одиночный объект (например, конкретная линия)
    /// </summary>
    public class LengthItem : BaseViewModel
    {
        private double _scaleFactor = 1.0;
        private LengthUnit _lengthUnit = LengthUnit.Millimeters;
        private AreaUnit _areaUnit = AreaUnit.SquareMillimeters;

        public ObjectId ObjectId { get; set; }
        public string Handle { get; set; }
        public double RawLength { get; set; }
        public double RawArea { get; set; }
        public bool IsClosed { get; set; }
        public Point3d StartPoint { get; set; }
        public Point3d EndPoint { get; set; }

        public double ScaleFactor
        {
            get => _scaleFactor;
            set { _scaleFactor = value; OnPropertyChanged(); NotifyAllChanged(); }
        }

        public LengthUnit LengthUnit
        {
            get => _lengthUnit;
            set { _lengthUnit = value; OnPropertyChanged(); OnPropertyChanged(nameof(LengthFormatted)); }
        }

        public AreaUnit AreaUnit
        {
            get => _areaUnit;
            set { _areaUnit = value; OnPropertyChanged(); OnPropertyChanged(nameof(AreaFormatted)); }
        }

        public double Length => RawLength * ScaleFactor;
        public double Area => RawArea * ScaleFactor * ScaleFactor;
        public bool HasArea => IsClosed && RawArea > 0;

        // Для замкнутых объектов длина = периметр
        public string LengthLabel => IsClosed ? "Периметр" : "Длина";
        public string LengthFormatted => FormatLength(Length, LengthUnit);
        public string AreaFormatted => FormatArea(Area, AreaUnit);

        public string CoordinatesInfo => $"({StartPoint.X:F0}, {StartPoint.Y:F0}) > ({EndPoint.X:F0}, {EndPoint.Y:F0})";

        private void NotifyAllChanged()
        {
            OnPropertyChanged(nameof(Length));
            OnPropertyChanged(nameof(Area));
            OnPropertyChanged(nameof(LengthFormatted));
            OnPropertyChanged(nameof(AreaFormatted));
        }

        public static string FormatLength(double value, LengthUnit unit)
        {
            return unit switch
            {
                LengthUnit.Meters => $"{value / 1000:N2} м",
                LengthUnit.Kilometers => $"{value / 1000000:N3} км",
                _ => $"{value:N2} мм"
            };
        }

        public static string FormatArea(double value, AreaUnit unit)
        {
            return unit switch
            {
                AreaUnit.SquareMeters => $"{value / 1000000:N2} м?",
                AreaUnit.SquareKilometers => $"{value / 1000000000000:N6} км?",
                _ => $"{value:N2} мм?"
            };
        }
    }

    /// <summary>
    /// Группа объектов (например, "Отрезок", "Полилиния")
    /// </summary>
    public class LengthGroup : BaseViewModel
    {
        private double _scaleFactor = 1.0;
        private LengthUnit _lengthUnit = LengthUnit.Millimeters;
        private AreaUnit _areaUnit = AreaUnit.SquareMillimeters;

        public string ObjectType { get; set; }
        public int Count { get; set; }
        public double RawTotalLength { get; set; }
        public double RawTotalArea { get; set; }

        public double ScaleFactor
        {
            get => _scaleFactor;
            set
            {
                _scaleFactor = value;
                OnPropertyChanged();
                NotifyAllChanged();
                foreach (var item in Items) item.ScaleFactor = value;
            }
        }

        public LengthUnit LengthUnit
        {
            get => _lengthUnit;
            set
            {
                _lengthUnit = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalLengthFormatted));
                foreach (var item in Items) item.LengthUnit = value;
            }
        }

        public AreaUnit AreaUnit
        {
            get => _areaUnit;
            set
            {
                _areaUnit = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalAreaFormatted));
                foreach (var item in Items) item.AreaUnit = value;
            }
        }

        public double TotalLength => RawTotalLength * ScaleFactor;
        public double TotalArea => RawTotalArea * ScaleFactor * ScaleFactor;
        public bool HasArea => RawTotalArea > 0;

        public string TotalLengthFormatted => LengthItem.FormatLength(TotalLength, LengthUnit);
        public string TotalAreaFormatted => LengthItem.FormatArea(TotalArea, AreaUnit);

        public ObservableCollection<LengthItem> Items { get; set; } = new ObservableCollection<LengthItem>();

        private void NotifyAllChanged()
        {
            OnPropertyChanged(nameof(TotalLength));
            OnPropertyChanged(nameof(TotalArea));
            OnPropertyChanged(nameof(TotalLengthFormatted));
            OnPropertyChanged(nameof(TotalAreaFormatted));
            OnPropertyChanged(nameof(HasArea));
        }
    }
}