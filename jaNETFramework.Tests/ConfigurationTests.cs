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

using jaNET.Configuration;
using jaNET.Infrastructure;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace jaNETFramework.Tests;

/// <summary>A scratch application directory that is removed afterwards.</summary>
internal sealed class TempApp : IDisposable
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "janet-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
    public AppPaths Paths { get; }
    public MemoryLog Log { get; } = new MemoryLog();

    public TempApp() {
        System.IO.Directory.CreateDirectory(Directory);
        Paths = new AppPaths(Directory);
    }

    public AppConfigStore NewConfig(bool withDefaults = true) {
        var store = new AppConfigStore(Paths, Log);
        if (withDefaults) store.EnsureExists();
        return store;
    }

    public void Dispose() {
        try { System.IO.Directory.Delete(Directory, true); } catch (IOException) { }
    }
}

public class AppPathsTests
{
    [Fact]
    public void FixedRootEndsWithASeparator() {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "x"));

        Assert.EndsWith(Path.DirectorySeparatorChar.ToString(), paths.Root);
        Assert.EndsWith("AppConfig.xml", paths.ConfigFile);
        Assert.Equal(paths.Root + "log.txt", paths.File("log.txt"));
    }

    static string Slash(string path) => path.Replace(Path.DirectorySeparatorChar, '/');

    static PathContext Machine(string system, bool admin = false, string home = "/home/me", string current = "/work", string program = "/opt/janet",
                               string[] files = null, params (string Name, string Value)[] environment) =>
        new PathContext(system == "windows", system == "mac", admin, current, program, home,
                        name => environment.Where(e => e.Name == name).Select(e => e.Value).FirstOrDefault(),
                        file => (files ?? new string[0]).Contains(Slash(file)));

    [Fact]
    public void JanetHomeWinsOverEverythingElse() {
        string root = AppPaths.Locate(Machine("linux", files: new[] { "/opt/janet/AppConfig.xml" }, environment: ("JANET_HOME", "/srv/janet")));

        Assert.EndsWith("srv/janet", Slash(root).TrimEnd('/'));
    }

    [Fact]
    public void AnExistingInstallationStaysWhereItIs() {
        Assert.Equal("/opt/janet", Slash(AppPaths.Locate(Machine("linux", files: new[] { "/opt/janet/AppConfig.xml" }))));
        Assert.Equal("C:/old", Slash(AppPaths.Locate(Machine("windows", current: "C:/old", files: new[] { "C:/old/AppConfig.xml" }))));
    }

    [Theory]
    [InlineData("windows", false, "C:/Users/me/AppData/Local/jaNET")]
    [InlineData("linux", true, "/var/lib/janet")]
    [InlineData("linux", false, "/home/me/.local/share/janet")]
    [InlineData("mac", false, "/home/me/Library/Application Support/jaNET")]
    public void ANewInstallationGetsAFolderOfItsOwn(string system, bool admin, string expected) {
        string root = AppPaths.Locate(Machine(system, admin, environment: ("LOCALAPPDATA", "C:/Users/me/AppData/Local")));

        Assert.Equal(expected, Slash(root));
    }

    [Fact]
    public void XdgDataHomeIsHonoured() {
        Assert.Equal("/data/janet", Slash(AppPaths.Locate(Machine("linux", environment: ("XDG_DATA_HOME", "/data")))));
    }

    [Fact]
    public void AnAccountWithoutAProfileKeepsTheOldPlace() {
        Assert.Equal("/opt/janet", Slash(AppPaths.Locate(Machine("linux", home: ""))));
        Assert.Equal("C:/work", Slash(AppPaths.Locate(Machine("windows", current: "C:/work"))));
    }

    [Fact]
    public void TheWebUiComesFromTheDataFolderIfItHasOneAndFromTheProgramOtherwise() {
        string data = Path.Combine(Path.GetTempPath(), "janet-paths-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string program = Path.Combine(data, "program");
        Directory.CreateDirectory(Path.Combine(program, "www"));
        try {
            var paths = new AppPaths(data, program);
            Assert.Equal(program + Path.DirectorySeparatorChar + "www" + Path.DirectorySeparatorChar, paths.WebRoot);

            Directory.CreateDirectory(Path.Combine(data, "www"));
            Assert.Equal(data + Path.DirectorySeparatorChar + "www" + Path.DirectorySeparatorChar, paths.WebRoot);
            Assert.Equal(paths.WebRoot.TrimEnd(Path.DirectorySeparatorChar) + "/css/app.css", paths.MapRequest("www/css/app.css"));
            Assert.EndsWith("/", paths.MapRequest("www/"));           // a folder stays a folder
            Assert.Equal(paths.Root + "AppConfig.xml", paths.MapRequest("AppConfig.xml"));
        }
        finally {
            Directory.Delete(data, true);
        }
    }

    [Fact]
    public void HelperProgramsAreLookedForNextToTheProgram() {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "nowhere-data"), Path.Combine(Path.GetTempPath(), "nowhere-program"));

        Assert.StartsWith(Path.Combine(Path.GetTempPath(), "nowhere-program"), paths.ProgramFile("jspeech.exe"));
    }
}

