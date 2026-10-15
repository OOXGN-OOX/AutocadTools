// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AutocadTools.Services
{
    public sealed class CommandAlias : INotifyPropertyChanged
    {
        public CommandDefinition Command { get; }
        private string _aliases = "";
        public string Aliases
        {
            get => _aliases;
            set { _aliases = value ?? ""; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Aliases))); }
        }
        public CommandAlias(CommandDefinition command) { Command = command; }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    // Only this delimited section belongs to the plugin. All other PGP text is preserved.
    public sealed class CommandAliases
    {
        private const string Begin = "; BEGIN AUTOCAD TOOLS ALIASES";
        private const string End = "; END AUTOCAD TOOLS ALIASES";
        private static readonly Regex Entry = new Regex(@"^\s*([^;,\s]+)\s*,\s*(.*)$", RegexOptions.Multiline);
        private readonly string _path, _text, _outside, _newline;
        private readonly Encoding _encoding;
        private readonly byte[] _original, _preamble;
        private readonly Dictionary<string, string> _occupied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<CommandAlias> Rows { get; }
        public string Path => _path;

        public CommandAliases(string path, int ansiCodePage = 0)
        {
            _path = path;
            _original = File.ReadAllBytes(path);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _encoding = DetectEncoding(_original, ansiCodePage == 0 ? CultureInfo.CurrentCulture.TextInfo.ANSICodePage : ansiCodePage);
            _preamble = _encoding.GetPreamble();
            int offset = _original.Take(_preamble.Length).SequenceEqual(_preamble) ? _preamble.Length : 0;
            _text = _encoding.GetString(_original, offset, _original.Length - offset);
            _newline = _text.Contains("\r\n") ? "\r\n" : "\n";
            var starts = Regex.Matches(_text, @"(?m)^" + Regex.Escape(Begin) + @"\r?$");
            var ends = Regex.Matches(_text, @"(?m)^" + Regex.Escape(End) + @"\r?$");
            if (starts.Count != ends.Count || starts.Count > 1 ||
                (starts.Count == 1 && starts[0].Index >= ends[0].Index))
                throw new InvalidOperationException("Повреждён раздел сокращений Autocad Tools в PGP.");
            string managed = "";
            _outside = _text;
            if (starts.Count == 1)
            {
                int length = ends[0].Index + ends[0].Length - starts[0].Index;
                if (_text.Substring(starts[0].Index + length).StartsWith("\n")) length++;
                managed = _text.Substring(starts[0].Index, length);
                _outside = _text.Remove(starts[0].Index, length);
            }
            foreach (Match match in Entry.Matches(_outside))
                _occupied[match.Groups[1].Value] = match.Groups[2].Value.Trim();
            Rows = CommandCatalog.All.Select(c => new CommandAlias(c)).ToArray();
            foreach (var row in Rows)
                row.Aliases = string.Join(", ", Entry.Matches(managed).Cast<Match>()
                    .Where(m => string.Equals(m.Groups[2].Value.Trim(), "*" + row.Command.English, StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.Groups[1].Value));
        }

        private static Encoding DetectEncoding(byte[] bytes, int codePage)
        {
            if (bytes.Take(3).SequenceEqual(new byte[] { 239, 187, 191 })) return new UTF8Encoding(true, true);
            if (bytes.Take(2).SequenceEqual(new byte[] { 255, 254 })) return new UnicodeEncoding(false, true, true);
            if (bytes.Take(2).SequenceEqual(new byte[] { 254, 255 })) return new UnicodeEncoding(true, true, true);
            try { new UTF8Encoding(false, true).GetString(bytes); return new UTF8Encoding(false, true); }
            catch (DecoderFallbackException)
            { return Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback); }
        }

        private string Compose()
        {
            var entries = new List<string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(CommandCatalog.All.SelectMany(c => new[] { c.English, c.Russian }), StringComparer.OrdinalIgnoreCase);
            foreach (var row in Rows)
            foreach (string alias in Regex.Split(row.Aliases.Trim(), @"[,\s]+").Where(s => s.Length > 0))
            {
                if (!Regex.IsMatch(alias, @"^\p{L}[\p{L}\p{Nd}]{0,15}$"))
                    throw new InvalidOperationException("Сокращение «" + alias + "»: используйте от 1 до 16 букв или цифр, начиная с буквы.");
                if (names.Contains(alias)) throw new InvalidOperationException("«" + alias + "» — полное имя команды.");
                if (_occupied.TryGetValue(alias, out var target))
                    throw new InvalidOperationException("«" + alias + "» уже занято в PGP: " + target);
                if (_occupied.Values.Any(v => v.StartsWith("*") && string.Equals(v.Substring(1).TrimStart('_', '.'), alias, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("«" + alias + "» совпадает с именем команды из PGP.");
                if (!used.Add(alias)) throw new InvalidOperationException("Сокращение «" + alias + "» назначено несколько раз.");
                entries.Add(alias.ToUpperInvariant() + ", *" + row.Command.English);
            }
            string text = _outside;
            if (entries.Count > 0)
            {
                if (text.Length > 0 && !text.EndsWith("\n")) text += _newline;
                text += Begin + _newline + string.Join(_newline, entries) + _newline + End + _newline;
            }
            return text;
        }

        public string Validate()
        {
            try { _encoding.GetBytes(Compose()); return null; }
            catch (EncoderFallbackException) { return "Кодировка этого PGP не поддерживает введённые символы. Используйте латинские сокращения."; }
            catch (InvalidOperationException ex) { return ex.Message; }
        }

        public void Suggest()
        {
            foreach (var row in Rows.Where(r => string.IsNullOrWhiteSpace(r.Aliases) && r.Command.SuggestedAlias.Length > 0))
            {
                row.Aliases = row.Command.SuggestedAlias;
                if (Validate() != null) row.Aliases = "";
            }
        }

        public bool Save()
        {
            string error = Validate();
            if (error != null) throw new InvalidOperationException(error);
            byte[] bytes = _preamble.Concat(_encoding.GetBytes(Compose())).ToArray();
            if (!File.ReadAllBytes(_path).SequenceEqual(_original))
                throw new IOException("PGP изменён другим приложением. Закройте окно и откройте его заново.");
            if (bytes.SequenceEqual(_original)) return false;
            string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                File.Replace(temporary, _path, _path + ".AutocadTools.bak");
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return true;
        }
    }
}
