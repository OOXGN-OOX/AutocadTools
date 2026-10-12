// Copyright (c) 2026 OOXGN (GitHub: OOXGN-OOX). All rights reserved.
// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// See LICENSE in the repository root.
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;

// Core.Application, чтобы не ловить неоднозначность
using AcadCoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutocadTools.Services
{
    public static class CopyProtectionService
    {
        private static readonly HashSet<string> _blockedCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "COPY",
            "COPYCLIP",
            "CUTCLIP",
            "PASTECLIP",
            "PASTEBLOCK",
            "WBLOCK",
            "EXPORT",
            "EXPORTTOAUTOCAD"
        };

        private static readonly Dictionary<Document, CommandEventHandler> _handlers = new();
        private static bool _docDestroyHooked;

        public static void Enable(Document doc, string message = "OOXGN · Autocad Tools: копирование запрещено")
        {
            if (doc == null) return;
            if (_handlers.ContainsKey(doc)) return;

            if (!_docDestroyHooked)
            {
                _docDestroyHooked = true;
                AcadCoreApp.DocumentManager.DocumentToBeDestroyed += OnDocToBeDestroyed;
            }

            bool reenterGuard = false;

            CommandEventHandler handler = (s, e) =>
            {
                try
                {
                    if (reenterGuard) return;

                    var cmd = e?.GlobalCommandName ?? string.Empty;
                    if (!_blockedCommands.Contains(cmd)) return;

                    reenterGuard = true;

                    var d = (Document)s;
                    d?.Editor.WriteMessage($"\n{message}\n");

                    // Отмена команды: ESC ESC
                    d?.SendStringToExecute("\x03\x03", true, false, false);
                }
                catch
                {
                    // намеренно проглатываем, чтобы обработчик не “сломался”
                }
                finally
                {
                    reenterGuard = false;
                }
            };

            doc.CommandWillStart += handler;
            _handlers[doc] = handler;
        }

        public static void Disable(Document doc)
        {
            if (doc == null) return;
            if (!_handlers.TryGetValue(doc, out var handler)) return;

            try { doc.CommandWillStart -= handler; } catch { /* ignore */ }
            _handlers.Remove(doc);
        }

        private static void OnDocToBeDestroyed(object sender, DocumentCollectionEventArgs e)
        {
            try
            {
                var doc = e?.Document;
                if (doc != null) Disable(doc);
            }
            catch
            {
                // ignore
            }
        }
    }
}