public class AppConfigStoreTests
{
    [Fact]
    public void WritesTheDefaultConfigurationOnce() {
        using var app = new TempApp();
        var config = app.NewConfig();

        Assert.True(File.Exists(app.Paths.ConfigFile));
        Assert.Equal("localhost", config.Comm.Hostname);
        Assert.Equal("8080", config.Comm.HttpPort);
        Assert.Equal("none", config.Comm.Authentication);
        Assert.Equal("5744", config.Comm.LocalPort);
        Assert.Equal("localhost; 192.168.1.1", config.Comm.Trusted);
        Assert.Equal("/dev/ttyACM0", config.Comm.ComPort);
        Assert.Equal("Alert from Jubito", config.MailHeaders.Subject);
        Assert.StartsWith("http://api.openweathermap.org/", config.WeatherUrl);

        File.AppendAllText(app.Paths.ConfigFile, "<!-- hand edit -->");
        config.EnsureExists();                       // must not overwrite
        Assert.Contains("hand edit", File.ReadAllText(app.Paths.ConfigFile));
    }

    [Fact]
    public void ReadsInstructionSetsAndEvents() {
        using var app = new TempApp();
        var config = app.NewConfig();

        Assert.Equal(new[] { "You are, %user%." }, config.InstructionActions("*whoami"));
        Assert.Equal(new[] { "*whoami" }, config.InstructionActions("whoami"));
        Assert.Empty(config.InstructionActions("nope"));
        Assert.Equal(new[] { "%unmute%; salute; weathertoday" }, config.EventActions("oncheckin"));
        Assert.Contains("whoami", config.InstructionSetIds());
        Assert.Contains("*whoami", config.InstructionSetIds());
    }

    [Fact]
    public void ListsElementsInTheFormatOfTheOriginalProgram() {
        using var app = new TempApp();
        var config = app.NewConfig();

        Assert.Contains("<InstructionSet id=\"*salute\">Good %salute% %user%.</InstructionSet>", config.InstructionSetXml());
        Assert.Contains("<InstructionSet id=\"whoami\" img=\"/www/images/icon-set/user.png\" descr=\"System login\" shortdescr=\"Get user login\" header=\"Login name\" categ=\"System\" ref=\"whoamiwidget\">*whoami</InstructionSet>",
            config.InstructionSetXml());
        Assert.Equal("<event id=\"oncheckout\">judo sleep 5000; goodbye; %unmute%</event>", config.EventXml()[1]);
    }

    [Fact]
    public void AddsInstructionSetsWithOnlyTheValuesThatAreSet() {
        using var app = new TempApp();
        var config = app.NewConfig();

        Assert.Equal("Element added.", config.AddInstructionSets(new[] {
            new InstructionSetEntry("*x", "  do it  "),
            new InstructionSetEntry("x", "*x", Category: "Cat", Header: "Head", Reference: " ")
        }));

        Assert.Equal(new[] { "do it" }, config.InstructionActions("*x"));
        Assert.Contains("<InstructionSet id=\"x\" header=\"Head\" categ=\"Cat\">*x</InstructionSet>", config.InstructionSetXml());
    }

