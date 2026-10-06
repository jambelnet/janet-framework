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
using System.Collections.Generic;
using System.Linq;

namespace jaNETProgram.Terminal;

/// <summary>Command line of jaNETProgram: options start with "--", everything else is a command to run at start-up.</summary>
sealed class Options
{
    public bool Headless { get; private set; }
    public bool Plain { get; private set; }
    public bool Help { get; private set; }
    public bool Version { get; private set; }

    /// <summary>The data folder named with --root, or null.</summary>
    public string? Root { get; private set; }
    public List<string> Commands { get; } = new List<string>();
    public List<string> Unknown { get; } = new List<string>();

    public static Options Parse(IEnumerable<string> args) {
        var options = new Options();

        string[] all = args.ToArray();

        for (int i = 0; i < all.Length; i++) {
            string arg = all[i];

            if (arg == "--root" || arg == "--home") {
                if (i + 1 < all.Length) options.Root = all[++i];
                else options.Unknown.Add(arg + " (needs a folder)");
                continue;
            }
            if (arg.StartsWith("--root=", StringComparison.Ordinal)) {
                options.Root = arg.Substring("--root=".Length);
                continue;
            }

            switch (arg) {
                case "--headless":
                case "--service":
                case "--daemon":
                    options.Headless = true;
                    break;
                case "--plain":
                case "--no-fancy":
                    options.Plain = true;
                    break;
                case "--help":
                case "-h":
                case "/?":
                    options.Help = true;
                    break;
                case "--version":
                    options.Version = true;
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal)) options.Unknown.Add(arg);
                    else options.Commands.Add(arg);
                    break;
            }
        }
        return options;
    }

    public const string Usage =
@"jaNETProgram [options] [command ...]

Starts jaNET Framework with its web server, socket server and scheduler and opens the console.
Every command given on the command line is executed once at start-up, e.g.
jaNETProgram ""judo schedule ls"" ""%checkin%""

Options:
  --root DIR   keep AppConfig.xml, the settings, the key and log.txt in DIR (same as JANET_HOME=DIR)
  --headless   run without a console (services, containers, nohup): stop with Ctrl+C or SIGTERM
  --plain      simple prompt and plain text output, no colors, tables or line editing
  --version    print the version and exit
  --help       print this text and exit

The console switches to plain text by itself when input or output is redirected, TERM is 'dumb',
or JANET_PLAIN=1 is set.

Where jaNET keeps its data: the folder of --root or JANET_HOME; else the folder that already has an
AppConfig.xml where older versions kept it; else a folder of its own (%LOCALAPPDATA%\jaNET on Windows,
/var/lib/janet for root and ~/.local/share/janet on Linux, ~/Library/Application Support/jaNET on a Mac).";
}
