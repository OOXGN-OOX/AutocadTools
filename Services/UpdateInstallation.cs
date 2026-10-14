// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

namespace AutocadTools.Services
{
    public sealed class UpdateInstallation
    {
        public string Directory { get; }
        public bool AllUsers { get; }
        private UpdateInstallation(string directory, bool allUsers) { Directory = directory; AllUsers = allUsers; }

        public static UpdateInstallation Detect(string assemblyPath, string appData, string programFiles)
        {
            string[] folders = { Path.Combine(appData, "Autodesk", "ApplicationPlugins", "AutocadTools.bundle"),
                Path.Combine(programFiles, "Autodesk", "ApplicationPlugins", "AutocadTools.bundle") };
            for (int i = 0; i < folders.Length; i++)
            {
                foreach (string profile in new[] { "2025-2026", "2027" })
                    if (string.Equals(Path.GetFullPath(assemblyPath), Path.Combine(folders[i], "Contents", "Windows", profile, "AutocadTools.dll"), StringComparison.OrdinalIgnoreCase))
                        return new UpdateInstallation(folders[i], i == 1);
            }
            return null;
        }

        public static UpdateInstallation Current() => Detect(Assembly.GetExecutingAssembly().Location,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

        public void Queue(PreparedUpdate update)
        {
            var start = PrepareRunner(update);
            try
            {
                using (var process = Process.Start(start))
                    if (process == null) throw new InvalidOperationException("Не удалось запустить установку обновления.");
            }
            catch
            {
                File.WriteAllText(Path.Combine(update.Directory, "status.json"), "{\"State\":\"Failed\"}");
                throw;
            }
        }

        public ProcessStartInfo PrepareRunner(PreparedUpdate update)
        {
            string runner = Path.Combine(update.Directory, "UpdateRunner.ps1");
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("AutocadTools.UpdateRunner.ps1"))
            {
                if (resource == null) throw new InvalidOperationException("Модуль установки обновлений отсутствует.");
                using (var output = File.Create(runner)) resource.CopyTo(output);
            }
            string request = Path.Combine(update.Directory, "request.json");
            File.WriteAllText(request, JsonConvert.SerializeObject(new
            {
                InstallerPath = update.InstallerPath, update.Sha256, update.Tag,
                InstallDirectory = Directory, AllUsers, TimeoutSeconds = 86400
            }), new System.Text.UTF8Encoding(false));
            File.WriteAllText(Path.Combine(update.Directory, "status.json"), JsonConvert.SerializeObject(new { State = "Waiting", UpdatedUtc = DateTime.UtcNow }));
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", runner, "-RequestPath", request })
                start.ArgumentList.Add(argument);
            return start;
        }

        public static string PendingState(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return null;
            string root = Path.Combine(PluginPaths.UserDataDirectory, "Updates") + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(directory).StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
            try
            {
                var status = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(directory, "status.json")));
                string state = status.Value<string>("State");
                var updated = status.Value<DateTime?>("UpdatedUtc");
                if ((state == "Waiting" || state == "Installing") && (!updated.HasValue || DateTime.UtcNow - updated.Value.ToUniversalTime() > TimeSpan.FromHours(24)))
                    return "Expired";
                return state;
            }
            catch { return null; }
        }

        public static void CancelPending(string directory)
        {
            if (PendingState(directory) == "Waiting") File.WriteAllText(Path.Combine(directory, "cancel"), "");
        }
    }
}
