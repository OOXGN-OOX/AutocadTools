// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using Autodesk.AutoCAD.Runtime;
namespace AutocadTools.Commands
{
    public sealed class ClassicUiCommand
    {
        [CommandMethod("ATCLASSIC", CommandFlags.Modal)]
        [CommandMethod("АТМЕНЮ", CommandFlags.Modal)]
        public void Execute() => App.ShowClassicUi();
    }
}
