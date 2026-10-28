// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
namespace AutocadTools.Commands
{
    public sealed class SettingsCommand
    {
        [CommandMethod("ATSETTINGS", CommandFlags.Modal)]
        [CommandMethod("АТНАСТРОЙКИ", CommandFlags.Modal)]
        public void Run() => AcadApp.ShowModalWindow(new Views.SettingsWindow());
    }
}
