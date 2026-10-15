// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

// Command attributes are generated from the production handlers by Start-Preview.ps1.
// The companion can be loaded without replacing the bootstrap already in memory.
public sealed partial class CommandPreview
{
    private static Assembly _assembly;
    private static readonly Dictionary<Type, object> Instances = new Dictionary<Type, object>();
    private static void Invoke(string name)
    {
        var ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
        if (ed == null) return;
        var bootstrap = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "AutocadTools.LivePreview");
        var type = bootstrap?.GetType("AutocadTools.LivePreview.PreviewCommands");
        var ensure = type?.GetMethod("EnsureCurrent", BindingFlags.Static | BindingFlags.NonPublic);
        if (ensure == null) { ed.WriteMessage("\nСначала загрузите AutocadTools.LivePreview.dll через NETLOAD."); return; }
        try
        {
            if (!(bool)ensure.Invoke(null, null)) return;
            var assembly = (Assembly)type.GetField("_assembly", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            if (_assembly != assembly) { Instances.Clear(); _assembly = assembly; }
            var method = assembly.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                .Single(m => m.GetCustomAttributesData().Any(a => a.AttributeType.FullName == "AutocadTools.LivePreview.CommandEntryAttribute" &&
                    a.ConstructorArguments.Count > 0 && string.Equals(a.ConstructorArguments[0].Value as string, name, StringComparison.OrdinalIgnoreCase)));
            object instance = null;
            if (!method.IsStatic && !Instances.TryGetValue(method.DeclaringType, out instance))
                Instances[method.DeclaringType] = instance = Activator.CreateInstance(method.DeclaringType);
            ed.WriteMessage("\n[Проверка] " + name);
            method.Invoke(instance, null);
        }
        catch (System.Exception ex) { ed.WriteMessage("\nОшибка тестовой команды: " + (ex.InnerException ?? ex).Message); }
    }
}