    [Fact]
    public void EscapesSpecialCharactersAndReadsThemBack() {
        using var app = new TempApp();
        var config = app.NewConfig();

        config.AddInstructionSets(new[] { new InstructionSetEntry("c", "{ evalBool(1 < 2 && \"a\" == 'b'); y; n; }") });

        Assert.Equal("{ evalBool(1 < 2 && \"a\" == 'b'); y; n; }", config.InstructionActions("c").Single());
        Assert.Contains("&lt;", config.InstructionSetXml().Last());
    }

    [Fact]
    public void IdsAreDataNotXPath() {
        using var app = new TempApp();
        var config = app.NewConfig();
        config.AddInstructionSets(new[] { new InstructionSetEntry("it's", "quoted"), new InstructionSetEntry("a']|//*[@id='b", "tricky") });

        Assert.Equal(new[] { "quoted" }, config.InstructionActions("it's"));
        Assert.Equal(new[] { "tricky" }, config.InstructionActions("a']|//*[@id='b"));
        Assert.Empty(config.InstructionActions("' or '1'='1"));
    }

    [Fact]
    public void AddEventAlsoAddsTheTriggeringInstructionSet() {
        using var app = new TempApp();
        var config = app.NewConfig();

        Assert.Equal("Element added.", config.AddEvent("ev1", "yes; no"));

        Assert.Equal(new[] { "yes; no" }, config.EventActions("ev1"));
        Assert.Equal(new[] { "%~>ev1%" }, config.InstructionActions("ev1"));
    }

    [Fact]
    public void RemovesTheItemAndItsLauncher() {
        using var app = new TempApp();
        var config = app.NewConfig();
        config.AddInstructionSets(new[] { new InstructionSetEntry("*x", "do"), new InstructionSetEntry("x", "*x"), new InstructionSetEntry("other", "keep") });

        Assert.Equal("Element removed.", config.RemoveInstructionSet("x"));

        Assert.Empty(config.InstructionActions("x"));
        Assert.Empty(config.InstructionActions("*x"));
        Assert.Equal(new[] { "keep" }, config.InstructionActions("other"));
        Assert.Equal("Element removed.", config.RemoveInstructionSet("never existed"));
    }

    [Fact]
    public void RemovesEvents() {
        using var app = new TempApp();
        var config = app.NewConfig();

        config.RemoveEvent("oncheckin");

        Assert.Empty(config.EventActions("oncheckin"));
        Assert.Single(config.EventActions("oncheckout"));
    }

    [Fact]
    public void UpdatesOnlyTheGivenCommValues() {
        using var app = new TempApp();
        var config = app.NewConfig();

        Assert.Equal("Element added.", config.Update(new CommUpdate { ComPort = "COM9", BaudRate = "19200", Trusted = " " }));

        Assert.Equal("COM9", config.Comm.ComPort);
        Assert.Equal("19200", config.Comm.BaudRate);
        Assert.Equal("localhost; 192.168.1.1", config.Comm.Trusted);   // blank: unchanged
        Assert.Equal("8080", config.Comm.HttpPort);
    }

    [Fact]
    public void MailHeadersCanBeChanged() {
        // the original implementation failed for this call
        using var app = new TempApp();
        var config = app.NewConfig();

        Assert.Equal("Element added.", config.UpdateMailHeaders("a@x.org", "b@y.org", "Hello there"));

        Assert.Equal(new MailHeaderSettings("a@x.org", "b@y.org", "Hello there"), config.MailHeaders);
    }

    [Fact]
    public void WeatherUrlAndMissingSectionsAreCreatedWhenNeeded() {
        using var app = new TempApp();
        var config = app.NewConfig();

        config.UpdateWeatherUrl("http://example.org/w?q=1&u=m");
        Assert.Equal("http://example.org/w?q=1&u=m", config.WeatherUrl);

        Assert.Equal(string.Empty, config.MailKeyword);       // not in the default file
        File.WriteAllText(app.Paths.ConfigFile, "<jaNET><System/></jaNET>");
        config.Update(new CommUpdate { Hostname = "h" });
        Assert.Equal("h", config.Comm.Hostname);
    }

