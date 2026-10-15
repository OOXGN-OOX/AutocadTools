// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

// File: AutocadTools/App.cs

using System;
using System.Linq;
using System.Windows.Input;

using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using Autodesk.AutoCAD.Windows;

using WpfOrientation = System.Windows.Controls.Orientation;

// Псевдоним для Application
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(AutocadTools.App))]

namespace AutocadTools
{
    public class App : IExtensionApplication
    {
        private const string RibbonTabId = "AUTOCAD_TOOLS_TAB";
        private const string RibbonTabTitle = "Autocad Tools";

        private static ContextMenuExtension _contextMenu;
        private DateTime _nextUiCheckUtc;
        private bool? _darkRibbon;
        private bool? _highContrastRibbon;

        public void Initialize()
        {
            try { Services.StampSignatureTracking.Start(); } catch (System.Exception ex) { LogStartupError(ex); }
            try { Services.SelectionLengthService.Start(); } catch (System.Exception ex) { LogStartupError(ex); }
            try { Services.UpdateCheckService.Start(); } catch (System.Exception ex) { LogStartupError(ex); }
            AcadApp.Idle += Application_Idle;
        }

        public void Terminate()
        {
            Services.StampSignatureTracking.Stop();
            Services.UpdateCheckService.Stop();
            Services.SelectionLengthService.Stop();
            Services.DsdPublishService.Shutdown();
            AcadApp.Idle -= Application_Idle;
            if (_contextMenu != null)
            {
                AcadApp.RemoveDefaultContextMenuExtension(_contextMenu);
                _contextMenu = null;
            }
        }

        private void Application_Idle(object sender, EventArgs e)
        {
            if (DateTime.UtcNow < _nextUiCheckUtc) return;
            _nextUiCheckUtc = DateTime.UtcNow.AddSeconds(1);
            // Keep checking: Ribbon may not exist on the first Idle, and workspaces can rebuild it.
            CreateRibbonSafe();
            if (_contextMenu == null) CreateContextMenuSafe();
        }

        private static void LogStartupError(System.Exception ex)
        {
            AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[Autocad Tools] Ошибка дополнительного сервиса: " + ex.Message + "\n");
        }

        private void CreateRibbonSafe()
        {
            try
            {
                CreateRibbon();
            }
            catch (System.Exception ex)
            {
                AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                    $"\n[Autocad Tools Error] Ошибка создания ленты: {ex.Message}\n");
            }
        }

