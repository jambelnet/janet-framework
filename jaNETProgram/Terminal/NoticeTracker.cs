/* *****************************************************************************************************************************
 * (c) J@mBeL.net 2010-2026
 * Author: John Ambeliotis
 *
 * License:
 *  This file is part of jaNET Framework.

    jaNET Framework is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    jaNET Framework is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with jaNET Framework. If not, see <http://www.gnu.org/licenses/>. */

using jaNET.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace jaNETProgram.Terminal;

/// <summary>
/// Decides which notices of the host are new to the person at the console: each one is shown once, shown again if it went away and
/// came back, and not at all when the answer to the command they just typed already says it.
/// </summary>
sealed class NoticeTracker
{
    readonly JanetHost _host;
    HashSet<string> _shown = new();

    public NoticeTracker(JanetHost host) {
        _host = host;
    }

    /// <param name="answer">The answer the user has just read; notices it already contains are marked as shown but not returned.</param>
    public IReadOnlyList<HostNotice> TakeNew(string answer = "") {
        IReadOnlyList<HostNotice> current = _host.Notices;
        HashSet<string> before = _shown;
        _shown = current.Select(Key).ToHashSet();

        return current
            .Where(n => !before.Contains(Key(n)))
            .Where(n => answer.Length == 0 || !answer.Contains(n.Message, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>Does the host report something about this source at this level or worse?</summary>
    public bool Has(string source, NoticeLevel atLeast = NoticeLevel.Warning) =>
        _host.Notices.Any(n => n.Source == source && n.Level >= atLeast);

    static string Key(HostNotice notice) => notice.Source + "\n" + notice.Message;
}
