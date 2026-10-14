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
        public ReleaseAsset Installer { get; }
        public ReleaseAsset Checksums { get; }
        public bool CanInstall => Installer != null && Checksums != null;
        private ReleaseVersion(Version version, string tag, Uri url, ReleaseAsset installer, ReleaseAsset checksums)
        { Version = version; Tag = tag; Url = url; Installer = installer; Checksums = checksums; }

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
                string installerName = $"AutocadTools-{normalized.Major}.{normalized.Minor}.{normalized.Build}-Setup.exe";
                var assets = release["assets"] as JArray;
                best = new ReleaseVersion(normalized, tag, url,
                    ReleaseAsset.Find(assets, tag, installerName), ReleaseAsset.Find(assets, tag, "SHA256SUMS.txt"));
            }
            return best;
        }
    }

    public sealed class ReleaseAsset
    {
        public string Name { get; }
        public Uri Url { get; }
        public long Size { get; }
        public string Sha256 { get; }
        private ReleaseAsset(string name, Uri url, long size, string sha256)
        { Name = name; Url = url; Size = size; Sha256 = sha256; }

        internal static ReleaseAsset Find(JArray assets, string tag, string name)
        {
            if (assets == null) return null;
            var matches = assets.Where(a => a.Value<string>("name") == name && a.Value<string>("state") == "uploaded").ToArray();
            if (matches.Length != 1) return null;
            var asset = matches[0];
            if (!Uri.TryCreate(asset.Value<string>("browser_download_url"), UriKind.Absolute, out var url)) return null;
            string expected = "https://github.com/OOXGN-OOX/AutocadTools/releases/download/"
                + Uri.EscapeDataString(tag) + "/" + Uri.EscapeDataString(name);
            if (url.AbsoluteUri != expected) return null;
            long size = asset.Value<long?>("size") ?? 0;
            if (size <= 0) return null;
            string digest = asset.Value<string>("digest");
            string sha256 = digest != null && System.Text.RegularExpressions.Regex.IsMatch(digest, "^sha256:[0-9a-fA-F]{64}$")
                ? digest.Substring(7).ToLowerInvariant() : null;
            return new ReleaseAsset(name, url, size, sha256);
        }
    }
}
