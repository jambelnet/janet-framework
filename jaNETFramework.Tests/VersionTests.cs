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

using jaNET.Services;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace jaNETFramework.Tests;

/// <summary>A release is a version, an entry in CHANGELOG.md and a tag: the first two must not drift apart.</summary>
public class VersionTests
{
    static string Root() {
        for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent) {
            if (File.Exists(Path.Combine(dir.FullName, "jaNETFramework.sln"))) return dir.FullName;
        }
        return null;
    }

    [Fact]
    public void TheChangelogHasAnEntryForTheCurrentVersion() {
        string root = Root();
        if (root == null) return;

        string changelog = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));

        Assert.Matches(@"(?m)^## \[" + Regex.Escape(AppInfo.Version) + @"\] - \d{4}-\d{2}-\d{2}$", changelog);
        Assert.Contains("## [Unreleased]", changelog);
    }

    [Fact]
    public void ThereIsOneVersionAndItIsInDirectoryBuildProps() {
        string root = Root();
        if (root == null) return;

        string props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        Assert.Contains($"<Version>{AppInfo.Version}</Version>", props);

        foreach (string project in Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories).Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)))
            Assert.DoesNotContain("<Version>", File.ReadAllText(project));
    }

    [Fact]
    public void TheWorkflowsAreThere() {
        string root = Root();
        if (root == null) return;

        Assert.True(File.Exists(Path.Combine(root, ".github", "workflows", "ci.yml")));
        Assert.True(File.Exists(Path.Combine(root, ".github", "workflows", "release.yml")));
    }
}
