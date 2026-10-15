// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(AutocadTools.LivePreview.PreviewCommands))]
[assembly: CommandClass(typeof(AutocadTools.LivePreview.PreviewCommands))]

namespace AutocadTools.LivePreview
{
    // This assembly stays loaded; each preview has its own identity and immutable directory.
    public class PreviewCommands : IExtensionApplication
    {
        private const string TabId = "AUTOCAD_TOOLS_PREVIEW";
        private static Assembly _assembly;
        private static string _loadedPath;
        private static string _announcedPath;
        private static DateTime _nextPoll;
        private static readonly Dictionary<Type, object> Instances = new Dictionary<Type, object>();
        private static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutocadTools", "Preview", Convert.ToString(AcadApp.GetSystemVariable("ACADVER")).StartsWith("26.") ? "2027" :
            Convert.ToString(AcadApp.GetSystemVariable("ACADVER")).StartsWith("25.0") ? "2025" : "2026");

        public void Initialize() { AcadApp.Idle += OnIdle; }
        public void Terminate()
        {
            AcadApp.Idle -= OnIdle;
            var tab = ComponentManager.Ribbon?.FindTab(TabId);
            if (tab != null) ComponentManager.Ribbon.Tabs.Remove(tab);
        }

        private static void Message(string message) => AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[Проверка] " + message + "\n");
        private static void OnIdle(object sender, EventArgs args)
        {
            if (DateTime.UtcNow < _nextPoll) return;
            _nextPoll = DateTime.UtcNow.AddSeconds(2);
            try
            {
                var manifest = ReadManifest();
                var ribbon = ComponentManager.Ribbon;
                if (ribbon != null && ribbon.FindTab(TabId) == null)
                {
                    var tab = new RibbonTab { Id = TabId, Title = "Проверка", Name = "Проверка" };
                    var source = new RibbonPanelSource { Title = "Autocad Tools — тестовая сборка" };
                    foreach (var item in Buttons)
                        source.Items.Add(new RibbonButton { Text = item.Label, ShowText = true, Size = RibbonItemSize.Large,
                            CommandHandler = new RunCommand(item.Command), ToolTip = "Тестовая команда: " + item.Command });
                    tab.Panels.Add(new RibbonPanel { Source = source });
                    ribbon.Tabs.Add(tab);
                    tab.IsActive = true;
                }
                if (manifest != null && manifest.AssemblyPath != _announcedPath)
                {
                    _announcedPath = manifest.AssemblyPath;
                    Message("Готова сборка " + manifest.Build + ". Команды на вкладке «Проверка» используют её при следующем запуске.");
                }
            }
            catch (System.Exception ex) { Message("Не удалось прочитать тестовую сборку: " + ex.Message); _nextPoll = DateTime.UtcNow.AddMinutes(1); }
        }

        private sealed class Manifest
        {
            public string AssemblyPath { get; set; }
            public string Build { get; set; }
        }
        private static Manifest ReadManifest()
        {
            var path = Path.Combine(DirectoryPath, "current.json");
            return File.Exists(path) ? JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path)) : null;
        }

        private static bool EnsureCurrent()
        {
            var manifest = ReadManifest() ?? throw new InvalidOperationException("Нет подготовленной тестовой сборки.");
            if (_loadedPath == manifest.AssemblyPath) return true;
            // Never discard edits from a modeless preview window while switching revisions.
            if (_assembly != null && System.Windows.Application.Current != null &&
                System.Windows.Application.Current.Windows.Cast<Window>().Any(w => w.IsLoaded && w.GetType().Assembly == _assembly))
            {
                Message("Закройте окно предыдущей тестовой сборки перед запуском новой. Изменения в полях сохранены в открытом окне.");
                return false;
            }
            var root = Path.GetFullPath(DirectoryPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(manifest.AssemblyPath);
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new InvalidOperationException("Путь сборки находится вне каталога проверки.");
            var name = AssemblyName.GetAssemblyName(path);
            if (!name.Name.StartsWith("AutocadTools.Preview.", StringComparison.Ordinal))
                throw new InvalidOperationException("У сборки отсутствует отдельное тестовое имя.");
            // Do not call ExtensionLoader or App.Initialize: production commands and background services stay registered once.
            var directory = Path.GetDirectoryName(path);
            Assembly Resolve(AssemblyLoadContext context, AssemblyName requested)
            {
                var shared = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => AssemblyName.ReferenceMatchesDefinition(a.GetName(), requested));
                if (shared != null) return shared;
                var dependency = Path.Combine(directory, requested.Name + ".dll");
                return File.Exists(dependency) ? context.LoadFromAssemblyPath(dependency) : null;
            }
            AssemblyLoadContext.Default.Resolving += Resolve;
            var loaded = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            Instances.Clear();
            _assembly = loaded;
            _loadedPath = path;
            File.WriteAllText(Path.Combine(DirectoryPath, "loaded.json"), JsonSerializer.Serialize(new { manifest.Build, AssemblyPath = path,
                ProcessId = Environment.ProcessId, LoadedUtc = DateTime.UtcNow }));
            Message("Загружена сборка " + manifest.Build + ".");
            return true;
        }

        private static void Invoke(string command)
        {
            try
            {
                if (!EnsureCurrent()) return;
                var methods = _assembly.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
                var method = methods.Single(m => m.GetCustomAttributesData().Any(a =>
                    (a.AttributeType == typeof(CommandMethodAttribute) || a.AttributeType.FullName == "AutocadTools.LivePreview.CommandEntryAttribute") &&
                    a.ConstructorArguments.Count > 0 && string.Equals(a.ConstructorArguments[0].Value as string, command, StringComparison.OrdinalIgnoreCase)));
                object instance = null;
                if (!method.IsStatic && !Instances.TryGetValue(method.DeclaringType, out instance))
                    Instances[method.DeclaringType] = instance = Activator.CreateInstance(method.DeclaringType);
                method.Invoke(instance, null);
            }
            catch (System.Exception ex) { Message((ex is TargetInvocationException ? ex.InnerException : ex)?.ToString()); }
        }

        [CommandMethod("AT_TEST_MULTICOPY", CommandFlags.UsePickSet | CommandFlags.Modal)] public void MultiCopy() => Invoke("ATMCOPY");
        [CommandMethod("AT_TEST_STAMP", CommandFlags.Session)] public void Stamp() => Invoke("ATSTAMP");
        [CommandMethod("AT_TEST_FORMAT", CommandFlags.Modal)] public void Format() => Invoke("ATFRAME");
        [CommandMethod("AT_TEST_ALIGN", CommandFlags.UsePickSet | CommandFlags.Modal)] public void Align() => Invoke("ATISO");
        [CommandMethod("AT_TEST_INFO")] public void Info() { if (EnsureCurrent()) Message(_loadedPath); }

        private static readonly (string Label, string Command)[] Buttons = {
            ("Мультикопирование", "AT_TEST_MULTICOPY"), ("Свойства штампа", "AT_TEST_STAMP"),
            ("Вставить рамку", "AT_TEST_FORMAT"), ("Аксонометрия", "AT_TEST_ALIGN"), ("Номер сборки", "AT_TEST_INFO") };
        private sealed class RunCommand : ICommand
        {
            private readonly string _name;
            public RunCommand(string name) { _name = name; }
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object parameter) => AcadApp.DocumentManager.MdiActiveDocument != null;
            public void Execute(object parameter) => AcadApp.DocumentManager.MdiActiveDocument?.SendStringToExecute(_name + " ", true, false, false);
        }
    }
}
