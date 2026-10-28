// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.Text;
using System.Globalization;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    /// <summary>Single pass over MTEXT codes. Escaped literal text is never parsed as formatting.</summary>
    internal static class MTextFormatting
    {
        public static string Strip(string text, StripTextOptions options)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (options == null) throw new ArgumentNullException(nameof(options));
            var result = new StringBuilder(text.Length);
            int nextTerminator = text.IndexOf(';');
            bool removeGroups = options.Font || options.Color || options.Height;
            for (int index = 0; index < text.Length; index++)
            {
                char current = text[index];
                if (current is '{' or '}' && removeGroups) continue;
                if (current == '\t' && options.Tabs) { result.Append(' '); continue; }
                if (current != '\\' || index + 1 == text.Length) { result.Append(current); continue; }

                char code = text[++index];
                if (code is '\\' or '{' or '}')
                {
                    result.Append('\\').Append(code);
                    continue;
                }
                if ((code is 'L' or 'l' && options.Underline) || (code is 'O' or 'o' && options.Overline)) continue;
                if (code == '~' && options.NonBreakingSpace) { result.Append(' '); continue; }
                if (code == 'P') { result.Append(options.LineFeed ? "\n" : "\\P"); continue; }

                bool remove = code switch
                {
                    'A' => options.Alignment,
                    'C' or 'c' => options.Color,
                    'F' or 'f' => options.Font,
                    'H' => options.Height,
                    'Q' => options.Oblique,
                    'W' => options.Width,
                    'T' => options.Tracking,
                    'p' => options.Paragraph,
                    'S' => options.Stacking,
                    _ => false
                };
                if (remove)
                {
                    while (nextTerminator >= 0 && nextTerminator <= index)
                        nextTerminator = text.IndexOf(';', nextTerminator + 1);
                    int end = nextTerminator;
                    if (end >= 0 && IsArgumentValid(code, text.AsSpan(index + 1, end - index - 1)))
                    {
                        if (code == 'S')
                            for (int content = index + 1; content < end; content++)
                                result.Append(text[content] switch { '^' => ' ', '#' => '/', var value => value });
                        index = end;
                        continue;
                    }
                }
                result.Append('\\').Append(code);
            }
            return result.ToString();
        }

        private static bool IsArgumentValid(char code, ReadOnlySpan<char> value)
        {
            if (value.IndexOf('\\') >= 0 || value.IndexOf('\n') >= 0) return false;
            if (code is 'F' or 'f' or 'p' or 'S') return true;
            if (code == 'H' && value.Length > 0 && value[^1] is 'x' or 'X') value = value[..^1];
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number);
        }
    }
}
