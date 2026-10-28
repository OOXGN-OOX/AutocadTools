// SPDX-License-Identifier: AGPL-3.0-only
// OOXGN - Copyright (c) 2026. All rights reserved.
// Author: OOXGN | Project: BotPlusAutocadTool
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System.Collections.Generic;

namespace AutocadTools.Models
{
    /// <summary>
    /// Container for all stamp properties
    /// </summary>
    public class StampProperties
    {
        public Dictionary<string, string> Properties { get; set; }

        public StampProperties()
        {
            Properties = new Dictionary<string, string>();
        }

        public string GetValue(string key)
        {
            return Properties.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value) ? value : " ";
        }

        public void SetValue(string key, string value)
        {
            if (Properties.ContainsKey(key))
                Properties[key] = value;
            else
                Properties.Add(key, value);
        }
    }
}

