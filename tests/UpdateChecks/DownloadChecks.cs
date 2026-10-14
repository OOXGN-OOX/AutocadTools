// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using AutocadTools.Services;
using Newtonsoft.Json;
using System.Net;
using System.Security.Cryptography;
using System.Text;

static class DownloadChecks
{
    public static async Task Run()
    {
        byte[] installer = Encoding.UTF8.GetBytes("test installer payload, never executed");
        string hash = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
        string name = "AutocadTools-1.2.0-Setup.exe";
        byte[] sums = Encoding.UTF8.GetBytes(hash + "  " + name + "\n");
        ReleaseVersion Release(byte[] manifest, string digest = null, string downloadUrl = null, long? size = null) => ReleaseVersion.FindUpdate(JsonConvert.SerializeObject(new[] {
            new { tag_name = "v1.2.0", draft = false, prerelease = false, html_url = "https://github.com/OOXGN-OOX/AutocadTools/releases/tag/v1.2.0",
                assets = new[] {
                    new { name, state = "uploaded", size = size ?? installer.LongLength, digest, browser_download_url = downloadUrl ?? "https://github.com/OOXGN-OOX/AutocadTools/releases/download/v1.2.0/" + name },
                    new { name = "SHA256SUMS.txt", state = "uploaded", size = manifest.LongLength, digest = (string)null, browser_download_url = "https://github.com/OOXGN-OOX/AutocadTools/releases/download/v1.2.0/SHA256SUMS.txt" }
                } }
        }), new Version(1, 1, 0));
        var root = Path.Combine(PluginPaths.TestRoot, "Updates");
        Directory.CreateDirectory(root);
        HttpClient Client(byte[] body, byte[] manifest, bool offline = false) => new HttpClient(new Handler(request => {
            if (offline) throw new HttpRequestException("Offline");
            return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request,
                Content = new ByteArrayContent(request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt") ? manifest : body) };
        }));
        using (var client = Client(installer, sums))
        {
            var release = Release(sums, "sha256:" + hash);
            if (!release.CanInstall) throw new Exception("Installer assets not selected");
            var prepared = await new UpdatePackageDownloader(client).DownloadAsync(release, root, CancellationToken.None);
            if (!File.ReadAllBytes(prepared.InstallerPath).SequenceEqual(installer) || prepared.Sha256 != hash) throw new Exception("Valid download failed");
            var user = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var common = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var userAssembly = Path.Combine(user, "Autodesk", "ApplicationPlugins", "AutocadTools.bundle", "Contents", "Windows", "2027", "AutocadTools.dll");
            var commonAssembly = Path.Combine(common, "Autodesk", "ApplicationPlugins", "AutocadTools.bundle", "Contents", "Windows", "2025-2026", "AutocadTools.dll");
            var perUser = UpdateInstallation.Detect(userAssembly, user, common);
            var allUsers = UpdateInstallation.Detect(commonAssembly, user, common);
            if (perUser == null || perUser.AllUsers || allUsers == null || !allUsers.AllUsers) throw new Exception("Installation scope mismatch");
            if (UpdateInstallation.Detect(Path.Combine(root, "AutocadTools.dll"), user, common) != null) throw new Exception("Development DLL treated as installed");
            var start = perUser.PrepareRunner(prepared);
            if (start.UseShellExecute || !start.CreateNoWindow || !start.ArgumentList.Contains("-RequestPath")) throw new Exception("Update helper launch flags incorrect");
            if (!File.Exists(Path.Combine(prepared.Directory, "UpdateRunner.ps1"))) throw new Exception("Embedded updater missing");
            if (UpdateInstallation.PendingState(prepared.Directory) != "Waiting") throw new Exception("Pending update not persisted");
            UpdateInstallation.CancelPending(prepared.Directory);
            if (!File.Exists(Path.Combine(prepared.Directory, "cancel"))) throw new Exception("Pending cancellation not recorded");
            File.WriteAllText(Path.Combine(prepared.Directory, "status.json"), "{\"State\":\"Waiting\",\"UpdatedUtc\":\"2020-01-01T00:00:00Z\"}");
            if (UpdateInstallation.PendingState(prepared.Directory) != "Expired") throw new Exception("Stale helper suppresses updates forever");
            if (UpdateInstallation.PendingState(Path.GetTempPath()) != null) throw new Exception("Untrusted status path accepted");
            Directory.Delete(prepared.Directory, true);
        }
        async Task Reject(byte[] body, byte[] manifest, ReleaseVersion release, bool offline = false, bool cancel = false)
        {
            using var client = Client(body, manifest, offline);
            using var cancelled = new CancellationTokenSource();
            if (cancel) cancelled.Cancel();
            bool rejected = false;
            try { await new UpdatePackageDownloader(client).DownloadAsync(release, root, cancelled.Token); }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is HttpRequestException || ex is OperationCanceledException || ex is InvalidOperationException) { rejected = true; }
            if (!rejected || Directory.EnumerateDirectories(root).Any()) throw new Exception("Bad download accepted or partial files retained");
        }
        byte[] corrupt = installer.ToArray(); corrupt[0] ^= 1;
        await Reject(corrupt, sums, Release(sums));
        await Reject(installer, sums, Release(sums, "sha256:" + new string('0', 64)));
        await Reject(installer, sums, Release(sums), offline: true);
        await Reject(installer, sums, Release(sums), cancel: true);
        await Reject(installer, sums, Release(sums, size: installer.Length + 10));
        byte[] duplicate = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(sums) + Encoding.UTF8.GetString(sums));
        await Reject(installer, duplicate, Release(duplicate));
        if (Release(sums, downloadUrl: "https://evil.invalid/installer.exe").CanInstall) throw new Exception("Untrusted installer URL accepted");
        if (Release(sums, downloadUrl: "https://github.com/other/repo/releases/download/v1.2.0/" + name).CanInstall) throw new Exception("Another repository asset accepted");
        try { UpdatePackageDownloader.ParseChecksum(hash + "  ../" + name, name); throw new Exception("Path traversal checksum accepted"); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS: updater assets, SHA256, sizes, offline/cancellation cleanup, embedded helper, scopes and queued state.");
    }

    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(send(request)); }
    }
}
