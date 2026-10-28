// SPDX-License-Identifier: AGPL-3.0-only
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: BotPlusAutocadTool
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Input;
using AutocadTools.Models;
using AutocadTools.Services;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.PlottingServices; // NEW: PlotFactory.ProcessPlotState
using AcadCoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

using SystemException = System.Exception;

namespace AutocadTools.ViewModels
{
    public class BatchPlotViewModel : BaseViewModel
    {
        private readonly BatchPlotService _service;
        private string _statusMessage;
        private bool _isBusy;
        private ObservableCollection<BatchPlotItem> _items;

        // NEW: галочка "В фоне" -> BACKGROUNDPLOT (3/0)
        private bool _plotInBackground = true;

        // NEW: общий чекбокс в шапке таблицы (tri-state)
        private bool? _isAllSelected = false;
        private bool _suppressIsAllSelectedRecalc;


        public event EventHandler RequestClose;

        #region Публичные свойства

        public ObservableCollection<BatchPlotItem> Items
        {
            get => _items;
            set
            {
                if (_items == value) return;

                if (_items != null)
                    foreach (var item in _items)
                        item.PropertyChanged -= OnItemPropertyChanged;

                _items = value;

                if (_items != null)
                    foreach (var item in _items)
                        item.PropertyChanged += OnItemPropertyChanged;

                OnPropertyChanged();
                RecalcIsAllSelected();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public List<string> AllFormatNames { get; private set; }

        public bool IsTopToBottom { get => _service.SearchOrder == PlotSearchOrder.TopToBottom; set { if (value) SetSearchOrder(PlotSearchOrder.TopToBottom); } }
        public bool IsBottomToTop { get => _service.SearchOrder == PlotSearchOrder.BottomToTop; set { if (value) SetSearchOrder(PlotSearchOrder.BottomToTop); } }
        public bool IsLeftToRight { get => _service.SearchOrder == PlotSearchOrder.LeftToRight; set { if (value) SetSearchOrder(PlotSearchOrder.LeftToRight); } }
        public bool IsRightToLeft { get => _service.SearchOrder == PlotSearchOrder.RightToLeft; set { if (value) SetSearchOrder(PlotSearchOrder.RightToLeft); } }

        private void SetSearchOrder(PlotSearchOrder order)
        {
            if (_service.SearchOrder == order || IsBusy) return;
            _service.SearchOrder = order;
            UserPreferences.Write("BatchPlotOrder", new Newtonsoft.Json.Linq.JObject { ["Direction"] = order.ToString() });
            // Reuse the items to keep edited titles, numbers and checkboxes.
            Items = new ObservableCollection<BatchPlotItem>(_service.SortFrames(Items));
            OnPropertyChanged(nameof(IsTopToBottom));
            OnPropertyChanged(nameof(IsBottomToTop));
            OnPropertyChanged(nameof(IsLeftToRight));
            OnPropertyChanged(nameof(IsRightToLeft));
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage == value) return;
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy == value) return;
                _isBusy = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        /// <summary>
        /// Галочка "В фоне": true -> BACKGROUNDPLOT=3, false -> BACKGROUNDPLOT=0.
        /// </summary>
        public bool PlotInBackground
        {
            get => _plotInBackground;
            set
            {
                if (_plotInBackground == value) return;
                _plotInBackground = value;
                SettingsService.Settings.PlotInBackground = value;
                SettingsService.Save();
                OnPropertyChanged();
            }
        }

        private string _outputDirectory = SettingsService.Settings.PlotOutputDirectory;
        public string OutputDirectory
        {
            get => _outputDirectory;
            set
            {
                if (!SetProperty(ref _outputDirectory, value)) return;
                SettingsService.Settings.PlotOutputDirectory = value ?? "";
                SettingsService.Save();
            }
        }
        public bool KeepExistingPdfFiles
        {
            get => SettingsService.Settings.KeepExistingPdfFiles;
            set { SettingsService.Settings.KeepExistingPdfFiles = value; SettingsService.Save(); OnPropertyChanged(); }
        }
        public int PlotScaleIndex
        {
            get => _service.PlotScale == 100 ? 1 : 0;
            set
            {
                int scale = value == 1 ? 100 : 1;
                if (_service.PlotScale == scale) return;
                _service.PlotScale = scale;
                SettingsService.Settings.PlotScale = scale;
                SettingsService.Save();
                OnPropertyChanged();
                LoadData();
            }
        }

        /// <summary>
        /// Общий чекбокс выбора в заголовке таблицы:
        /// true = выбрать всё, false = снять всё, null = частично выбрано.
        /// </summary>
        public bool? IsAllSelected
        {
            get => _isAllSelected;
            set
            {
                if (_isAllSelected == value) return;
                _isAllSelected = value;
                OnPropertyChanged();

                if (_suppressIsAllSelectedRecalc)
                    return;

                if (value == true) SelectAll();
                else if (value == false) DeselectAll();
            }
        }

        #endregion

        #region Команды (XAML)

        public ICommand PlotCommand { get; }
        public ICommand PlotBySheetsCommand { get; }
        public ICommand UpdateAttributesCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand DeselectAllCommand { get; }

        public ICommand SetFormatkaCommand { get; }
        public ICommand SelectFormatkiCommand { get; }
        public ICommand InsertIndexCommand { get; }
        public ICommand BrowseOutputCommand { get; }

        #endregion

        private readonly Action<Action> _drawingInteraction;

        public BatchPlotViewModel(Action<Action> drawingInteraction = null)
        {
            _drawingInteraction = drawingInteraction ?? (operation => operation());
            _service = new BatchPlotService();
            if (Enum.TryParse(UserPreferences.Read("BatchPlotOrder").Value<string>("Direction"), out PlotSearchOrder order)
                && Enum.IsDefined(typeof(PlotSearchOrder), order)) _service.SearchOrder = order;
            var filter = UserPreferences.Read("BatchPlotFilter");
            _service.BlockName = filter.Value<string>("Block") ?? BatchPlotService.DefaultBlockName;
            _service.LayerName = filter.Value<string>("Layer") ?? BatchPlotService.DefaultLayerName;
            _plotInBackground = SettingsService.Settings.PlotInBackground;

            AllFormatNames = _service.GetDefinedFormatNames();
            Items = new ObservableCollection<BatchPlotItem>();

            PlotCommand = new RelayCommand(_ => ExecutePublish(PluginPaths.CreateDsdPath(), "Публикация (сшивка) запущена..."), CanExecutePrint);
            PlotBySheetsCommand = new RelayCommand(_ => ExecutePublish(PluginPaths.CreateDsdPath(), "Публикация (по листам) запущена...", separateSheets: true), CanExecutePrint);

            UpdateAttributesCommand = new RelayCommand(ExecuteUpdateAttributes, _ => !IsBusy && Items != null && Items.Any());
            RefreshCommand = new RelayCommand(_ => LoadData(), _ => !IsBusy);
            SelectAllCommand = new RelayCommand(_ => SelectAll(), _ => !IsBusy && Items.Any());
            DeselectAllCommand = new RelayCommand(_ => DeselectAll(), _ => !IsBusy && Items.Any());

            SetFormatkaCommand = new RelayCommand(_ => _drawingInteraction(ExecuteSetFormatka), _ => !IsBusy);

            SelectFormatkiCommand = new RelayCommand(_ => _drawingInteraction(ExecuteSelectFormatki), _ => !IsBusy && Items.Any());
            InsertIndexCommand = new RelayCommand(_ => _drawingInteraction(ExecuteInsertIndex), _ => !IsBusy && Items.Any(i => i.IsSelected));
            BrowseOutputCommand = new RelayCommand(_ =>
            {
                string folder = UI.FolderPicker.Choose(OutputDirectory);
                if (folder != null) OutputDirectory = folder;
            }, _ => !IsBusy);

            LoadData();
        }

        #region Загрузка

        private void ExecuteInsertIndex()
        {
            var doc = AcadCoreApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var selected = Items.Where(i => i.IsSelected).ToArray();
            if (selected.Length == 0) return;
            try
            {
                using (doc.LockDocument())
                    if (!doc.Database.TileMode) LayoutManager.Current.CurrentLayout = "Model";
                var point = doc.Editor.GetPoint("\nУкажите точку вставки ведомости в модели: ");
                if (point.Status != PromptStatus.OK) return;
                using (doc.LockDocument()) PlotIndexService.Insert(doc.Database, selected,
                    point.Value.TransformBy(doc.Editor.CurrentUserCoordinateSystem), _service.PlotScale);
                doc.Editor.Regen();
                StatusMessage = $"В модель вставлена таблица: {selected.Length} листов.";
            }
            catch (SystemException ex)
            {
                Infrastructure.PluginLog.Error("Insert drawing index", ex);
                StatusMessage = "Не удалось вставить таблицу: " + ex.Message;
                doc.Editor.WriteMessage("\n[Autocad Tools] " + StatusMessage);
            }
        }

        private void LoadData()
        {
            try
            {
                IsBusy = true;
                StatusMessage = $"Загрузка блоков... (блок='{_service.BlockName}', слой='{_service.LayerName}')";

                var blocks = _service.GetFormatBlocks();
                var selection = UserPreferences.Read(SelectionSection);
                foreach (var block in blocks)
                    if (selection.TryGetValue(block.BlockId.Handle.ToString(), out var flag) && flag.Type == Newtonsoft.Json.Linq.JTokenType.Boolean)
                        block.IsSelected = flag.ToObject<bool>();

                Items = new ObservableCollection<BatchPlotItem>(blocks);

                if (blocks.Count > 0)
                {
                    int withFormat = blocks.Count(b => !string.IsNullOrWhiteSpace(b.Format) && b.Format != "Нестандартный");
                    StatusMessage = $"Найдено форматов: {blocks.Count} (стандартных: {withFormat}). Фильтр: блок='{_service.BlockName}', слой='{_service.LayerName}'";
                }
                else
                {
                    StatusMessage = $"Форматы не найдены. Фильтр: блок='{_service.BlockName}', слой='{_service.LayerName}'";
                }

                RecalcIsAllSelected();
            }
            catch (SystemException ex)
            {
                StatusMessage = $"Ошибка загрузки: {ex.Message}";
                MessageBox.Show($"Не удалось загрузить блоки:\n\n{ex.Message}", "Autocad Tools — ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OnItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BatchPlotItem.IsSelected))
            {
                if (!_suppressIsAllSelectedRecalc && sender is BatchPlotItem changed && !changed.BlockId.IsNull)
                {
                    var selection = UserPreferences.Read(SelectionSection);
                    selection[changed.BlockId.Handle.ToString()] = changed.IsSelected;
                    UserPreferences.Write(SelectionSection, selection);
                }
                RecalcIsAllSelected();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private string SelectionSection => "BatchPlotSelection:" + (AcadCoreApp.DocumentManager.MdiActiveDocument?.Name ?? "");

        private void SaveSelection()
        {
            var selection = UserPreferences.Read(SelectionSection);
            foreach (var item in Items.Where(i => !i.BlockId.IsNull)) selection[item.BlockId.Handle.ToString()] = item.IsSelected;
            UserPreferences.Write(SelectionSection, selection);
        }

        private void RecalcIsAllSelected()
        {
            if (_suppressIsAllSelectedRecalc) return;

            _suppressIsAllSelectedRecalc = true;
            try
            {
                if (Items == null || Items.Count == 0)
                {
                    _isAllSelected = false;
                    OnPropertyChanged(nameof(IsAllSelected));
                    return;
                }

                int selectedCount = Items.Count(i => i.IsSelected);
                bool? newVal = selectedCount == 0 ? false
                               : selectedCount == Items.Count ? true
                               : (bool?)null;

                if (_isAllSelected != newVal)
                {
                    _isAllSelected = newVal;
                    OnPropertyChanged(nameof(IsAllSelected));
                }
            }
            finally
            {
                _suppressIsAllSelectedRecalc = false;
            }
        }

        #endregion

        #region Указать форматку

        private void ExecuteSelectFormatki()
        {
            var doc = AcadCoreApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var selection = doc.Editor.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nВыберите форматки для печати: " },
                new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT") }));
            if (selection.Status != PromptStatus.OK) return;
            using (selection.Value)
            {
                var ids = new HashSet<ObjectId>(selection.Value.GetObjectIds());
                foreach (var item in Items) item.IsSelected = ids.Contains(item.BlockId);
            }
            RecalcIsAllSelected();
        }

        protected override void OnDisposing()
        {
            foreach (var item in Items) item.PropertyChanged -= OnItemPropertyChanged;
            RequestClose = null;
        }

        private void ExecuteSetFormatka()
        {
            try
            {
                var doc = AcadCoreApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                var ed = doc.Editor;
                var db = doc.Database;

                var peo = new PromptEntityOptions("\nВыберите блок-форматку (вставку блока): ");
                peo.SetRejectMessage("\nНужно выбрать вставку блока (BlockReference).");
                peo.AddAllowedClass(typeof(BlockReference), exactMatch: true);

                PromptEntityResult per = ed.GetEntity(peo);

                if (per.Status != PromptStatus.OK)
                    return;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var br = (BlockReference)tr.GetObject(per.ObjectId, OpenMode.ForRead);

                    string newLayer = br.Layer ?? "";
                    string newBlockName = GetEffectiveName(br, tr) ?? "";

                    tr.Commit();

                    if (string.IsNullOrWhiteSpace(newLayer) || string.IsNullOrWhiteSpace(newBlockName))
                    {
                        MessageBox.Show("Не удалось определить имя блока или слой.", "Autocad Tools",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    _service.LayerName = newLayer;
                    _service.BlockName = newBlockName;
                    UserPreferences.Write("BatchPlotFilter", new Newtonsoft.Json.Linq.JObject { ["Block"] = newBlockName, ["Layer"] = newLayer });
                }

                StatusMessage = $"Фильтр изменён: блок='{_service.BlockName}', слой='{_service.LayerName}'";
                LoadData();
            }
            catch (SystemException ex)
            {
                StatusMessage = "Ошибка выбора форматки";
                MessageBox.Show($"Ошибка выбора форматки:\n\n{ex.Message}", "Autocad Tools — ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string GetEffectiveName(BlockReference blk, Transaction tr)
        {
            if (blk.IsDynamicBlock)
            {
                var btr = tr.GetObject(blk.DynamicBlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                return btr?.Name ?? blk.Name;
            }
            return blk.Name;
        }

        #endregion

        #region Обновление атрибутов

        private void ExecuteUpdateAttributes(object obj)
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Обновление атрибутов...";

                _service.UpdateBlockAttributes(Items.ToList());

                LoadData();
                StatusMessage = "Атрибуты обновлены успешно";
            }
            catch (SystemException ex)
            {
                StatusMessage = "Ошибка обновления атрибутов";
                MessageBox.Show($"Ошибка обновления атрибутов:\n\n{ex.Message}", "Autocad Tools — ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region Печать (DSD + PUBLISH)

        private bool CanExecutePrint(object obj)
            => !IsBusy && !DsdPublishService.IsBusy && Items != null && Items.Any(i => i.IsSelected);

        private void ExecutePublish(string dsdPath, string statusText, bool separateSheets = false)
        {
            var selected = Items.Where(i => i.IsSelected).ToList();
            if (!selected.Any())
            {
                MessageBox.Show("Не выбрано ни одного листа", "Autocad Tools",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Валидация перед стартом
            var invalid = new List<BatchPlotItem>();
            foreach (var item in selected)
            {
                if (!item.IsValid(out string errorMsg))
                {
                    item.Status = errorMsg ?? "Ошибка валидации";
                    invalid.Add(item);
                }
            }

            if (invalid.Any())
            {
                var msg = "Обнаружены невалидные элементы:\n\n" +
                          string.Join("\n", invalid.Select(i => $"- Лист {i.SheetNumber}: {i.Status}")) +
                          "\n\nЗапустить публикацию всё равно?";

                var result = MessageBox.Show(msg, "Autocad Tools — предупреждение",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                    return;
            }

            // NEW: блокируем повторный старт, если уже идёт plot/publish (foreground или background)
            if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
            {
                StatusMessage = "Уже выполняется печать/публикация. Дождитесь завершения текущей задачи.";
                MessageBox.Show("Сейчас уже выполняется печать/публикация (в фоне или в текущем процессе).\n\n" +
                                "Дождитесь завершения/отмените текущую задачу и повторите.",
                                "Autocad Tools", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                IsBusy = true;
                if (MessageBox.Show("Для публикации текущий DWG будет сохранён. Продолжить?", "AutocadTools",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                StatusMessage = "Подготовка листов (PageSetup/DSD)...";

                var doc = AcadCoreApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                string drawingPath = doc.Database.Filename;
                string outputDirectory = Path.GetDirectoryName(drawingPath);
                if (string.IsNullOrEmpty(outputDirectory) || !File.Exists(drawingPath))
                    throw new InvalidOperationException("Сохраните DWG перед публикацией.");
                if (!string.IsNullOrWhiteSpace(OutputDirectory))
                {
                    if (!Path.IsPathFullyQualified(OutputDirectory)) throw new InvalidOperationException("Укажите полный путь к папке PDF.");
                    outputDirectory = OutputDirectory;
                }
                Directory.CreateDirectory(outputDirectory);
                string drawingName = Path.GetFileNameWithoutExtension(drawingPath);
                string outputPath = Path.Combine(outputDirectory, drawingName + (separateSheets ? "_Sheets" : "_Binder.pdf"));
                string jobDirectory = Path.Combine(PluginPaths.UserDataDirectory, "Publish", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(jobDirectory);
                string pdfPath = Path.Combine(jobDirectory, "Result.pdf");

                // 1) Генерируем DSD и PageSetup'ы с учетом выбранных листов
                bool dsdReady = BlockExportStage1Service.Run(doc, dsdPath, selected, pdfPath);

                if (!dsdReady)
                {
                    StatusMessage = "Ошибка подготовки DSD";
                    MessageBox.Show("Не удалось создать настройки печати (проверь доступность пути DSD и наличие выбранных блоков).",
                        "Autocad Tools — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                StatusMessage = statusText;

                // 2) BACKGROUNDPLOT строго по ТЗ: 3 если "в фоне", иначе 0

                // 3) Запускаем -PUBLISH
                bool ok = DsdPublishService.Publish(dsdPath, pdfPath, outputPath, selected, separateSheets, PlotInBackground,
                    SettingsService.Settings.OpenFolderAfterPlot, SettingsService.Settings.OpenFileAfterPlot, SettingsService.Settings.KeepExistingPdfFiles);

                if (!ok)
                {
                    StatusMessage = "Не удалось запустить публикацию DSD";
                    MessageBox.Show("Команда публикации не стартовала.",
                        "Autocad Tools — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                foreach (var item in selected)
                    item.Status = "Публикация...";

                // Закрываем окно, чтобы AutoCAD мог выполнить команду -PUBLISH
                RequestClose?.Invoke(this, EventArgs.Empty);
            }
            catch (SystemException ex)
            {
                StatusMessage = "Ошибка запуска публикации";
                MessageBox.Show($"Критическая ошибка:\n\n{ex.Message}", "Autocad Tools — ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region Выбор

        private void SelectAll()
        {
            if (Items == null) return;

            _suppressIsAllSelectedRecalc = true;
            try
            {
                foreach (var item in Items)
                    item.IsSelected = true;
            }
            finally
            {
                _suppressIsAllSelectedRecalc = false;
                RecalcIsAllSelected();
            }

            StatusMessage = $"Выбрано: {Items.Count} элементов";
            SaveSelection();
        }

        private void DeselectAll()
        {
            if (Items == null) return;

            _suppressIsAllSelectedRecalc = true;
            try
            {
                foreach (var item in Items)
                    item.IsSelected = false;
            }
            finally
            {
                _suppressIsAllSelectedRecalc = false;
                RecalcIsAllSelected();
            }

            StatusMessage = "Выбор снят";
            SaveSelection();
        }

        #endregion
    }
}
