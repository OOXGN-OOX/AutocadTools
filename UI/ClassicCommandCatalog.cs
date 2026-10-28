// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System.Linq;
namespace AutocadTools.UI
{
    internal sealed record ButtonDefinition(string Title, string Icon, string Command, string Description);
    internal sealed record PanelDefinition(string Title, ButtonDefinition[] Buttons);
    internal static class ClassicCommandCatalog
    {
        internal const string TabTitle = "Autocad Tools";
        internal static readonly PanelDefinition[] Panels = { new("Инструменты", Services.CommandCatalog.All.Select(c => new ButtonDefinition(c.Title, c.English, c.English, c.ToolTip)).ToArray()) };
    }
}
