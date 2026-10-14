// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AutocadTools.Services
{
    public sealed class PreparedUpdate
    {
        public string Directory { get; }
        public string InstallerPath { get; }
        public string Sha256 { get; }
        public string Tag { get; }
        public PreparedUpdate(string directory, string installerPath, string sha256, string tag)
        { Directory = directory; InstallerPath = installerPath; Sha256 = sha256; Tag = tag; }
    }

    public sealed class UpdatePackageDownloader
    {
        private readonly HttpClient _client;
        public UpdatePackageDownloader(HttpClient client) { _client = client; }

        public async Task<PreparedUpdate> DownloadAsync(ReleaseVersion release, string cacheRoot, CancellationToken token)
        {
            if (!release.CanInstall) throw new InvalidOperationException("В выпуске нет установщика и контрольных сумм.");
            if (release.Installer.Size > 512L * 1024 * 1024 || release.Checksums.Size > 1024 * 1024)
                throw new InvalidOperationException("Размер обновления превышает допустимый.");
            string directory = Path.Combine(cacheRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                byte[] sums = await ReadAssetAsync(release.Checksums, token).ConfigureAwait(false);
                string hash = ParseChecksum(Encoding.UTF8.GetString(sums), release.Installer.Name);
                if (release.Installer.Sha256 != null && release.Installer.Sha256 != hash)
                    throw new InvalidDataException("Контрольные суммы выпуска не совпадают.");
                string installer = Path.Combine(directory, release.Installer.Name);
                using (var request = CreateRequest(release.Installer))
                using (var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    ValidateResponse(response, release.Installer);
                    using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                    using (var output = new FileStream(installer, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    using (var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                    {
                        byte[] buffer = new byte[81920];
                        long length = 0;
                        int read;
                        while ((read = await input.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
                        {
                            length += read;
                            if (length > release.Installer.Size) throw new InvalidDataException("Размер установщика не совпадает с выпуском.");
                            digest.AppendData(buffer, 0, read);
                            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                        }
                        string actual = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
                        if (length != release.Installer.Size || actual != hash)
                            throw new InvalidDataException("Установщик не прошёл проверку SHA256. Установка отменена.");
                    }
                }
                return new PreparedUpdate(directory, installer, hash, release.Tag);
            }
            catch
            {
                // This directory is created exclusively by this call under the supplied cache root.
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
                throw;
            }
        }

        private async Task<byte[]> ReadAssetAsync(ReleaseAsset asset, CancellationToken token)
        {
            using (var request = CreateRequest(asset))
            using (var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                ValidateResponse(response, asset);
                using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                using (var output = new MemoryStream())
                {
                    byte[] buffer = new byte[4096];
                    int read;
                    while ((read = await input.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
                    {
                        if (output.Length + read > asset.Size) throw new InvalidDataException("Размер файла контрольных сумм не совпадает.");
                        output.Write(buffer, 0, read);
                    }
                    if (output.Length != asset.Size) throw new InvalidDataException("Неполный файл контрольных сумм.");
                    return output.ToArray();
                }
            }
        }

        private static void ValidateResponse(HttpResponseMessage response, ReleaseAsset asset)
        {
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != "https")
                throw new InvalidDataException("Загрузка обновления требует HTTPS.");
            if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength != asset.Size)
                throw new InvalidDataException("Размер загружаемого файла не совпадает с выпуском.");
        }

        private static HttpRequestMessage CreateRequest(ReleaseAsset asset)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
            request.Headers.UserAgent.ParseAdd("AutocadTools/" + typeof(UpdatePackageDownloader).Assembly.GetName().Version);
            request.Headers.Accept.ParseAdd("application/octet-stream");
            return request;
        }

        public static string ParseChecksum(string text, string installerName)
        {
            string result = null;
            foreach (string line in text.TrimStart('\uFEFF').Split('\n'))
            {
                var match = Regex.Match(line.TrimEnd('\r'), "^([0-9a-fA-F]{64}) [ *](.+)$");
                if (!match.Success || match.Groups[2].Value != installerName) continue;
                if (result != null) throw new InvalidDataException("Повторная запись контрольной суммы установщика.");
                result = match.Groups[1].Value.ToLowerInvariant();
            }
            return result ?? throw new InvalidDataException("В SHA256SUMS.txt нет контрольной суммы установщика.");
        }
    }
}
