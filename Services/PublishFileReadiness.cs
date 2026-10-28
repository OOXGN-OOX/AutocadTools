// SPDX-License-Identifier: AGPL-3.0-only
// Derived from Davud77/BotPlusAutocadTool (51e3e82). See THIRD-PARTY-NOTICES.md.
using System;

namespace AutocadTools.Services
{
    /// <summary>A writer releasing a file briefly does not mean publication has finished.</summary>
    internal sealed class PublishFileReadiness
    {
        private long _length;
        private DateTime _writeUtc, _stableSince;
        internal bool Observe(DateTime now, long length, DateTime writeUtc, bool available)
        {
            if (!available || length <= 0) { Reset(); return false; }
            if (_stableSince == default || length != _length || writeUtc != _writeUtc)
            {
                _length = length; _writeUtc = writeUtc; _stableSince = now;
                return false;
            }
            return now - _stableSince >= TimeSpan.FromSeconds(2);
        }
        internal void Reset() => _stableSince = default;
    }
}