        private void CreateContextMenuSafe()
        {
            try
            {
                CreateContextMenu();
            }
            catch (System.Exception ex)
            {
                AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                    $"\n[Autocad Tools Error] Ошибка создания контекстного меню: {ex.Message}\n");
            }
        }

        private void CreateContextMenu()
        {
            _contextMenu = new ContextMenuExtension();
            _contextMenu.Title = "Autocad Tools";

            var menuItem = new MenuItem("Autocad Tools: выбрать по образцу");
            menuItem.Click += OnSelectSimilarClick;
            _contextMenu.MenuItems.Add(menuItem);

            AcadApp.AddDefaultContextMenuExtension(_contextMenu);
        }

        private void OnSelectSimilarClick(object sender, EventArgs e)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc != null)
                doc.SendStringToExecute("WSELECT ", true, false, false);
        }

        private void CreateRibbon()
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            var tab = ribbon.FindTab(RibbonTabId);
            if (tab == null)
            {
                tab = new RibbonTab { Id = RibbonTabId, Name = RibbonTabTitle, Title = RibbonTabTitle };
                ribbon.Tabs.Add(tab);
            }
            tab.Name = RibbonTabTitle;
            tab.Title = RibbonTabTitle;
            bool dark = System.Convert.ToInt32(AcadApp.GetSystemVariable("COLORTHEME")) == 0;
            bool highContrast = System.Windows.SystemParameters.HighContrast;
            if (tab.Panels.Count > 0)
            {
                if (_darkRibbon != dark || _highContrastRibbon != highContrast)
                {
                    foreach (var button in tab.Panels.SelectMany(panel => panel.Source.Items).OfType<RibbonButton>())
                        if (button.Tag is string command) button.LargeImage = Views.ToolIcons.Create(command, dark);
                    _darkRibbon = dark;
                    _highContrastRibbon = highContrast;
                }
                return;
            }
            _darkRibbon = dark;
            _highContrastRibbon = highContrast;
            tab.IsActive = true;

            // =========================
            // ПАНЕЛЬ: ИНСТРУМЕНТЫ
            // =========================
            var toolsPanelSource = new RibbonPanelSource { Title = "Инструменты" };
            var toolsPanel = new RibbonPanel { Source = toolsPanelSource };
            tab.Panels.Add(toolsPanel);

            AddButton(toolsPanelSource, "Преобразовать\nв аксонометрию", "Align_Den");
            AddButton(toolsPanelSource, "Автонумерация", "AUTONUM");
            AddButton(toolsPanelSource, "Выбор по\nобразцу", "WSELECT");
            AddButton(toolsPanelSource, "Сумма\nдлин", "SUMLENGTH");
            AddButton(toolsPanelSource, "Мульти\nкопирование", "MULTICOPY");
            AddButton(toolsPanelSource, "Подготовка\nпланов", "PLANPREP");
            AddButton(toolsPanelSource, "Таблица\nExcel", "AUTOCADTOOLS_TABLEEXCEL");


            // =========================
            // ПАНЕЛЬ: ОЧИСТКА И СБРОС
            // =========================
            var cleanPanelSource = new RibbonPanelSource { Title = "Очистка и сброс" };
            var cleanPanel = new RibbonPanel { Source = cleanPanelSource };
            tab.Panels.Add(cleanPanel);

            AddButton(cleanPanelSource, "Очистка\nчертежа", "CLEANDWG");
            AddButton(cleanPanelSource, "Очистить\nневыбранные", "CLEANUNSELECTED");
            AddButton(cleanPanelSource, "Сброс\nтекста", "STRIPTEXT");

            // =========================
            // ПАНЕЛЬ: ВЫПУСК
            // =========================
            var releasePanelSource = new RibbonPanelSource { Title = "Выпуск" };
            var releasePanel = new RibbonPanel { Source = releasePanelSource };
            tab.Panels.Add(releasePanel);

            AddButton(releasePanelSource, "Штамп", "AUTOCADTOOLS_STAMP");
            AddButton(releasePanelSource, "Вставить\nформатку", "AUTOCADTOOLS_INSERTFORMAT");
            AddButton(releasePanelSource, "Печать", "AUTOCADTOOLS_BATCHPLOT");
            AddButton(releasePanelSource, "Подготовить\nвсе блоки", "AUTOCADTOOLS_BLOCKS_STAGE1");
            AddButton(releasePanelSource, "Сформировать\nзаказчику", "AUTOCADTOOLS_CUSTOMERORDER");

            // =========================
            // ПАНЕЛЬ: СПРАВКА
            // =========================
            var helpPanelSource = new RibbonPanelSource { Title = "Справка" };
            var helpPanel = new RibbonPanel { Source = helpPanelSource };
            tab.Panels.Add(helpPanel);

            AddButton(helpPanelSource, "О программе", "AUTOCADTOOLS_INFO");
            AddButton(helpPanelSource, "Проверить\nобновления", "AUTOCADTOOLS_CHECKUPDATES");
        }

        private void AddButton(RibbonPanelSource source, string text, string commandName)
        {
            var button = new RibbonButton
            {
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
                Orientation = WpfOrientation.Vertical,
                LargeImage = Views.ToolIcons.Create(commandName, _darkRibbon ?? true),
                Tag = commandName,
                CommandHandler = new RibbonCommand(commandName)
            };
            source.Items.Add(button);
        }


    }

    public class RibbonCommand : ICommand
    {
        private readonly string _commandName;
        public RibbonCommand(string commandName) => _commandName = commandName;

        public event EventHandler CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc != null && !string.IsNullOrWhiteSpace(_commandName))
                doc.SendStringToExecute($"{_commandName} ", true, false, false);
        }
    }
}
