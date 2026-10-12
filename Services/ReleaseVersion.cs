// Copyright (c) 2026 OOXGN (GitHub: OOXGN-OOX). All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AutocadTools.Services
{
    public sealed class ReleaseVersion
    {
        public Version Version { get; }
        public string Tag { get; }
        public Uri Url { get; }
        private ReleaseVersion(Version version, string tag, Uri url) { Version = version; Tag = tag; Url = url; }

        public static ReleaseVersion FindUpdate(string json, Version installed, bool includePrerelease = false)
        {
            ReleaseVersion best = null;
            foreach (var release in JArray.Parse(json))
            {
                if (release.Value<bool>("draft") || (!includePrerelease && release.Value<bool>("prerelease"))) continue;
                string tag = release.Value<string>("tag_name");
                string number = tag?.TrimStart('v', 'V').Split('-', '+')[0];
                if (!Version.TryParse(number, out var version)) continue;
                var normalized = new Version(version.Major, version.Minor, Math.Max(0, version.Build));
                var current = new Version(installed.Major, installed.Minor, Math.Max(0, installed.Build));
                if (normalized <= current || (best != null && normalized <= best.Version)) continue;
                if (!Uri.TryCreate(release.Value<string>("html_url"), UriKind.Absolute, out var url) ||
                    url.Scheme != "https" || url.Host != "github.com" ||
                    !url.AbsolutePath.StartsWith("/OOXGN-OOX/AutocadTools/releases/", StringComparison.Ordinal)) continue;
                best = new ReleaseVersion(normalized, tag, url);
            }
            return best;
        }
    }
}
