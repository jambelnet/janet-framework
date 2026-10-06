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
using System.Text.RegularExpressions;
using Xunit;

namespace jaNETFramework.Tests;

/// <summary>Guards for the repository: every source file names its license, and no personal API key ships in the defaults.</summary>
public class LicenseTests
{
    static string Root() {
        for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent) {
            if (File.Exists(Path.Combine(dir.FullName, "jaNETFramework.sln"))) return dir.FullName;
        }
        return null;
    }

    static string[] SourceFiles() {
        string root = Root();
        if (root == null) return Array.Empty<string>();

        string[] skip = { Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar };
        return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).Where(f => !skip.Any(f.Contains)).ToArray();
    }

    [Fact]
    public void ThereAreSourceFilesToCheck() {
        Assert.NotEmpty(SourceFiles());
    }

    [Fact]
    public void EverySourceFileCarriesTheLicenseNotice() {
        string[] missing = SourceFiles()
            .Where(f => !File.ReadAllText(f).Contains("This file is part of jaNET Framework"))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.True(missing.Length == 0, "No license notice in: " + string.Join(", ", missing));
    }

    [Fact]
    public void TheLicenseFileIsThere() {
        string root = Root();
        Assert.NotNull(root);
        Assert.Contains("GNU GENERAL PUBLIC LICENSE", File.ReadAllText(Path.Combine(root, "LICENSE")));
    }

    [Fact]
    public void TheDefaultConfigurationContainsNoApiKey() {
        string root = Root();
        Assert.NotNull(root);
        string xml = File.ReadAllText(Path.Combine(root, "jaNETFramework", "Configuration", "DefaultAppConfig.xml"));

        Assert.DoesNotMatch(new Regex("APPID=[0-9a-f]{32}", RegexOptions.IgnoreCase), xml);
        Assert.Contains("APPID=YOUR_OPENWEATHERMAP_API_KEY", xml);
    }
}
