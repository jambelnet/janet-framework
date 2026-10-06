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

namespace jaNET.Hosting;

/// <summary>How much a <see cref="HostNotice"/> matters to the person running jaNET.</summary>
public enum NoticeLevel
{
    /// <summary>Good to know, nothing is wrong (e.g. no Arduino is plugged in).</summary>
    Info,

    /// <summary>jaNET works but something is unsafe or unusual (e.g. a web server without a password).</summary>
    Warning,

    /// <summary>Something the user asked for does not work (e.g. the web server could not start).</summary>
    Error
}

/// <summary>
/// Something a person running jaNET should see right away. <see cref="JanetHost.Notices"/> lists the ones that apply now; a notice
/// disappears by itself when its cause is gone, so front ends can show every notice once and again when it comes back.
/// </summary>
/// <param name="Source">What it is about: "Web server", "Socket server", "Serial port", "Configuration", "Settings" or "Security".</param>
/// <param name="Level">How serious it is.</param>
/// <param name="Message">The reason and, where there is one, what to do about it. One or two sentences, plain words.</param>
public sealed record HostNotice(string Source, NoticeLevel Level, string Message);

/// <summary>Why a service is not running, as the service itself found out. Null on a service means "nothing to report".</summary>
internal sealed record ServiceProblem(NoticeLevel Level, string Message);
