/* *****************************************************************************************************************************
 * (c) J@mBeL.net 2010-2017
 * Author: John Ambeliotis
 * Created: 24 Apr. 2010
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
    along with jaNET Framework. If not, see <http://www.gnu.org/licenses/>.

 * Stable Release Dates
 * Version: 0.1.0           24 Apr 2010
 * Version: 0.1.1           21 Nov 2010
 * Version: 0.1.2           28 Nov 2010
 * Version: 0.1.3           26 Dec 2010
 * Version: 0.1.4           20 Feb 2011
 * Version: 0.1.5           12 Jun 2011
 * Version: 0.1.6           30 Oct 2011
 * Version: 0.1.7           15 Apr 2012
 * Version: 0.1.8           16 Aug 2012
 * Version: 0.1.9           10 Feb 2013
 * Version: 0.2.0           26 Jan 2014
 * Version: 0.2.1+0.2.2     23 Mar 2014
 * Version: 0.2.3           07 May 2014
 * Version: 0.2.4           26 May 2014
 * Version: 0.2.4.1         31 Oct 2014
 * Version: 0.2.5           13 Jan 2015
 * Version: 0.2.6           04 Mar 2015
 * Version: 0.2.7           15 Mar 2015
 * Version: 0.2.8           20 Apr 2015
 * Version: 0.2.9           29 Oct 2015
 * Version: 0.3.9           17 Apr 2017
 * Version: 0.3.1.92        30 Aug 2018
 * Version: 1.0.0-rc.1      06 Oct 2026    (see CHANGELOG.md; the version is set in Directory.Build.props)
 * ****************************************************************************************************************************/

using jaNET.Hosting;
using jaNETProgram.Terminal;
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace jaNETProgram;

class Program
{
    public static int Main(string[] args) {
        Options options = Options.Parse(args);

        if (options.Help || options.Unknown.Count > 0) {
            if (options.Unknown.Count > 0)
                Console.Error.WriteLine("Unknown option(s): " + string.Join(" ", options.Unknown) + Environment.NewLine);
            Console.WriteLine(Options.Usage);
            return options.Unknown.Count > 0 ? 2 : 0;
        }

        if (options.Version) {
            Console.WriteLine(new JanetHost().Version);
            return 0;
        }

        var host = new JanetHost(new JanetHostOptions { RootDirectory = options.Root });

        if (options.Headless)
            return Guarded(() => Headless.Run(host, options.Commands));

        IUi ui = UseFancyConsole(options) ? (IUi)new FancyUi(host) : new PlainUi(host);

        ui.ShowBanner();
        if (Guarded(host.Start) != 0)
            return 1;
        ui.ShowServices();

        options.Commands.ForEach(a => host.Execute(a));

        while (host.IsRunning) {
            try {
                string? cmdReader = ui.ReadCommand();

                if (cmdReader == null) // standard input was closed
                    break;

                if (cmdReader.Length > 0)
                    ui.Run(cmdReader);
            }
            catch (ArgumentOutOfRangeException e) {
                Debug.Print(e.Message);
            }
        }

        ui.ShowGoodbye();
        host.Dispose();
        return 0;
    }

    // Start fails when jaNET cannot use its folder (read-only, no rights) or a key is unusable. Say so in words instead of a stack trace.
    static int Guarded(Action start) => Guarded(() => { start(); return 0; });

    static int Guarded(Func<int> run) {
        try {
            return run();
        }
        catch (Exception e) when (e is UnauthorizedAccessException || e is System.IO.IOException || e is InvalidOperationException) {
            Console.Error.WriteLine("jaNET cannot start: " + e.Message);
            Console.Error.WriteLine("It keeps AppConfig.xml, its settings and log.txt in " + (OperatingSystem.IsWindows() ? "the folder it is started from" : "its program folder") +
                                    " and must be allowed to write there. Start it from a folder you can write to.");
            return 1;
        }
    }

    // The fancy console needs a real terminal on both sides; everything else gets the classic text console.
    static bool UseFancyConsole(Options options) {
        if (options.Plain) return false;
        // JANET_FANCY=1 is a testing aid: force the fancy console even though input/output are redirected
        bool forced = Environment.GetEnvironmentVariable("JANET_FANCY") == "1";
        if (!forced && (Console.IsInputRedirected || Console.IsOutputRedirected || Console.IsErrorRedirected)) return false;
        if (Environment.GetEnvironmentVariable("JANET_PLAIN") == "1") return false;
        if (string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase)) return false;

        try { Console.OutputEncoding = Encoding.UTF8; } catch { }

        try {
            return forced || Console.WindowWidth > 0;
        }
        catch {
            return false;
        }
    }
}
