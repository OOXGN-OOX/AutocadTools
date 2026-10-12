// Copyright (c) 2026 OOXGN (GitHub: OOXGN-OOX). All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutocadTools.Services
{
    public static class UpdateCheckService
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private static CancellationTokenSource _cancel;
        private static Task<ReleaseVersion> _pending;
        private static bool _manual;
        private static readonly UpdateSchedule Schedule = new UpdateSchedule();

        public static void Start()
        {
            Stop();
            try
            {
                var settings = SettingsService.Settings;
                Schedule.NewSession();
                // Every new AutoCAD session checks, even if another session checked today.
                if (settings.AutoCheckUpdates) Check(false);
                AcadApp.Idle += OnIdle;
            }
            catch
            {
                Stop();
                throw;
            }
        }
        public static void Check(bool manual)
        {
            if (_pending != null) { if (manual) _manual = true; return; }
            _manual = manual;
            if (!manual) Schedule.Checked(DateTime.UtcNow);
            SettingsService.Settings.LastUpdateCheckUtc = DateTime.UtcNow;
            SettingsService.Save();
            _cancel = new CancellationTokenSource();
            bool prerelease = SettingsService.Settings.IncludePrereleaseUpdates;
            _pending = Fetch(prerelease, _cancel.Token);
        }
        private static async Task<ReleaseVersion> Fetch(bool prerelease, CancellationToken token)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/OOXGN-OOX/AutocadTools/releases?per_page=30"))
            {
                request.Headers.UserAgent.ParseAdd("AutocadTools/" + Assembly.GetExecutingAssembly().GetName().Version);
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using (var response = await Client.SendAsync(request, token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    return ReleaseVersion.FindUpdate(json, Assembly.GetExecutingAssembly().GetName().Version, prerelease);
                }
            }
        }
        private static void OnIdle(object sender, EventArgs e)
        {
            try { ProcessIdle(); }
            catch (System.Exception ex)
            {
                // Unexpected host/startup failures must not repeat on every Idle.
                Stop();
                Trace.TraceError("Autocad Tools update service stopped: {0}", ex);
            }
        }
        private static void ProcessIdle()
        {
            if (_pending == null && SettingsService.Settings.AutoCheckUpdates && Schedule.IsDue(DateTime.UtcNow))
                Check(false);
            if (_pending == null || !_pending.IsCompleted || AcadApp.DocumentManager.MdiActiveDocument == null) return;
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (!string.IsNullOrEmpty(doc.CommandInProgress)) return;
            var task = _pending; var manual = _manual;
            _pending = null;
            _cancel?.Dispose(); _cancel = null;
            try
            {
                var update = task.GetAwaiter().GetResult();
                if (!manual && !SettingsService.Settings.AutoCheckUpdates) return;
                SettingsService.Settings.LastUpdateCheckUtc = DateTime.UtcNow;
                SettingsService.Save();
                if (update != null)
                {
                    if (!manual && SettingsService.Settings.LastNotifiedUpdateTag == update.Tag) return;
                    SettingsService.Settings.LastNotifiedUpdateTag = update.Tag;
                    SettingsService.Save();
                    if (MessageBox.Show($"Доступна новая версия {update.Tag}. Открыть страницу загрузки?\n\nПеред запуском установщика закройте AutoCAD.",
                        "Обновление Autocad Tools", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                        Process.Start(new ProcessStartInfo(update.Url.AbsoluteUri) { UseShellExecute = true });
                }
                else if (manual) MessageBox.Show("Более новой опубликованной версии не найдено.", "Autocad Tools", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                if (manual) MessageBox.Show("Не удалось проверить обновления: " + ex.Message, "Autocad Tools", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        public static void Stop()
        {
            AcadApp.Idle -= OnIdle;
            _cancel?.Cancel(); _cancel?.Dispose(); _cancel = null;
            _pending?.ContinueWith(task => { var ignored = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            _pending = null;
        }
    }
}
