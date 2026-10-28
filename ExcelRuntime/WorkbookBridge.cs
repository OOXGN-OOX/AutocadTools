// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System.Linq;
using AutocadTools.Services;
namespace AutocadTools.ExcelRuntime
{
    // Only runtime primitive types cross the isolated assembly load context.
    public static class WorkbookBridge
    {
        public static void Save(string path, int rows, int columns, string[,] values, int[][] merges) =>
            ExcelWorkbookService.Save(path, new TableWorkbookData(rows, columns, values,
                merges.Select(m => new ExcelMerge(m[0], m[1], m[2], m[3])).ToList()));
        public static object[] Read(string path, bool allowFirstWorksheet)
        {
            var data = ExcelWorkbookService.Read(path, allowFirstWorksheet);
            return new object[] { data.Rows, data.Columns, data.Values,
                data.Merges.Select(m => new[] { m.TopRow, m.LeftCol, m.BottomRow, m.RightCol }).ToArray() };
        }
    }
}
