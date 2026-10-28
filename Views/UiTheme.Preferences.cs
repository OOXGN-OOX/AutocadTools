// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System.Windows;
namespace AutocadTools.Views
{
    internal static partial class UiTheme
    {
        static partial void AttachPreferences(Window window) => UI.WindowPreferences.Attach(window);
    }
}
