// SPDX-License-Identifier: AGPL-3.0-only
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: BotPlusAutocadTool
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using AutocadTools.Infrastructure;

namespace AutocadTools.Services
{
    /// <summary>
    /// Сервис для сохранения настроек плагина
    /// </summary>
    public static class SettingsService
    {
        private static readonly string SettingsPath;
        private static PluginSettings _settings = new PluginSettings();

        static SettingsService()
        {
            // Directory creation and serializer loading must not poison this type.
            try
            {
                SettingsPath = Path.Combine(PluginPaths.UserDataDirectory, "settings.json");
                Load();
            }
            catch (Exception ex) { PluginLog.Error("Initialize settings", ex); }
        }

        public static PluginSettings Settings => _settings;

        private static void Load()
        {
            try
            {
                var legacyPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "wm_settings.json");
                var loadPath = File.Exists(SettingsPath) ? SettingsPath : legacyPath;
                if (File.Exists(loadPath))
                {
                    var json = File.ReadAllText(loadPath);
                    _settings = JsonConvert.DeserializeObject<PluginSettings>(json) ?? new PluginSettings();
                    _settings.Normalize();
                }
                else
                {
                    _settings = new PluginSettings();
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error("Read settings", ex);
                _settings = new PluginSettings();
            }
        }

        public static void Save()
            => TrySave();

        public static bool TrySave()
        {
            try
            {
                if (SettingsPath == null) return false;
                SaveCore();
                return true;
            }
            catch (Exception ex) { PluginLog.Error("Save settings", ex); return false; }
        }

        private static void SaveCore()
        {
            _settings.Normalize();
            var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
            AtomicFile.WriteAllText(SettingsPath, json);
        }
    }

    public class PluginSettings
    {
        public bool AutoCheckUpdates { get; set; } = true;
        public bool AutoInstallUpdates { get; set; } = true;
        public bool IncludePrereleaseUpdates { get; set; } = false;
        public DateTime LastUpdateCheckUtc { get; set; }
        public string LastNotifiedUpdateTag { get; set; }
        public string PendingUpdateDirectory { get; set; }
        // SumLength settings
        public int ScaleNumerator { get; set; } = 1;
        public int ScaleDenominator { get; set; } = 1;
        public int LengthUnit { get; set; } = 0; // 0=mm, 1=m, 2=km
        public int AreaUnit { get; set; } = 0;   // 0=mm², 1=m², 2=km²
        public double FrameInsertionScale { get; set; } = 1;
        public int PlotScale { get; set; } = 1;
        public bool PlotInBackground { get; set; } = true;
        public bool OpenFolderAfterPlot { get; set; }
        public bool OpenFileAfterPlot { get; set; }
        public bool KeepExistingPdfFiles { get; set; }
        public string PlotOutputDirectory { get; set; } = "";

        internal void Normalize()
        {
            ScaleNumerator = Math.Max(1, ScaleNumerator);
            ScaleDenominator = Math.Max(1, ScaleDenominator);
            if (LengthUnit < 0 || LengthUnit > 2) LengthUnit = 0;
            if (AreaUnit < 0 || AreaUnit > 2) AreaUnit = 0;
            if (!double.IsFinite(FrameInsertionScale) || FrameInsertionScale <= 0 || FrameInsertionScale > 1000000) FrameInsertionScale = 1;
            if (PlotScale != 1 && PlotScale != 100) PlotScale = 1;
            PlotOutputDirectory ??= "";
        }
    }
}