    [Fact]
    public void MissingOrBrokenFilesReadAsEmptyAndAreLogged() {
        using var app = new TempApp();
        var config = app.NewConfig(withDefaults: false);

        Assert.Equal(string.Empty, config.Comm.Hostname);
        Assert.Empty(config.InstructionActions("x"));
        Assert.DoesNotContain(app.Log.Entries, e => !e.Contains("not found") && !e.Contains("Malformed"));
        Assert.NotEmpty(app.Log.Entries);

        File.WriteAllText(app.Paths.ConfigFile, "<jaNET><broken");
        Assert.Equal(string.Empty, config.Comm.Hostname);
    }

    [Fact]
    public void AFailedChangeKeepsTheOldFileAndReportsTheReason() {
        using var app = new TempApp();
        var config = app.NewConfig();
        string before = File.ReadAllText(app.Paths.ConfigFile);
        File.WriteAllText(app.Paths.ConfigFile, "<jaNET><broken");
        File.Copy(app.Paths.ConfigFile, app.Paths.ConfigFile + ".bak", true);
        File.WriteAllText(app.Paths.ConfigFile + ".bak", before);

        string message = config.AddEvent("x", "y");

        Assert.EndsWith("Please try again.", message);
        Assert.False(File.Exists(app.Paths.ConfigFile + ".tmp"));
    }

    [Fact]
    public void ChangesBecomeVisibleImmediately() {
        using var app = new TempApp();
        var config = app.NewConfig();
        Assert.Empty(config.InstructionActions("late"));

        config.AddInstructionSets(new[] { new InstructionSetEntry("late", "now") });

        Assert.Equal(new[] { "now" }, config.InstructionActions("late"));
    }
}

public class SettingsStoreTests
{
    [Fact]
    public void SavesAndLoadsEncryptedLines() {
        using var app = new TempApp();
        var store = new SettingsStore(app.Paths, app.Log);

        Assert.Null(store.Load(".x"));
        Assert.False(store.Exists(".x"));

        Assert.Equal("Settings saved.", store.Save(".x", "one\r\n two \nthree"));

        Assert.True(store.Exists(".x"));
        Assert.Equal(new[] { "one", "two", "three" }, store.Load(".x"));
        Assert.DoesNotContain("one", File.ReadAllText(app.Paths.File(".x")));
    }

    [Fact]
    public void NewFilesUseTheCurrentFormat() {
        using var app = new TempApp();
        new SettingsStore(app.Paths, app.Log).Save(".htaccess", "admin\r\nadmin");

        string[] lines = File.ReadAllLines(app.Paths.File(".htaccess"));

        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.StartsWith("v2:", line));
        Assert.NotEqual(lines[0], lines[1]);                 // the same value looks different every time
    }

    [Fact]
    public void TypedSettingsAreNullUntilSaved() {
        using var app = new TempApp();
        var store = new SettingsStore(app.Paths, app.Log);
        Assert.Null(store.LoadSmtp());
        Assert.Null(store.LoadGmail());

        store.Save(SettingsFiles.Smtp, "smtp.example.org\r\nuser\r\nsecret\r\n587\r\ntrue");
        store.Save(SettingsFiles.Gmail, "me@example.org\r\nsecret");

        Assert.Equal(new MailServerSettings("smtp.example.org", "user", "secret", 587, true), store.LoadSmtp());
        Assert.Equal(new GmailSettings("me@example.org", "secret", GmailDefaults.FeedUrl, GmailDefaults.SmtpHost, GmailDefaults.SmtpPort, GmailDefaults.SmtpSsl, GmailDefaults.Pop3Host, GmailDefaults.Pop3Port, GmailDefaults.Pop3Ssl), store.LoadGmail());
    }

    [Fact]
    public void WebLoginMatchesBothValues() {
        var login = new WebLogin("bob", "pw");

        Assert.True(login.Matches("bob", "pw"));
        Assert.False(login.Matches("bob", "x"));
        Assert.False(login.Matches("x", "pw"));
    }
}
