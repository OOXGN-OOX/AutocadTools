// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
// ============================================================================
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: AutocadTools
// ============================================================================

using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

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
            catch { }
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
                }
                else
                {
                    _settings = new PluginSettings();
                }
            }
            catch
            {
                _settings = new PluginSettings();
            }
        }

        public static void Save()
        {
            try
            {
                if (SettingsPath != null) SaveCore();
            }
            catch { }
        }

        private static void SaveCore()
        {
            var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
            File.WriteAllText(SettingsPath, json);
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
    }
}
