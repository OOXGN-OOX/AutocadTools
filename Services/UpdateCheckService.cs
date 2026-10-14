// Copyright (c) 2026 OOXGN (GitHub: OOXGN-OOX). All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.IO;
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
        private static Task<PreparedUpdate> _installation;
        private static CancellationTokenSource _installationCancel;
        private static bool _reportInstallation;
        private static readonly HttpClient DownloadClient = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
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
            if (_installation != null)
            {
                if (manual) MessageBox.Show("Обновление уже загружается. Оно будет установлено после закрытия AutoCAD.", "Autocad Tools", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
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
            ProcessInstallation();
            if (_installation == null && _pending == null && SettingsService.Settings.AutoCheckUpdates && Schedule.IsDue(DateTime.UtcNow))
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
                    string state = UpdateInstallation.PendingState(SettingsService.Settings.PendingUpdateDirectory);
                    if (SettingsService.Settings.LastNotifiedUpdateTag == update.Tag && (state == "Waiting" || state == "Installing"))
                    {
                        if (manual) MessageBox.Show("Обновление готово и будет установлено после закрытия всех окон AutoCAD и Core Console.", "Autocad Tools", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    var installation = UpdateInstallation.Current();
                    if (update.CanInstall && installation != null)
                    {
                        bool automatic = SettingsService.Settings.AutoInstallUpdates;
                        if (!automatic && !manual && SettingsService.Settings.LastNotifiedUpdateTag == update.Tag) return;
                        if (!automatic && MessageBox.Show($"Доступна версия {update.Tag}. Скачать и установить её после закрытия AutoCAD?",
                            "Обновление Autocad Tools", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                        {
                            SettingsService.Settings.LastNotifiedUpdateTag = update.Tag;
                            SettingsService.Save();
                            return;
                        }
                        _reportInstallation = manual || !automatic;
                        _installationCancel = new CancellationTokenSource();
                        _installationCancel.CancelAfter(TimeSpan.FromMinutes(15));
                        _installation = DownloadAndQueue(update, installation, _installationCancel.Token);
                        doc.Editor.WriteMessage($"\n[Autocad Tools] Загружается обновление {update.Tag}. Установка выполнится после закрытия AutoCAD.\n");
                        return;
                    }
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

        private static async Task<PreparedUpdate> DownloadAndQueue(ReleaseVersion release, UpdateInstallation installation, CancellationToken token)
        {
            var update = await new UpdatePackageDownloader(DownloadClient).DownloadAsync(release,
                Path.Combine(PluginPaths.UserDataDirectory, "Updates"), token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                installation.Queue(update);
                return update;
            }
            catch
            {
                if (!File.Exists(Path.Combine(update.Directory, "request.json"))) Directory.Delete(update.Directory, true);
                throw;
            }
        }

        private static void ProcessInstallation()
        {
            if (_installation == null || !_installation.IsCompleted) return;
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null || !string.IsNullOrEmpty(doc.CommandInProgress)) return;
            var task = _installation;
            _installation = null;
            _installationCancel?.Dispose(); _installationCancel = null;
            try
            {
                var prepared = task.GetAwaiter().GetResult();
                SettingsService.Settings.LastNotifiedUpdateTag = prepared.Tag;
                SettingsService.Settings.PendingUpdateDirectory = prepared.Directory;
                SettingsService.Save();
                if (!_reportInstallation && !SettingsService.Settings.AutoInstallUpdates)
                {
                    UpdateInstallation.CancelPending(prepared.Directory);
                    return;
                }
                doc.Editor.WriteMessage($"\n[Autocad Tools] Обновление {prepared.Tag} проверено и готово. Закройте AutoCAD и Core Console для установки.\n");
                if (_reportInstallation) MessageBox.Show("Обновление скачано и проверено. Установка выполнится после закрытия всех окон AutoCAD и Core Console.",
                    "Autocad Tools", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\n[Autocad Tools] Обновление не установлено: " + ex.Message + "\n");
                if (_reportInstallation) MessageBox.Show("Не удалось подготовить обновление: " + ex.Message, "Autocad Tools", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        public static void Stop()
        {
            AcadApp.Idle -= OnIdle;
            _cancel?.Cancel(); _cancel?.Dispose(); _cancel = null;
            _pending?.ContinueWith(task => { var ignored = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            _pending = null;
            _installationCancel?.Cancel(); _installationCancel?.Dispose(); _installationCancel = null;
            _installation?.ContinueWith(task => { var ignored = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            _installation = null;
        }

        public static void ApplySettings()
        {
            if (!SettingsService.Settings.AutoInstallUpdates)
            {
                _installationCancel?.Cancel();
                _installation?.ContinueWith(task =>
                {
                    if (task.Status != TaskStatus.RanToCompletion) return;
                    try { UpdateInstallation.CancelPending(task.Result.Directory); }
                    catch (System.Exception ex) { Trace.TraceError("Could not cancel pending update: {0}", ex); }
                }, TaskScheduler.Default);
                UpdateInstallation.CancelPending(SettingsService.Settings.PendingUpdateDirectory);
            }
        }
    }
}
