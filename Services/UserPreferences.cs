// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.Collections.Generic;
using System.IO;
using AutocadTools.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AutocadTools.Services
{
    internal static class UserPreferences
    {
        private static readonly object Gate = new();
        private static string PathName => Path.Combine(PluginPaths.UserDataDirectory, "preferences.json");
        private static Dictionary<string, JObject> _values;
        private static bool _unsaved;
        private static Dictionary<string, JObject> Values
        {
            get
            {
                if (_values != null) return _values;
                try { _values = File.Exists(PathName) ? JsonConvert.DeserializeObject<Dictionary<string, JObject>>(File.ReadAllText(PathName)) : null; }
                catch (Exception ex) { PluginLog.Error("Read user preferences", ex); }
                return _values ??= new Dictionary<string, JObject>();
            }
        }

        internal static JObject Read(string section)
        {
            lock (Gate) return Values.TryGetValue(section, out var value) ? (JObject)value.DeepClone() : new JObject();
        }

        internal static void Write(string section, JObject value)
        {
            lock (Gate)
            {
                if (!_unsaved && Values.TryGetValue(section, out var previous) && JToken.DeepEquals(previous, value)) return;
                Values[section] = (JObject)value.DeepClone();
                _unsaved = true;
                try { AtomicFile.WriteAllText(PathName, JsonConvert.SerializeObject(Values, Formatting.Indented)); _unsaved = false; }
                catch (Exception ex) { PluginLog.Error("Save user preferences", ex); }
            }
        }
    }
}
