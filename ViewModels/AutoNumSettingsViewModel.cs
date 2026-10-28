// SPDX-License-Identifier: AGPL-3.0-only
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: BotPlusAutocadTool
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AutocadTools.Models;

namespace AutocadTools.ViewModels
{
    /// <summary>
    /// ViewModel для окна настроек автонумерации
    /// </summary>
    public class AutoNumSettingsViewModel : BaseViewModel
    {
        #region Constants

        private const int PreviewCount = 5;

        #endregion

        #region Fields

        private AutoNumSettings _settings;
        private bool? _dialogResult;
        private ObservableCollection<string> _availableAttributes;
        private string _startNumberText;
        private string _stepText;
        private string _digitsText;

        #endregion

        #region Constructor

        public AutoNumSettingsViewModel()
        {
            _settings = AutoNumSettings.Load();
            _availableAttributes = new ObservableCollection<string>();
            _dialogResult = false;

            OkCommand = new RelayCommand(ExecuteOk);
            CancelCommand = new RelayCommand(ExecuteCancel);
            RefreshAttributesCommand = new RelayCommand(ExecuteRefreshAttributes);

            // Загружаем список атрибутов из предвыбора
            ExecuteRefreshAttributes(null);
            
            // Инициализируем предпросмотр
            OnPropertyChanged(nameof(PreviewItems));
        }

        #endregion

        #region Properties - Core

        public AutoNumSettings Settings
        {
            get => _settings;
            set => SetProperty(ref _settings, value);
        }

        public bool? DialogResult
        {
            get => _dialogResult;
            set => SetProperty(ref _dialogResult, value);
        }

        public ObservableCollection<string> AvailableAttributes
        {
            get => _availableAttributes;
            set => SetProperty(ref _availableAttributes, value);
        }

        #endregion

        #region Properties - Numbering Format

