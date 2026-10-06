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
using System.IO;
using System.Linq;

namespace jaNET.Infrastructure;

/// <summary>What <see cref="AppPaths.Locate"/> needs to know about the machine; a record so that every system can be tested on any system.</summary>
internal sealed record PathContext(
    bool IsWindows, bool IsMac, bool IsAdministrator,
    string CurrentDirectory, string ProgramDirectory, string HomeDirectory,
    Func<string, string?> Environment, Func<string, bool> FileExists)
{
    public static PathContext ThisMachine() => new(
        OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(),
        !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && System.Environment.UserName == "root",
        Directory.GetCurrentDirectory(), AppContext.BaseDirectory,
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
        System.Environment.GetEnvironmentVariable, File.Exists);
}

/// <summary>
/// Where jaNET keeps its data (AppConfig.xml, the settings files, the key, log.txt) and where it finds the web UI.
/// The data folder is, in this order: the one named by <c>JANET_HOME</c> or <c>--root</c>; the folder that already has an AppConfig.xml
/// where older versions kept it (the current directory on Windows, the program directory elsewhere), so that nobody's setup moves;
/// otherwise a folder of its own that is not the program's: <c>%LOCALAPPDATA%\jaNET</c>, <c>/var/lib/janet</c> for root, or
/// <c>~/.local/share/janet</c> (<c>~/Library/Application Support/jaNET</c> on a Mac). The web UI is the <c>www</c> folder in the data
/// folder if there is one (to change the UI), otherwise the one next to the program.
/// </summary>
internal sealed class AppPaths
{
    public const string ConfigFileName = "AppConfig.xml";

    readonly string _root;
    readonly string _programDirectory;

    public AppPaths() : this(Locate(PathContext.ThisMachine()), AppContext.BaseDirectory) { }

    /// <summary>For tests and embedding: use a fixed directory.</summary>
    public AppPaths(string root) : this(root, AppContext.BaseDirectory) { }

    public AppPaths(string root, string programDirectory) {
        _root = EndWithSeparator(Path.GetFullPath(root));
        _programDirectory = EndWithSeparator(Path.GetFullPath(programDirectory));
    }

    /// <summary>The data folder including a trailing directory separator.</summary>
    public string Root => _root;

    /// <summary>Full path of a file inside the data folder.</summary>
    public string File(string fileName) => _root + fileName;

    /// <summary>Full path of a file that belongs to the program and not to the data (a helper such as jspeech.exe).</summary>
    public string ProgramFile(string fileName) {
        string inProgram = _programDirectory + fileName;
        return System.IO.File.Exists(inProgram) || !System.IO.File.Exists(File(fileName)) ? inProgram : File(fileName);
    }

    public string ConfigFile => File(ConfigFileName);

    /// <summary>Every folder that may be served as /www/: the one in the data folder and the one next to the program.</summary>
    public IReadOnlyList<string> WebRoots => new[] { EndWithSeparator(_root + "www"), EndWithSeparator(_programDirectory + "www") };

    /// <summary>The folder the web server serves under /www/, with a trailing separator.</summary>
    public string WebRoot => EndWithSeparator(Directory.Exists(_root + "www") ? _root + "www" : _programDirectory + "www");

    /// <summary>
    /// The file for a decoded request path: "www/..." is in <see cref="WebRoot"/>, anything else is looked for in the data folder
    /// (where it is then refused, only the web UI is served).
    /// </summary>
    public string MapRequest(string decodedPath) {
        // the request's own "/" stays as it is: the web server tells a folder ("www/") from a file by it
        string web = WebRoot.TrimEnd(Path.DirectorySeparatorChar);
        if (decodedPath == "www") return web;
        if (decodedPath.StartsWith("www/", StringComparison.Ordinal)) return web + decodedPath.Substring(3);
        return _root + decodedPath;
    }

    public static string Locate(PathContext machine) {
        string? chosen = machine.Environment("JANET_HOME");
        if (!string.IsNullOrWhiteSpace(chosen)) return System.IO.Path.GetFullPath(chosen);

        string legacy = machine.IsWindows ? machine.CurrentDirectory : machine.ProgramDirectory;
        if (machine.FileExists(Path.Combine(legacy, ConfigFileName))) return legacy;

        string? own = machine.IsWindows ? Join(machine.Environment("LOCALAPPDATA"), "jaNET")
            : machine.IsMac ? Join(machine.HomeDirectory, "Library", "Application Support", "jaNET")
            : machine.IsAdministrator ? "/var/lib/janet"
            : Join(machine.Environment("XDG_DATA_HOME") is { Length: > 0 } xdg ? xdg : Join(machine.HomeDirectory, ".local", "share"), "janet");

        return own ?? legacy;       // a service account without a profile: keep the old place
    }

    static string? Join(string? first, params string[] rest) =>
        string.IsNullOrWhiteSpace(first) ? null : Path.Combine(new[] { first }.Concat(rest).ToArray());

    static string EndWithSeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
}
