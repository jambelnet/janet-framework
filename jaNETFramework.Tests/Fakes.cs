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

using jaNET.Configuration;
using jaNET.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace jaNETFramework.Tests;

/// <summary>Collects log entries in memory.</summary>
internal sealed class MemoryLog : ILog
{
    public List<string> Entries { get; } = new List<string>();

    public void Write(string message) {
        lock (Entries) Entries.Add(message);
    }
}

/// <summary>Settings files kept in memory.</summary>
internal sealed class MemorySettingsStore : ISettingsStore
{
    readonly Dictionary<string, string[]> _files = new Dictionary<string, string[]>();

    public bool Exists(string fileName) => _files.ContainsKey(fileName);

    public int MigrateLegacyFiles() => 0;

    public IReadOnlyList<string> Load(string fileName) => _files.TryGetValue(fileName, out string[] lines) ? lines : null;

    public string Save(string fileName, string text) {
        _files[fileName] = text.Split(new[] { Environment.NewLine, "\r\n", "\n" }, StringSplitOptions.None).Select(line => line.Trim()).ToArray();
        return "Settings saved.";
    }
}

/// <summary>A clock that shows the time it is told to.</summary>
internal sealed class FakeClock : IClock
{
    public DateTime Now { get; set; } = new DateTime(2026, 10, 1, 8, 30, 0);
}

/// <summary>Remembers what it was asked to say instead of speaking.</summary>
internal sealed class SilentSpeaker : jaNET.Services.ISpeaker
{
    public bool Muted { get; set; }

    public List<string> Spoken { get; } = new List<string>();

    public void Say(string text) {
        lock (Spoken) Spoken.Add(text);
    }
}
