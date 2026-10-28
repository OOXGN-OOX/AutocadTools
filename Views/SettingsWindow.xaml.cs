// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutocadTools.Services;
using Newtonsoft.Json;

namespace AutocadTools.Views
{
    public partial class SettingsWindow : Window
    {
        public sealed class EditableSettings : PluginSettings
        {
            public int PlotScaleIndex { get => PlotScale == 100 ? 1 : 0; set => PlotScale = value == 1 ? 100 : 1; }
        }
        public SettingsWindow()
        {
            InitializeComponent();
            UiTheme.Apply(this);
            DataContext = JsonConvert.DeserializeObject<EditableSettings>(JsonConvert.SerializeObject(SettingsService.Settings));
            AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent, new RoutedEventHandler(SaveCheckbox), true);
            AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent, new RoutedEventHandler(SaveCheckbox), true);
        }
        private void SaveCheckbox(object sender, RoutedEventArgs args)
        {
            if (args.OriginalSource is not CheckBox checkbox) return;
            string path = checkbox.GetBindingExpression(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty)?.ParentBinding.Path?.Path;
            if (path == null) return;
            var property = typeof(PluginSettings).GetProperty(path);
            if (property?.PropertyType != typeof(bool)) return;
            bool value = checkbox.IsChecked == true;
            if ((bool)property.GetValue(SettingsService.Settings) == value) return;
            property.SetValue(SettingsService.Settings, value);
            SettingsService.Save();
        }
        private void ResetClick(object sender, RoutedEventArgs e) => DataContext = new EditableSettings();
        private void BrowseOutputClick(object sender, RoutedEventArgs e)
        {
            var edited = (EditableSettings)DataContext;
            string folder = UI.FolderPicker.Choose(edited.PlotOutputDirectory, this);
            if (folder != null)
            {
                edited.PlotOutputDirectory = folder;
                OutputFolderBox.SetCurrentValue(TextBox.TextProperty, folder);
                OutputFolderBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            }
        }
        private static bool HasErrors(DependencyObject parent)
        {
            if (Validation.GetHasError(parent)) return true;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                if (HasErrors(VisualTreeHelper.GetChild(parent, i))) return true;
            return false;
        }
        private void SaveClick(object sender, RoutedEventArgs e)
        {
            var edited = (EditableSettings)DataContext;
            if (HasErrors(this) || !double.IsFinite(edited.FrameInsertionScale) || edited.FrameInsertionScale <= 0 || edited.FrameInsertionScale > 1000000 || edited.ScaleNumerator < 1 || edited.ScaleDenominator < 1)
            {
                MessageBox.Show(this, "Проверьте числа. Масштабы должны быть положительными; масштаб вставки — не больше 1000000.", Title); return;
            }
            if (!string.IsNullOrWhiteSpace(edited.PlotOutputDirectory) && !Path.IsPathFullyQualified(edited.PlotOutputDirectory))
            {
                MessageBox.Show(this, "Укажите полный путь к папке PDF или оставьте поле пустым.", Title); return;
            }
            var settings = SettingsService.Settings;
            settings.FrameInsertionScale = edited.FrameInsertionScale;
            settings.PlotScale = edited.PlotScale;
            settings.PlotOutputDirectory = edited.PlotOutputDirectory?.Trim() ?? "";
            settings.PlotInBackground = edited.PlotInBackground;
            settings.KeepExistingPdfFiles = edited.KeepExistingPdfFiles;
            settings.OpenFolderAfterPlot = edited.OpenFolderAfterPlot;
            settings.OpenFileAfterPlot = edited.OpenFileAfterPlot;
            settings.ScaleNumerator = edited.ScaleNumerator; settings.ScaleDenominator = edited.ScaleDenominator;
            settings.LengthUnit = edited.LengthUnit; settings.AreaUnit = edited.AreaUnit;
            settings.AutoInstallUpdates = edited.AutoInstallUpdates;
            settings.AutoCheckUpdates = edited.AutoCheckUpdates;
            UpdateCheckService.ApplySettings(); settings.IncludePrereleaseUpdates = edited.IncludePrereleaseUpdates;
            if (!SettingsService.TrySave())
            {
                MessageBox.Show(this, "Не удалось сохранить настройки. Проверьте доступ к папке данных пользователя; подробности записаны в журнал.", Title); return;
            }
            DialogResult = true;
        }
    }
}
