// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace AutocadTools.UI
{
    internal static class FolderPicker
    {
        internal static string Choose(string current, Window owner = null)
        {
            var dialog = new OpenFolderDialog { Title = "Выберите папку сохранения", Multiselect = false };
            if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current)) dialog.InitialDirectory = current;
            bool? accepted = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            return accepted == true ? dialog.FolderName : null;
        }
    }
}