        public string Prefix
        {
            get => _settings?.Prefix ?? string.Empty;
            set
            {
                if (_settings == null) return;
                _settings.Prefix = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PreviewItems));
            }
        }

        public string Suffix
        {
            get => _settings?.Suffix ?? string.Empty;
            set
            {
                if (_settings == null) return;
                _settings.Suffix = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PreviewItems));
            }
        }

        public string StartNumberText
        {
            get => _startNumberText ?? (_settings?.StartNumber.ToString() ?? "1");
            set
            {
                if (!SetProperty(ref _startNumberText, value)) return;
                if (int.TryParse(value, out int num) && num >= 0)
                    StartNumber = num;
            }
        }

        public int StartNumber
        {
            get => _settings?.StartNumber ?? 1;
            set
            {
                if (_settings == null || _settings.StartNumber == value) return;
                _settings.StartNumber = value;
                _startNumberText = value.ToString();
                OnPropertyChanged();
                OnPropertyChanged(nameof(StartNumberText));
                OnPropertyChanged(nameof(PreviewItems));
            }
        }

        public string StepText
        {
            get => _stepText ?? (_settings?.Step.ToString() ?? "1");
            set
            {
                if (!SetProperty(ref _stepText, value)) return;
                if (int.TryParse(value, out int num) && num > 0)
                    Step = num;
            }
        }

        public int Step
        {
            get => _settings?.Step ?? 1;
            set
            {
                if (_settings == null || _settings.Step == value) return;
                _settings.Step = value;
                _stepText = value.ToString();
                OnPropertyChanged();
                OnPropertyChanged(nameof(StepText));
                OnPropertyChanged(nameof(PreviewItems));
            }
        }

        public string DigitsText
        {
            get => _digitsText ?? (_settings?.Digits.ToString() ?? "0");
            set
            {
                if (!SetProperty(ref _digitsText, value)) return;
                if (int.TryParse(value, out int num) && num >= 0)
                    Digits = num;
            }
        }

        public int Digits
        {
            get => _settings?.Digits ?? 0;
            set
            {
                if (_settings == null || _settings.Digits == value) return;
                _settings.Digits = value;
                _digitsText = value.ToString();
                OnPropertyChanged();
                OnPropertyChanged(nameof(DigitsText));
                OnPropertyChanged(nameof(PreviewItems));
            }
        }

        #endregion

        #region Properties - Mode & Order

        public AutoNumSettings.NumberingOrder Order
        {
            get => _settings?.Order ?? AutoNumSettings.NumberingOrder.Pick;
            set
            {
                if (_settings == null) return;
                _settings.Order = value;
                OnPropertyChanged();
            }
        }

        public AutoNumSettings.NumberingMode Mode
        {
            get => _settings?.Mode ?? AutoNumSettings.NumberingMode.Text;
            set
            {
                if (_settings == null) return;
                _settings.Mode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsAttributeMode));
                OnPropertyChanged(nameof(IsTableModeVisibility));
            }
        }

        public string AttributeTag
        {
            get => _settings?.AttributeTag ?? "MARK";
            set
            {
                if (_settings == null) return;
                _settings.AttributeTag = value ?? "MARK";
                OnPropertyChanged();
            }
        }

        #endregion

        #region Properties - UI Index Bindings

        public int OrderIndex
        {
            get => Order == AutoNumSettings.NumberingOrder.Pick ? 0 : 1;
            set
            {
                Order = value == 0 ? AutoNumSettings.NumberingOrder.Pick : AutoNumSettings.NumberingOrder.Geometric;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSortingMode));
                OnPropertyChanged(nameof(IsSortingModeVisibility));
            }
        }

        public int ModeIndex
        {
            get => Mode switch
            {
                AutoNumSettings.NumberingMode.Text => 0,
                AutoNumSettings.NumberingMode.MLeader => 1,
                AutoNumSettings.NumberingMode.Attribute => 2,
                AutoNumSettings.NumberingMode.Table => 3,
                _ => 0
            };
            set
            {
                Mode = value switch
                {
                    0 => AutoNumSettings.NumberingMode.Text,
                    1 => AutoNumSettings.NumberingMode.MLeader,
                    2 => AutoNumSettings.NumberingMode.Attribute,
                    3 => AutoNumSettings.NumberingMode.Table,
                    _ => AutoNumSettings.NumberingMode.Text
                };
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsAttributeMode));
                OnPropertyChanged(nameof(IsAttributeModeVisibility));
            }
        }

        public int SortingIndex
        {
            get => (int)(_settings?.Sorting ?? AutoNumSettings.SortPreset.RowsTopDown);
            set
            {
                if (_settings == null) return;
                _settings.Sorting = (AutoNumSettings.SortPreset)value;
                OnPropertyChanged();
            }
        }

        #endregion

        #region Properties - Visibility

        public bool IsAttributeMode => Mode == AutoNumSettings.NumberingMode.Attribute;
        public System.Windows.Visibility IsTableModeVisibility => Mode == AutoNumSettings.NumberingMode.Table ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        public int TableRow { get => _settings.TableRow; set { _settings.TableRow = value; OnPropertyChanged(); } }
        public int TableColumn { get => _settings.TableColumn; set { _settings.TableColumn = value; OnPropertyChanged(); } }
        private string _tableRowText, _tableColumnText;
        public string TableRowText { get => _tableRowText ?? TableRow.ToString(); set => SetProperty(ref _tableRowText, value); }
        public string TableColumnText { get => _tableColumnText ?? TableColumn.ToString(); set => SetProperty(ref _tableColumnText, value); }
        public bool IsSortingMode => Order == AutoNumSettings.NumberingOrder.Geometric;

        public System.Windows.Visibility IsAttributeModeVisibility => IsAttributeMode ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        public System.Windows.Visibility IsSortingModeVisibility => IsSortingMode ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        #endregion

        #region Properties - Preview

        /// <summary>
        /// Элементы предпросмотра (5 примеров номеров)
        /// </summary>
        public List<string> PreviewItems
        {
            get
            {
                var items = new List<string>(PreviewCount);
                if (_settings == null) return items;

                long current = StartNumber;
                for (int i = 0; i < PreviewCount; i++)
                {
                    if (current < int.MinValue || current > int.MaxValue) break;
                    items.Add(_settings.FormatNumber((int)current));
                    current += Step;
                }
                return items;
            }
        }

        #endregion

        #region Commands

        public RelayCommand OkCommand { get; }
        public RelayCommand CancelCommand { get; }
        public RelayCommand RefreshAttributesCommand { get; }

        private void ExecuteOk(object parameter)
        {
            if (!int.TryParse(StartNumberText, out _) || !int.TryParse(StepText, out var step) || step == 0 ||
                !int.TryParse(DigitsText, out var digits) || digits < 0 || digits > 16 ||
                !int.TryParse(TableRowText, out var row) || row < 1 || !int.TryParse(TableColumnText, out var column) || column < 1)
            {
                System.Windows.MessageBox.Show("Введите целые числа. Шаг не равен нулю, количество знаков — от 0 до 16, строка и столбец — от 1.", "Автонумерация");
                return;
            }
            _settings.StartNumber = int.Parse(StartNumberText);
            _settings.Step = step;
            _settings.TableRow = row; _settings.TableColumn = column;
            _settings?.Save();
            DialogResult = true;
        }

        private void ExecuteCancel(object parameter)
        {
            DialogResult = false;
        }

        private void ExecuteRefreshAttributes(object parameter)
        {
            AvailableAttributes.Clear();

            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                using var tr = doc.Database.TransactionManager.StartTransaction();
                var psr = doc.Editor.SelectImplied();

                if (psr.Status != PromptStatus.OK || psr.Value.Count == 0)
                {
                    tr.Commit();
                    return;
                }

                var attributeTags = new HashSet<string>();

                foreach (SelectedObject so in psr.Value)
                {
                    if (so.ObjectId.IsErased) continue;

                    if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is BlockReference br)
                    {
                        foreach (ObjectId attId in br.AttributeCollection)
                        {
                            if (attId.IsErased) continue;

                            if (tr.GetObject(attId, OpenMode.ForRead) is AttributeReference attRef &&
                                !string.IsNullOrEmpty(attRef.Tag))
                            {
                                attributeTags.Add(attRef.Tag);
                            }
                        }
                    }
                }

                foreach (var tag in attributeTags)
                    AvailableAttributes.Add(tag);

                tr.Commit();
            }
            catch
            {
                // Игнорируем ошибки - список будет пустым
            }
        }

        #endregion
    }
}
