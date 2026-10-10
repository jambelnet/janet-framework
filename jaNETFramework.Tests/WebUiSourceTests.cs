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
using System.Linq;
using System.Text;
using Xunit;

namespace jaNETFramework.Tests;

/// <summary>Guards for the files of the web UI: damaged text must not reach the browser.</summary>
public class WebUiSourceTests
{
    static string WwwFolder() {
        for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent) {
            if (File.Exists(Path.Combine(dir.FullName, "jaNETFramework.sln")))
                return Path.Combine(dir.FullName, "www");
        }
        return null;
    }

    static string[] UiFiles() {
        string www = WwwFolder();
        if (www == null || !Directory.Exists(www)) return Array.Empty<string>();

        return Directory.GetFiles(www, "*.*", SearchOption.AllDirectories)
            .Where(f => new[] { ".js", ".css", ".html", ".webmanifest" }.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToArray();
    }

    [Fact]
    public void ThereAreUiFilesToCheck() {
        Assert.NotEmpty(UiFiles());
    }

    [Fact]
    public void NoFileContainsDoubleEncodedCharacters() {
        // "°" saved as UTF-8, read as Windows-1252 and saved again becomes "Â°": the temperature then shows as 19.5Â°C
        char[] telltale = { (char)0xC2, (char)0xC3 };

        foreach (string file in UiFiles()) {
            string text = File.ReadAllText(file, Encoding.UTF8);
            Assert.False(text.IndexOfAny(telltale) >= 0, Path.GetFileName(file) + " contains characters that were encoded twice");
        }
    }

    [Fact]
    public void TheSettingsGroupsAreExclusiveAndStartCollapsed() {
        string index = Path.Combine(WwwFolder() ?? string.Empty, "index.html");
        if (!File.Exists(index)) return;

        string html = File.ReadAllText(index, Encoding.UTF8);

        foreach (string id in new[] { "instructions-group", "scheduler-group", "settings-group", "about-group" })
            Assert.Contains("<details class=\"group\" id=\"" + id + "\" name=\"settings-sections\">", html);
    }

    [Fact]
    public void TheQuickActionsStayInOneRow() {
        string css = Path.Combine(WwwFolder() ?? string.Empty, "css", "app.css");
        if (!File.Exists(css)) return;

        string text = File.ReadAllText(css, Encoding.UTF8);
        int start = text.IndexOf(".actions {", StringComparison.Ordinal);
        string rule = text.Substring(start, text.IndexOf('}', start) - start);

        Assert.Contains("grid-auto-flow: column", rule);       // an auto-fit grid wraps the fourth button on narrow phones
        Assert.DoesNotContain("auto-fit", rule);
    }

    [Fact]
    public void NoFileStartsWithAByteOrderMark() {
        foreach (string file in UiFiles()) {
            byte[] start = File.ReadAllBytes(file).Take(3).ToArray();
            Assert.False(start.SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), Path.GetFileName(file) + " starts with a byte order mark");
        }
    }

    [Fact]
    public void NoFileIsInvalidUtf8() {
        var strict = new UTF8Encoding(false, true);

        foreach (string file in UiFiles())
            strict.GetString(File.ReadAllBytes(file));          // throws on damaged bytes
    }
}
