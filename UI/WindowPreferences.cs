// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using AutocadTools.Infrastructure;
using AutocadTools.Services;
using Newtonsoft.Json.Linq;

namespace AutocadTools.UI
{
    internal static class WindowPreferences
    {
        private static IEnumerable<PropertyInfo> Properties(object source) => source.GetType().GetProperties()
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 &&
                (p.PropertyType.IsPrimitive || p.PropertyType.IsEnum || p.PropertyType == typeof(string)));

        private static object Source(Window window)
        {
            var vm = window.DataContext;
            if (vm == null) return null;
            return vm.GetType().Name switch
            {
                "PlanPrepViewModel" or "CleanDwgViewModel" or "StripTextViewModel" or "SelectSimilarViewModel" => vm.GetType().GetProperty("Options")?.GetValue(vm),
                "AutoNumSettingsViewModel" => vm.GetType().GetProperty("Settings")?.GetValue(vm),
                "CustomerOrderViewModel" => vm,
                _ => null
            };
        }

        internal static void Restore(Window window)
        {
            var source = Source(window);
            if (source == null) return;
            var saved = UserPreferences.Read(window.GetType().Name);
            foreach (var property in Properties(source))
            {
                if (!saved.TryGetValue(property.Name, out var token) || token.Type == JTokenType.Null) continue;
                try { property.SetValue(source, token.ToObject(property.PropertyType)); }
                catch (Exception ex) { PluginLog.Error("Restore preference " + property.Name, ex); }
            }
            if (source is AutocadTools.Models.AutoNumSettings numbering) numbering.Normalize();
            RefreshBindings(window);
        }

        internal static void Save(Window window)
        {
            var source = Source(window);
            if (source == null) return;
            var saved = new JObject();
            foreach (var property in Properties(source))
                saved[property.Name] = JToken.FromObject(property.GetValue(source) ?? "");
            UserPreferences.Write(window.GetType().Name, saved);
        }

        internal static void Attach(Window window)
        {
            bool ready = false;
            window.Loaded += (_, _) => { Restore(window); ready = true; };
            void Persist(object sender, RoutedEventArgs args)
            {
                if (!ready) return;
                window.Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() => Save(window)));
            }
            window.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(Persist), true);
            window.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((s, e) => Persist(s, e)), true);
            window.AddHandler(UIElement.LostFocusEvent, new RoutedEventHandler(Persist), true);
            window.Closed += (_, _) => { if (ready) Save(window); };
        }

        private static void RefreshBindings(DependencyObject element)
        {
            var values = element.GetLocalValueEnumerator();
            var properties = new List<DependencyProperty>();
            while (values.MoveNext()) properties.Add(values.Current.Property);
            foreach (var property in properties)
            {
                var expression = BindingOperations.GetBindingExpressionBase(element, property);
                if (expression is BindingExpression binding) binding.UpdateTarget();
                else if (expression is MultiBindingExpression multi) multi.UpdateTarget();
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) RefreshBindings(VisualTreeHelper.GetChild(element, i));
        }
    }
}
