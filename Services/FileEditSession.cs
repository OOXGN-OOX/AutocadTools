// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;

namespace AutocadTools.Services
{
    /// <summary>Waits for an editor to finish saving, including editors without file locks.</summary>
    internal sealed class FileEditSession
    {
        private bool _seenLocked, _changed;
        private DateTime _lastWriteUtc, _changedAtUtc;
        private long _length;
        public DateTime CreatedUtc { get; }

        public FileEditSession(DateTime now, DateTime lastWriteUtc, long length)
        {
            CreatedUtc = now;
            _lastWriteUtc = lastWriteUtc;
            _length = length;
        }

        public bool IsReady(DateTime now, bool locked, DateTime lastWriteUtc, long length)
        {
            if (locked) _seenLocked = true;
            if (lastWriteUtc != _lastWriteUtc || length != _length)
            {
                _lastWriteUtc = lastWriteUtc;
                _length = length;
                _changed = true;
                _changedAtUtc = now;
            }
            if (locked || now - CreatedUtc < TimeSpan.FromSeconds(2)) return false;
            // Editors can release their lock briefly during an atomic replacement save.
            return (_seenLocked || _changed) && (!_changed || now - _changedAtUtc >= TimeSpan.FromSeconds(2));
        }
    }
}
