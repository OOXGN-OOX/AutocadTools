// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace AutocadTools.Services
{
    internal readonly record struct ExcelMerge(int TopRow, int LeftCol, int BottomRow, int RightCol);
    internal sealed record TableWorkbookData(int Rows, int Columns, string[,] Values, List<ExcelMerge> Merges);

    internal static class ExcelWorkbookService
    {
        private static readonly Lazy<RuntimeBridge> Bridge = new(() => new RuntimeBridge());
        internal static void Save(string path, TableWorkbookData data) => Bridge.Value.Save(path, data.Rows, data.Columns, data.Values,
            data.Merges.Select(m => new[] { m.TopRow, m.LeftCol, m.BottomRow, m.RightCol }).ToArray());
        internal static TableWorkbookData Read(string path, bool allowFirstWorksheet = false)
        {
            object[] data = Bridge.Value.Read(path, allowFirstWorksheet);
            return new TableWorkbookData((int)data[0], (int)data[1], (string[,])data[2],
                ((int[][])data[3]).Select(m => new ExcelMerge(m[0], m[1], m[2], m[3])).ToList());
        }
        private sealed class RuntimeBridge : AssemblyLoadContext
        {
            private readonly string _directory;
            internal readonly Action<string, int, int, string[,], int[][]> Save;
            internal readonly Func<string, bool, object[]> Read;
            internal RuntimeBridge() : base("AutocadTools.Excel", isCollectible: false)
            {
                _directory = Path.Combine(Path.GetDirectoryName(typeof(ExcelWorkbookService).Assembly.Location), "ExcelRuntime");
                string path = Path.Combine(_directory, "AutocadTools.ExcelRuntime.dll");
                if (!File.Exists(path)) throw new FileNotFoundException("Не найден модуль Excel. Переустановите плагин полностью.", path);
                var type = LoadFromAssemblyPath(path).GetType("AutocadTools.ExcelRuntime.WorkbookBridge", true);
                Save = type.GetMethod("Save").CreateDelegate<Action<string, int, int, string[,], int[][]>>();
                Read = type.GetMethod("Read").CreateDelegate<Func<string, bool, object[]>>();
            }
            protected override Assembly Load(AssemblyName name)
            {
                string path = Path.Combine(_directory, name.Name + ".dll");
                return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
            }
        }
    }
}
