// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
#if LIVE_PREVIEW
global using CommandMethodAttribute = AutocadTools.LivePreview.CommandEntryAttribute;

namespace AutocadTools.LivePreview
{
    // Only the stable dispatcher registers native commands; revision DLLs carry metadata.
    [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
    public sealed class CommandEntryAttribute : System.Attribute
    {
        public string GlobalName { get; }
        public Autodesk.AutoCAD.Runtime.CommandFlags Flags { get; }
        public CommandEntryAttribute(string name, Autodesk.AutoCAD.Runtime.CommandFlags flags = Autodesk.AutoCAD.Runtime.CommandFlags.Modal)
        { GlobalName = name; Flags = flags; }
    }
}
#endif
