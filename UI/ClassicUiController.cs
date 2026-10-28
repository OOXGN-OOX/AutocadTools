// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using AutocadTools.Infrastructure;
using AutocadTools.Services;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutocadTools.UI
{
    /// <summary>Desktop ActiveX menus/toolbars share the ribbon command catalog.</summary>
    internal sealed class ClassicUiController : IDisposable
    {
        private dynamic _menu, _toolbar;
        private string _workspace;
        private bool? _ribbonVisible;
        private bool _forceVisible;
        internal void Show()
        {
            _forceVisible = true; _workspace = null;
            EnsureCreated();
            AcadApp.SetSystemVariable("MENUBAR", 1);
        }
        internal void EnsureCreated()
        {
            if (!Process.GetCurrentProcess().ProcessName.Equals("acad", StringComparison.OrdinalIgnoreCase)) return;
            string workspace = AcadApp.GetSystemVariable("WSCURRENT")?.ToString() ?? "";
            bool ribbonVisible = Autodesk.Windows.ComponentManager.Ribbon?.IsVisible == true;
            if (_menu != null && _toolbar != null && _workspace == workspace && _ribbonVisible == ribbonVisible) return;
            dynamic app = AcadApp.AcadApplication;
            dynamic group = app.MenuGroups.Item(0);
            _menu = Find(group.Menus, ClassicCommandCatalog.TabTitle);
            bool populateMenu = _menu == null || !MenuMatches(_menu);
            if (_menu == null)
            {
                _menu = group.Menus.Add(ClassicCommandCatalog.TabTitle);
            }
            if (populateMenu)
            {
                ClearMenu(_menu);
                foreach (var panel in ClassicCommandCatalog.Panels)
                {
                    dynamic submenu = _menu.AddSubMenu(_menu.Count, panel.Title);
                    foreach (var button in panel.Buttons)
                        submenu.AddMenuItem(submenu.Count, Caption(button.Title), Macro(button.Command));
                }
            }
            if (!(bool)_menu.OnMenuBar) _menu.InsertInMenuBar(app.MenuBar.Count);
            _toolbar = Find(group.Toolbars, ClassicCommandCatalog.TabTitle);
            int expectedCount = ClassicCommandCatalog.Panels.Sum(panel => panel.Buttons.Length) + ClassicCommandCatalog.Panels.Length - 1;
            if (_toolbar != null && (int)_toolbar.Count != expectedCount) { _toolbar.Delete(); _toolbar = null; }
            if (_toolbar == null)
            {
                _toolbar = group.Toolbars.Add(ClassicCommandCatalog.TabTitle);
                _toolbar.Visible = true;
                foreach (var panel in ClassicCommandCatalog.Panels)
                {
                    if ((int)_toolbar.Count > 0) _toolbar.AddSeparator(_toolbar.Count);
                    foreach (var button in panel.Buttons)
                    {
                        dynamic item = _toolbar.AddToolbarButton(_toolbar.Count, Caption(button.Title), button.Description, Macro(button.Command));
                        try { string bitmap = GetBitmap(button.Icon); item.SetBitmaps(bitmap, bitmap); }
                        catch (Exception ex) { PluginLog.Error("Classic toolbar icon", ex); }
                    }
                }
                _toolbar.Dock(0); // acToolbarDockTop
            }
            _toolbar.Visible = _forceVisible || !ribbonVisible;
            _workspace = workspace; _ribbonVisible = ribbonVisible;
        }
        private static bool MenuMatches(dynamic menu)
        {
            if ((int)menu.Count != ClassicCommandCatalog.Panels.Length) return false;
            for (int p = 0; p < ClassicCommandCatalog.Panels.Length; p++)
            {
                dynamic submenu = menu.Item(p).SubMenu;
                if ((int)submenu.Count != ClassicCommandCatalog.Panels[p].Buttons.Length) return false;
                for (int b = 0; b < ClassicCommandCatalog.Panels[p].Buttons.Length; b++)
                    if ((string)submenu.Item(b).Macro != Macro(ClassicCommandCatalog.Panels[p].Buttons[b].Command)) return false;
            }
            return true;
        }
        private static object Find(dynamic collection, string name)
        {
            for (int i = 0; i < (int)collection.Count; i++)
            {
                dynamic item = collection.Item(i);
                if (string.Equals((string)item.Name, name, StringComparison.Ordinal)) return item;
            }
            return null;
        }
        private static void ClearMenu(dynamic menu)
        {
            for (int i = (int)menu.Count - 1; i >= 0; i--) menu.Item(i).Delete();
        }
        internal static string Caption(string title) => title.Replace("\n", " ").Replace("Мульти- копирование", "Мультикопирование");
        internal static string Macro(string command) => "^C^C" + command + " ";
        private static string GetBitmap(string icon)
        {
            string directory = Path.Combine(PluginPaths.UserDataDirectory, "UI", "current");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, Path.ChangeExtension(icon, ".bmp"));
            if (File.Exists(path)) return path;
            var drawing = Views.ToolIcons.Create(icon, true);
            var visual = new System.Windows.Media.DrawingVisual();
            using (var context = visual.RenderOpen()) context.DrawImage(drawing, new System.Windows.Rect(0, 0, 32, 32));
            var source = new RenderTargetBitmap(32, 32, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            source.Render(visual);
            var encoder = new BmpBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = File.Create(path); encoder.Save(stream);
            return path;
        }
        public void Dispose()
        {
            try { if (_toolbar != null) _toolbar.Delete(); }
            finally
            {
                _toolbar = null;
                try
                {
                    if (_menu != null)
                    {
                        if ((bool)_menu.OnMenuBar) _menu.RemoveFromMenuBar();
                        ClearMenu(_menu);
                    }
                }
                finally { _menu = null; _workspace = null; _ribbonVisible = null; }
            }
        }
    }
}
