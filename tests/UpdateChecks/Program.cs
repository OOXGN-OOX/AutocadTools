// Copyright (c) 2026 OOXGN (GitHub: OOXGN-OOX). All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
using AutocadTools.Services;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Reflection;

if (args.Length > 0)
{
    TestSettings(args[0]);
    return;
}
foreach (var scenario in new[] { "valid", "malformed", "unavailable" })
{
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
    if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add(scenario);
    using var child = Process.Start(start)!;
    child.WaitForExit();
    if (child.ExitCode != 0) throw new Exception("Settings regression: " + scenario);
}
if (Assembly.GetExecutingAssembly().GetReferencedAssemblies().Any(a => a.Name == "System.Text.Json"))
    throw new Exception("Startup services still reference System.Text.Json");

var releases = JsonConvert.SerializeObject(new[] {
    new {tag_name="v1.1.0",draft=false,prerelease=false,html_url="https://github.com/OOXGN-OOX/AutocadTools/releases/tag/v1.1.0"},
    new {tag_name="v1.2.0",draft=false,prerelease=false,html_url="https://github.com/OOXGN-OOX/AutocadTools/releases/tag/v1.2.0"},
    new {tag_name="v2.0.0-beta.1",draft=false,prerelease=true,html_url="https://github.com/OOXGN-OOX/AutocadTools/releases/tag/v2.0.0-beta.1"},
    new {tag_name="v9.0.0",draft=true,prerelease=false,html_url="https://github.com/OOXGN-OOX/AutocadTools/releases/tag/v9.0.0"},
    new {tag_name="v8.0.0",draft=false,prerelease=false,html_url="https://evil.invalid/download"},
    new {tag_name="v7.0.0",draft=false,prerelease=false,html_url="https://github.com/other/repo/releases/tag/v7.0.0"},
    new {tag_name="not-a-version",draft=false,prerelease=false,html_url="https://github.com/OOXGN-OOX/AutocadTools/releases/tag/foo"}
});
if (ReleaseVersion.FindUpdate(releases, new Version(1,1,0)).Tag != "v1.2.0") throw new Exception("Stable update selection failed");
if (ReleaseVersion.FindUpdate(releases, new Version(1,1,0), true).Tag != "v2.0.0-beta.1") throw new Exception("Optional prerelease failed");
if (ReleaseVersion.FindUpdate(releases, new Version(3,0,0)) != null) throw new Exception("Downgrade/unsafe URL accepted");
if (ReleaseVersion.FindUpdate("[]", new Version(1,1,0)) != null) throw new Exception("Empty release list failed");
Console.WriteLine("PASS: versions, drafts, prereleases, no downgrade, safe repository URLs, malformed tags, empty list.");
var now = new DateTime(2026,10,12,12,0,0,DateTimeKind.Utc);
var schedule = new UpdateSchedule();
if (!schedule.IsDue(now)) throw new Exception("Startup check missing");
schedule.Checked(now);
if (schedule.IsDue(now.AddHours(23.99))) throw new Exception("Repeated before 24 hours");
if (!schedule.IsDue(now.AddHours(24))) throw new Exception("Daily check missing");
schedule.NewSession();
if (!schedule.IsDue(now.AddMinutes(1))) throw new Exception("New AutoCAD session skipped check");
Console.WriteLine("PASS: each startup and every 24 hours while open.");

SettingsService.Settings.AutoCheckUpdates = false;
UpdateCheckService.Start();
UpdateCheckService.Start();
if (Autodesk.AutoCAD.ApplicationServices.Application.SubscriberCount != 1) throw new Exception("Duplicate Idle subscription");
typeof(UpdateCheckService).GetField("_pending", BindingFlags.NonPublic | BindingFlags.Static)!
    .SetValue(null, Task.FromException<ReleaseVersion>(new HttpRequestException("Simulated offline GitHub")));
Autodesk.AutoCAD.ApplicationServices.Application.RaiseIdle();
if (Autodesk.AutoCAD.ApplicationServices.Application.SubscriberCount != 1) throw new Exception("Network error stopped daily checks");
typeof(UpdateCheckService).GetField("_pending", BindingFlags.NonPublic | BindingFlags.Static)!
    .SetValue(null, Task.FromResult<ReleaseVersion>(null!));
Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.ThrowOnRead = true;
Autodesk.AutoCAD.ApplicationServices.Application.RaiseIdle();
Autodesk.AutoCAD.ApplicationServices.Application.RaiseIdle();
if (Autodesk.AutoCAD.ApplicationServices.Application.SubscriberCount != 0) throw new Exception("Failed Idle handler remained attached");
if (System.Windows.MessageBox.Shown != 0) throw new Exception("Background failure opened a modal dialog");
Console.WriteLine("PASS: offline GitHub is contained; unexpected Idle failure unsubscribes without JIT dialogs.");

static void TestSettings(string scenario)
{
    var root = PluginPaths.TestRoot;
    if (scenario == "unavailable")
    {
        File.WriteAllText(root, "Directory creation must fail");
    }
    else
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "settings.json"), scenario == "malformed" ? "{broken" :
            "{\"AutoCheckUpdates\":false,\"ScaleNumerator\":5,\"LastUpdateCheckUtc\":\"2026-10-12T12:00:00Z\",\"LastNotifiedUpdateTag\":\"v1.1.1\",\"FutureSetting\":true}");
    }
    var settings = SettingsService.Settings;
    if (scenario == "valid")
    {
        if (settings.AutoCheckUpdates || settings.ScaleNumerator != 5 || settings.LastNotifiedUpdateTag != "v1.1.1" ||
            settings.LastUpdateCheckUtc.Kind != DateTimeKind.Utc) throw new Exception("Legacy settings were lost");
    }
    else if (!settings.AutoCheckUpdates || settings.ScaleNumerator != 1) throw new Exception("Default settings missing");
    settings.ScaleNumerator = 7;
    SettingsService.Save();
    if (scenario != "unavailable" && JsonConvert.DeserializeObject<PluginSettings>(File.ReadAllText(Path.Combine(root, "settings.json")))!.ScaleNumerator != 7)
        throw new Exception("Settings save failed");
    Console.WriteLine("PASS: settings " + scenario + ", defaults/save/legacy JSON compatibility.");
}
