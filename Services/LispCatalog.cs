// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Reflection;
using Newtonsoft.Json;

namespace AutocadTools.Services
{
    internal sealed record LispEntry(string FilePath, string RelativePath, string Command)
    {
        internal string Title { get; init; } = Command ?? "Загрузить библиотеку";
        internal string Group { get; init; } = "Другие функции";
        internal string Symbol { get; init; } = Command == null ? null : "c:" + Command;
        internal string Description { get; init; }
        internal string UnavailableReason { get; init; }
    }

    internal static class LispCatalog
    {
        internal static string Root => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Assets", "AdditionalFunctions");
        private sealed class ManifestEntry
        {
            public string File { get; set; }
            public string Command { get; set; }
            public string Symbol { get; set; }
            public string Title { get; set; }
            public string Group { get; set; }
            public string Description { get; set; }
            public string UnavailableReason { get; set; }
        }
        internal static IReadOnlyList<LispEntry> Read(string root = null)
        {
            if (root == null)
            {
                root = Root;
                var manifest = Path.Combine(root, "catalog.json");
                if (!File.Exists(manifest)) throw new FileNotFoundException("Не найдена встроенная библиотека дополнительных функций. Переустановите плагин.", manifest);
                var data = JsonConvert.DeserializeObject<List<ManifestEntry>>(File.ReadAllText(manifest)) ?? new();
                return data.Select(e =>
                {
                    var path = Path.GetFullPath(Path.Combine(root, e.File));
                    if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                        throw new InvalidDataException("Повреждена библиотека дополнительных функций: " + e.File);
                    return new LispEntry(path, e.File, e.Command) { Title = e.Title, Group = e.Group, Symbol = e.Symbol, Description = e.Description, UnavailableReason = e.UnavailableReason };
                }).ToArray();
            }
            if (!Directory.Exists(root)) return Array.Empty<LispEntry>();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var entries = new List<LispEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var files = Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Where(path => new[] { ".lsp", ".fas", ".vlx" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .OrderBy(path => path.Contains("архив", StringComparison.OrdinalIgnoreCase))
                .ThenBy(path => path.Length).ThenBy(path => path, StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                try
                {
                    var bytes = File.ReadAllBytes(file);
                    if (!seen.Add(Convert.ToHexString(SHA256.HashData(bytes)))) continue;
                    var relative = Path.GetRelativePath(root, file);
                    if (!Path.GetExtension(file).Equals(".lsp", StringComparison.OrdinalIgnoreCase))
                    {
                        entries.Add(new LispEntry(file, relative, null));
                        continue;
                    }
                    string source;
                    if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 254) source = Encoding.Unicode.GetString(bytes);
                    else if (bytes.Length >= 2 && bytes[0] == 254 && bytes[1] == 255) source = Encoding.BigEndianUnicode.GetString(bytes);
                    else
                    {
                        try { source = new UTF8Encoding(false, true).GetString(bytes); }
                        catch (DecoderFallbackException) { source = Encoding.GetEncoding(1251).GetString(bytes); }
                    }
                    var commands = GetCommands(source);
                    if (commands.Count == 0) entries.Add(new LispEntry(file, relative, null));
                    else foreach (var command in commands) entries.Add(new LispEntry(file, relative, command));
                }
                catch (IOException ex) { Infrastructure.PluginLog.Error("Read LISP " + file, ex); }
                catch (UnauthorizedAccessException ex) { Infrastructure.PluginLog.Error("Read LISP " + file, ex); }
            }
            return entries;
        }

        internal static IReadOnlyList<string> GetCommands(string source)
        {
            // Strip comments and string contents before finding public command definitions.
            var code = new StringBuilder(source.Length);
            bool quoted = false, escaped = false, lineComment = false, blockComment = false;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (blockComment)
                {
                    if (c == '|' && i + 1 < source.Length && source[i + 1] == ';') { blockComment = false; i++; }
                    code.Append(' '); continue;
                }
                if (lineComment) { if (c == '\n' || c == '\r') lineComment = false; code.Append(c == '\n' ? '\n' : ' '); continue; }
                if (quoted)
                {
                    if (!escaped && c == '"') quoted = false;
                    bool wasEscaped = escaped;
                    escaped = !wasEscaped && c == '\\';
                    code.Append(' '); continue;
                }
                if (c == ';')
                {
                    if (i + 1 < source.Length && source[i + 1] == '|') { blockComment = true; i++; }
                    else lineComment = true;
                    code.Append(' '); continue;
                }
                if (c == '"') { quoted = true; code.Append(' '); continue; }
                code.Append(c);
            }
            return Regex.Matches(code.ToString(), @"\(\s*defun(?:-q)?\s+c:([^\s()""';]+)", RegexOptions.IgnoreCase)
                .Cast<Match>().Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        internal static string BuildExpression(LispEntry entry)
        {
            static string Quote(string text) => "\"" + text.Replace("\\", "/").Replace("\"", "\\\"") + "\"";
            if (!File.Exists(entry.FilePath)) throw new FileNotFoundException("LISP-файл больше не доступен.", entry.FilePath);
            if (entry.UnavailableReason != null) throw new InvalidOperationException(entry.UnavailableReason);
            var root = entry.FilePath.Substring(0, entry.FilePath.Length - entry.RelativePath.Length).TrimEnd('\\', '/');
            var support = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(p => new[] { ".lsp", ".fas", ".vlx", ".dcl", ".dwg", ".shx" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                .Select(Path.GetDirectoryName).Prepend(Path.GetDirectoryName(entry.FilePath)).Append(root).Distinct(StringComparer.OrdinalIgnoreCase);
            var invoke = entry.Symbol == null ? "" : " (eval (list (read " + Quote(entry.Symbol) + ")))";
            return "(progn (vl-load-com) (setq botplus:library-root " + Quote(root) + ") " +
                "(defun botplus:invoke-library (/ bp-old-acad bp-old-error bp-result) " +
                "(setq bp-old-acad (getenv \"ACAD\") bp-old-error *error*) " +
                "(setenv \"ACAD\" (strcat " + Quote(string.Join(";", support) + ";") + " (if bp-old-acad bp-old-acad \"\"))) " +
                "(setq bp-result (vl-catch-all-apply '(lambda () (load " + Quote(entry.FilePath) + ")" + invoke + "))) " +
                "(setenv \"ACAD\" (if bp-old-acad bp-old-acad \"\")) (setq *error* bp-old-error) " +
                "(if (vl-catch-all-error-p bp-result) (princ (strcat \"\\n[Autocad Tools] \" (vl-catch-all-error-message bp-result)))) (princ)) (botplus:invoke-library))\n";
        }
    }
}
