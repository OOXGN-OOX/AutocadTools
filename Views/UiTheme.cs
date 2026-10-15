// Copyright (c) 2026 OOXGN. All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AutocadTools.Views
{
    internal static class UiTheme
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        private static void UpdateCaption(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            int dark = SystemParameters.HighContrast ? 0 : 1;
            // Windows 10/11 manages caption buttons, resize borders and snap behavior.
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        }

        public static void Apply(Window window)
        {
            var palette = new ResourceDictionary
            {
                Source = new Uri("/AutocadTools;component/Views/Styles.xaml", UriKind.Relative)
            };
            void Refresh()
            {
                foreach (var key in new[] { "WindowBackground", "ControlBackground", "ControlBorder", "Divider", "TextPrimary",
                    "TextSecondary", "AccentColor", "AccentText", "HoverBackground", "SelectionBackground", "DisabledText", "ErrorBrush", "InputBackground" })
                    window.Resources[key] = palette[key];
                UpdateCaption(window);
                if (!SystemParameters.HighContrast) return;
                window.Resources["WindowBackground"] = SystemColors.WindowBrush;
                window.Resources["ControlBackground"] = SystemColors.WindowBrush;
                window.Resources["InputBackground"] = SystemColors.WindowBrush;
                window.Resources["ControlBorder"] = SystemColors.WindowTextBrush;
                window.Resources["Divider"] = SystemColors.WindowTextBrush;
                window.Resources["TextPrimary"] = SystemColors.WindowTextBrush;
                window.Resources["TextSecondary"] = SystemColors.WindowTextBrush;
                window.Resources["AccentColor"] = SystemColors.HighlightBrush;
                window.Resources["AccentText"] = SystemColors.HighlightTextBrush;
                window.Resources["HoverBackground"] = SystemColors.ControlBrush;
                window.Resources["SelectionBackground"] = SystemColors.ControlBrush;
                window.Resources["DisabledText"] = SystemColors.GrayTextBrush;
                window.Resources["ErrorBrush"] = SystemColors.WindowTextBrush;
            }
            void OnSystemSettingsChanged(object sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(SystemParameters.HighContrast))
                    window.Dispatcher.Invoke(Refresh);
            }
            Refresh();
            window.SourceInitialized += (_, _) =>
            {
                UpdateCaption(window);
                SystemParameters.StaticPropertyChanged += OnSystemSettingsChanged;
            };
            window.Closed += (_, _) => SystemParameters.StaticPropertyChanged -= OnSystemSettingsChanged;
        }
    }
}
