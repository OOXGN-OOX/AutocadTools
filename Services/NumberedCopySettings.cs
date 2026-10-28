// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using AutocadTools.Models;
using System.Globalization;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AutocadTools.Services
{
    internal static class NumberedCopySettings
    {
        private const string Key = @"Software\OOXGN\AutocadTools\NumberedCopy";
        public static NumberedCopyOptions Load()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(Key))
                {
                    var options = JsonConvert.DeserializeObject<NumberedCopyOptions>(key?.GetValue("Options") as string ?? "null");
                    options?.Validate(); return options ?? new NumberedCopyOptions();
                }
            }
            catch { return new NumberedCopyOptions(); }
        }
        public static void Save(NumberedCopyOptions options)
        {
            try { using (var key = Registry.CurrentUser.CreateSubKey(Key)) key?.SetValue("Options", Serialize(options)); }
            catch { /* A read-only user profile does not prevent copying. */ }
        }
        internal static string Serialize(NumberedCopyOptions options)
        {
            var json = JObject.FromObject(options);
            // JSON decimal serialization adds .0 to integers; preserve the user-entered precision.
            json["Start"] = options.Start.ToString(CultureInfo.InvariantCulture);
            json["Step"] = options.Step.ToString(CultureInfo.InvariantCulture);
            return JsonConvert.SerializeObject(json, Formatting.None);
        }
    }
}
