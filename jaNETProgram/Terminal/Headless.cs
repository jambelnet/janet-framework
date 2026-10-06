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
using System.Runtime.InteropServices;
using System.Threading;

namespace jaNETProgram.Terminal;

/// <summary>
/// Runs the services without reading from the console, for systemd units, containers, nohup and Windows services.
/// Stops on Ctrl+C, SIGTERM (systemd, docker stop) or the %exit% function (also through the web or socket API).
/// </summary>
static class Headless
{
    public static int Run(JanetHost host, IEnumerable<string> commands) {
        Console.WriteLine("jaNET Framework [Version " + host.Version + "] running without console. Stop with Ctrl+C or SIGTERM.");
        Console.WriteLine("Data folder: " + host.DataDirectory);

        host.Start();
        foreach (string command in commands)
            host.Execute(command);

        var notices = new NoticeTracker(host);
        ShowNotices(notices);

        using (var stop = new ManualResetEventSlim())
        using (PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; stop.Set(); }))
        using (PosixSignalRegistration.Create(PosixSignal.SIGHUP, context => { context.Cancel = true; }))   // keep running when the terminal closes
        {
            Console.CancelKeyPress += (sender, e) => { e.Cancel = true; stop.Set(); };

            // every few seconds: something that is changed through the web or the socket can need attention too
            for (int tick = 1; host.IsRunning && !stop.Wait(500); tick++)
                if (tick % 10 == 0) ShowNotices(notices);
        }

        Console.WriteLine("Stopping...");
        host.Dispose();   // stops the servers
        return 0;
    }

    static void ShowNotices(NoticeTracker notices) {
        foreach (HostNotice notice in notices.TakeNew())
            if (notice.Level >= NoticeLevel.Warning)
                Console.WriteLine($"{notice.Level}: {notice.Source}: {notice.Message}");
    }
}
