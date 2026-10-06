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

namespace jaNETProgram.Terminal;

/// <summary>
/// The console exactly as it always was: copyright text, "user@jaNET>" prompt, results below. Used when input or
/// output is redirected (pipes, services, scripts), when TERM is dumb, or with --plain.
/// </summary>
sealed class PlainUi : IUi
{
    readonly JanetHost _host;
    readonly NoticeTracker _notices;
    readonly bool _colors;

    public PlainUi(JanetHost host) {
        _host = host;
        _notices = new NoticeTracker(host);

        // do not leak colour codes into a file or pipe that receives our output
        _colors = !Console.IsOutputRedirected;
    }

    public void ShowBanner() {
        Console.Write(_host.Execute("%copyright%") + "\r\n");
    }

    public void ShowServices() => ShowNotices(string.Empty);

    // only what needs attention (warnings and errors); "nothing is plugged in" style information would be noise in a log or a pipe
    void ShowNotices(string answer) {
        foreach (HostNotice notice in _notices.TakeNew(answer)) {
            if (notice.Level < NoticeLevel.Warning) continue;

            if (_colors) Console.ForegroundColor = notice.Level == NoticeLevel.Error ? ConsoleColor.Red : ConsoleColor.DarkYellow;
            Console.WriteLine($"{notice.Level}: {notice.Source}: {notice.Message}");
            if (_colors) Console.ResetColor();
        }
    }

    public string? ReadCommand() {
        Console.Write(Environment.NewLine + _host.UserName + "@jaNET>");
        if (_colors) Console.ForegroundColor = ConsoleColor.Green;

        string? line = Console.ReadLine();

        if (_colors) Console.ResetColor();

        // a file piped in may start with a byte order mark; it is not part of the command
        return line?.TrimStart((char)0xFEFF);
    }

    public void Run(string command) {
        if (_colors) Console.ForegroundColor = ConsoleColor.Yellow;
        string output = _host.Execute(command);
        Console.WriteLine(output);
        if (_colors) Console.ResetColor();

        ShowNotices(output);
    }

    public void ShowGoodbye() { }
}
