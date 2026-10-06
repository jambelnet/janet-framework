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

using System;
using System.IO;

namespace jaNET.Infrastructure;

/// <summary>Diagnostic log of things that went wrong while running (never throws).</summary>
internal interface ILog
{
    void Write(string message);
}

/// <summary>Appends entries to log.txt in the application directory.</summary>
internal sealed class FileLog : ILog
{
    static readonly object Gate = new();

    readonly AppPaths _paths;
    readonly IClock _clock;

    public FileLog(AppPaths paths, IClock clock) {
        _paths = paths;
        _clock = clock;
    }

    public void Write(string message) {
        try {
            DateTime now = _clock.Now;

            lock (Gate) {
                using var writer = File.AppendText(_paths.File("log.txt"));
                writer.Write("\r\nLog Entry @ ");
                writer.WriteLine("{0} {1}", now.ToLongTimeString(), now.ToLongDateString());
                writer.WriteLine("  :{0}", message);
                writer.WriteLine("--------------------------------------------------------------------------------------------------");
            }
        }
        catch (IOException) {
            // logging must never break the thing that is being logged
        }
        catch (UnauthorizedAccessException) {
        }
    }
}
