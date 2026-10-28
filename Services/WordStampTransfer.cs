// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.RegularExpressions;
using AutocadTools.Models;

namespace AutocadTools.Services
{
    internal static class WordStampTransfer
    {
        [DllImport("ole32.dll")] private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable table);
        [DllImport("ole32.dll")] private static extern int CreateBindCtx(int reserved, out IBindCtx context);
        [DllImport("oleaut32.dll", PreserveSig = false)] private static extern void GetActiveObject(ref Guid clsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object value);

        internal static List<StampTransferTarget> GetOpenDocuments()
        {
            var targets = new List<StampTransferTarget>();
            var identities = new HashSet<IntPtr>();
            void Add(object document)
            {
                var identity = Marshal.GetIUnknownForObject(document);
                bool added = false;
                try
                {
                    if (!identities.Add(identity)) return;
                    dynamic doc = document;
                    object docApplication = doc.Application;
                    try { if (!string.Equals((string)((dynamic)docApplication).Name, "Microsoft Word", StringComparison.OrdinalIgnoreCase)) return; }
                    finally { Release(docApplication); }
                    var target = new StampTransferTarget
                    {
                        Name = (string)doc.Name, Location = (string)doc.FullName,
                        Write = properties => Write(document, properties), Release = () => Release(document)
                    };
                    targets.Add(target); added = true;
                }
                catch (COMException) { }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                finally { Marshal.Release(identity); if (!added) Release(document); }
            }
            // The active Word instance also exposes unsaved documents which have no file moniker.
            object application = null, documents = null;
            try
            {
                var clsid = new Guid("000209FF-0000-0000-C000-000000000046");
                GetActiveObject(ref clsid, IntPtr.Zero, out application);
                documents = ((dynamic)application).Documents;
                for (int i = 1; i <= (int)((dynamic)documents).Count; i++) Add(((dynamic)documents).Item(i));
            }
            catch (COMException) { }
            finally { Release(documents); Release(application); }

            IRunningObjectTable table = null; IBindCtx context = null; IEnumMoniker enumerator = null;
            try
            {
                Marshal.ThrowExceptionForHR(GetRunningObjectTable(0, out table));
                Marshal.ThrowExceptionForHR(CreateBindCtx(0, out context));
                table.EnumRunning(out enumerator);
                var monikers = new IMoniker[1];
                while (enumerator.Next(1, monikers, IntPtr.Zero) == 0)
                {
                    try
                    {
                        monikers[0].GetDisplayName(context, null, out var name);
                        if (!Regex.IsMatch(name, @"\.(docx?|docm|dotx?|dotm)$", RegexOptions.IgnoreCase)) continue;
                        table.GetObject(monikers[0], out var document);
                        Add(document);
                    }
                    catch (COMException) { }
                    finally { Release(monikers[0]); }
                }
            }
            finally { Release(enumerator); Release(context); Release(table); }
            return targets;
        }

        internal static void Write(object document, StampProperties source)
        {
            dynamic doc = document;
            if ((bool)doc.ReadOnly) throw new InvalidOperationException("Документ Word открыт только для чтения.");
            if ((int)doc.ProtectionType != -1) throw new InvalidOperationException("Документ Word защищён от редактирования.");
            object properties = doc.CustomDocumentProperties;
            var before = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var changed = new List<string>();
            try
            {
                for (int i = 1; i <= (int)((dynamic)properties).Count; i++)
                {
                    object item = ((dynamic)properties).Item(i);
                    try { before[(string)((dynamic)item).Name] = ((dynamic)item).Value; }
                    finally { Release(item); }
                }
                foreach (var key in StampSchema.Keys)
                {
                    string value = source.GetValue(key);
                    if (value.Length > 255) throw new ArgumentException($"Поле «{key}» превышает ограничение Word в 255 символов.");
                    if (before.ContainsKey(key))
                    {
                        object item = ((dynamic)properties).Item(key);
                        try { ((dynamic)item).Value = value; }
                        finally { Release(item); }
                    }
                    else
                    {
                        object item = ((dynamic)properties).Add(key, false, 4, value);
                        Release(item);
                    }
                    changed.Add(key);
                }
            }
            catch
            {
                // Preserve the target when a property rejects the new value.
                foreach (var key in changed.AsEnumerable().Reverse())
                {
                    object item = null;
                    try
                    {
                        item = ((dynamic)properties).Item(key);
                        if (before.TryGetValue(key, out var oldValue)) ((dynamic)item).Value = oldValue;
                        else ((dynamic)item).Delete();
                    }
                    catch (COMException) { }
                    finally { Release(item); }
                }
                throw;
            }
            finally { Release(properties); }
            UpdatePropertyFields(document);
        }

        private static void UpdatePropertyFields(object document)
        {
            var keys = new HashSet<string>(StampSchema.Keys, StringComparer.OrdinalIgnoreCase);
            object stories = ((dynamic)document).StoryRanges;
            try
            {
                // Story types include body, headers, footers, notes and text frames.
                for (int storyType = 1; storyType <= 17; storyType++)
                {
                    object range;
                    try { range = ((dynamic)stories).Item(storyType); }
                    catch (COMException) { continue; }
                    while (range != null)
                    {
                        object next = null, fields = null;
                        try
                        {
                            fields = ((dynamic)range).Fields;
                            for (int i = 1; i <= (int)((dynamic)fields).Count; i++)
                            {
                                object field = ((dynamic)fields).Item(i), code = null;
                                try
                                {
                                    code = ((dynamic)field).Code;
                                    var match = Regex.Match((string)((dynamic)code).Text, "^\\s*DOCPROPERTY\\s+(?:\"([^\"]+)\"|(\\S+))", RegexOptions.IgnoreCase);
                                    string key = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                                    if (match.Success && keys.Contains(key)) ((dynamic)field).Update();
                                }
                                finally { Release(code); Release(field); }
                            }
                            next = ((dynamic)range).NextStoryRange;
                        }
                        finally { Release(fields); Release(range); }
                        range = next;
                    }
                }
            }
            finally { Release(stories); }
        }

        private static void Release(object value)
        {
            try { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
            catch (InvalidComObjectException) { }
        }
    }
}
