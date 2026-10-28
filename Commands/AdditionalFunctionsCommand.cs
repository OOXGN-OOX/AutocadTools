// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using Autodesk.AutoCAD.Runtime;

namespace AutocadTools.Commands
{
    public sealed class AdditionalFunctionsCommand
    {
        [CommandMethod("ATLIBRARY", CommandFlags.Session)]
        [CommandMethod("АТБИБЛИОТЕКА", CommandFlags.Session)]
        public void Run() => System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle, new System.Action(UI.AdditionalFunctionsMenu.Show));
    }
}
