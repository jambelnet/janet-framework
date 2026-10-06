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
using jaNET.Scripting;
using jaNET.Services;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace jaNETFramework.Tests;

public class StartupTests
{
    [Fact]
    public void FirstStartCreatesTheConfigurationAndTheDefaultWebLogin() {
        using var t = new TestHost();

        Assert.True(File.Exists(Path.Combine(t.Directory, "AppConfig.xml")));
        Assert.Equal(new WebLogin("admin", "admin"), new SettingsStore(new jaNET.Infrastructure.AppPaths(t.Directory), new MemoryLog()).LoadWebLogin());
    }

    [Fact]
    public void StartsTheServicesThatAreConfiguredAndChecksTheUserIn() {
        using var t = new TestHost();

        Assert.True(t.Web.IsRunning);
        Assert.True(t.Socket.IsRunning);
        Assert.True(t.Serial.IsOpen);                   // default config names /dev/ttyACM0
        Assert.Equal("present", t.Run("%whereami%"));
    }

    [Fact]
    public void StartingTwiceDoesNothingTheSecondTime() {
        using var t = new TestHost();

        t.Host.Start();

        Assert.Equal(1, t.Web.Starts);
    }

    [Fact]
    public void ExposesVersionAndUser() {
        using var t = new TestHost(start: false);

        Assert.Equal(AppInfo.Version, t.Host.Version);
        Assert.Equal(Environment.UserName, t.Host.UserName);
    }

    [Fact]
    public void TheCopyrightTextNamesVersionAndYear() {
        using var t = new TestHost();

        Assert.StartsWith($"jaNET Framework [Version {t.Host.Version}]\r\nCopyright (c) 2010-2026", t.Host.Copyright);
    }
}

public class InstructionTests
{
    [Fact]
    public void RunsAnInstructionSetAndExpandsFunctions() {
        using var t = new TestHost();

        Assert.Equal("You are, " + Environment.UserName + ".", t.Run("whoami"));
    }

    [Fact]
    public void UnknownInstructionsSayNotFound() {
        using var t = new TestHost();

        Assert.Equal("nonexistent, not found.", t.Run("nonexistent"));
    }

    [Fact]
    public void SeveralInstructionsSeparatedByAnyOfTwoCharacters() {
        using var t = new TestHost();
        t.Run("judo inset add yes <lock>YES</lock>");
        t.Run("judo inset add no <lock>NO</lock>");

        Assert.Equal("YES\r\nNO", t.Run("yes; no"));
        Assert.Equal("YES\r\nNO", t.Run("yes&no"));
        Assert.Equal("YES", t.Run("yes;yes; "));          // repeated and blank ones are ignored
    }

    [Fact]
    public void TheSameInstructionTwiceUnderDifferentSpellingsIsAnError() {
        using var t = new TestHost();
        t.Run("judo inset add yes <lock>YES</lock>");

        Assert.StartsWith("An item with the same key has already been added", t.Run("yes; %yes%"));
    }

    [Fact]
    public void TheAnswerCanBeJsonOrHtml() {
        using var t = new TestHost();
        t.Run("judo inset add lt <lock>a < b</lock>");

        Assert.Equal("{\"lt\":{\"Key\":\"lt\",\"Value\":\"a \\u003c b\"}}", t.Run("lt", ResponseFormat.Json));
        Assert.Equal("a &lt; b", t.Run("lt", ResponseFormat.Html));
        Assert.Equal("a < b", t.Run("lt", ResponseFormat.Text));
    }

    [Fact]
    public void InstructionsCanPointToOtherInstructions() {
        using var t = new TestHost();
        t.Run("judo inset add inner <lock>deep</lock>");
        t.Run("judo inset add outer <lock>[*inner]</lock>");

        Assert.Equal("[deep]", t.Run("outer"));
    }

    [Fact]
    public void APlainAsteriskIsJustText() {
        using var t = new TestHost();
        t.Run("judo inset add calc <lock>2 * 3</lock>");

        Assert.Equal("2 * 3", t.Run("calc"));       // used to loop forever
    }

    [Fact]
    public void APointerToNothingIsReported() {
        using var t = new TestHost();
        t.Run("judo inset add bad <lock>see *nothing</lock>");

        Assert.Equal("*nothing, not found.", t.Run("bad"));
    }

    [Fact]
    public void InstructionsThatCallEachOtherForeverAreStopped() {
        using var t = new TestHost();
        t.Run("judo inset add loop <lock>*loop</lock>");

        Assert.Contains("too deeply", t.Run("loop"));
    }

    [Fact]
    public void ConditionsPickTheBranch() {
        using var t = new TestHost();
        t.Run("judo inset add yes <lock>YES</lock>");
        t.Run("judo inset add no <lock>NO</lock>");
        t.Run("judo inset add c1 <lock>{ evalBool(1 == 1); yes; no; }</lock>");
        t.Run("judo inset add c2 <lock>{ evalBool(2 < 1); yes; no; }</lock>");
        t.Run("judo inset add c3 <lock>{ evalBool(\"abc\" ~> \"b\"); yes; no; }</lock>");
        t.Run("judo inset add c4 <lock>{ evalBool(\"%whereami%\" == \"present\"); yes; no; }</lock>");

        Assert.Equal("YES", t.Run("c1"));
        Assert.Equal("NO", t.Run("c2"));
        Assert.Equal("YES", t.Run("c3"));
        Assert.Equal("YES", t.Run("c4"));
    }

    [Fact]
    public void ABrokenConditionReportsTheCompilerMessage() {
        using var t = new TestHost();
        t.Run("judo inset add broken <lock>{ evalBool(this is not C#); a; b; }</lock>");

        Assert.StartsWith("Error Compiling Expression", t.Run("broken"));
    }

    [Fact]
    public void MutedInstructionsDoNotSpeak() {
        using var t = new TestHost();
        t.Run("judo inset add hello <lock>Hello</lock>");
        t.Settle();

        t.Run("{mute}hello");
        t.Run("{widget}hello");
        Thread.Sleep(200);

        Assert.Empty(t.Speaker.Spoken);
    }

    [Fact]
    public void OrdinaryInstructionsSpeakTheirAnswer() {
        using var t = new TestHost();
        t.Run("judo inset add hello <lock>Hello</lock>");
        t.Settle();

        t.Run("hello");

        Assert.True(SpinWait.SpinUntil(() => t.Speaker.Spoken.Contains("Hello\r\n"), 2000));
    }

    [Fact]
    public void TheMuteFunctionSilencesAndUnmuteRestores() {
        using var t = new TestHost();
        t.Run("%mute%");
        Assert.True(t.Speaker.Muted);
        t.Run("%unmute%");
        Assert.False(t.Speaker.Muted);
    }
}

public class FunctionTests
{
    public FunctionTests() {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;      // date and time separators are part of the answers
    }

    [Fact]
    public void TimeFunctionsFollowTheClock() {
        using var t = new TestHost();

        Assert.Equal("2026", t.Run("%calendaryear%"));
        Assert.Equal("1/10/2026", t.Run("%calendardate%"));
        Assert.Equal("08:30", t.Run("%time24%"));
        Assert.Equal("Thursday", t.Run("%day%"));
        Assert.Equal("morning", t.Run("%salute%"));
    }

    [Fact]
    public void WeatherFunctionsUseTheWeatherSource() {
        using var t = new TestHost();

        Assert.Equal("17.5", t.Run("%todaytemp%"));
        Assert.Equal("Athens", t.Run("%currentcity%"));
        Assert.Equal("11 - 19", t.Run("%todaylow% - %todayhigh%"));
    }

    [Fact]
    public void ValuesWithADollarSignAreNotTreatedAsGroupReferences() {
        using var t = new TestHost();

        Assert.Equal("http://icons/a$1b.png", t.Run("%weathericon%"));
    }

    [Fact]
    public void UptimeAndPathFunctions() {
        using var t = new TestHost();

        Assert.Equal("0", t.Run("%upseconds%"));
        Assert.StartsWith("Days[0], Hours[0], Minutes[0], Seconds[0]", t.Run("%uptime%"));
        Assert.Equal(t.Directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, t.Run("%apppath%"));
    }

    [Fact]
    public void InternetFunctionAsksTheConnectionCheck() {
        using var t = new TestHost();
        Assert.Equal("False", t.Run("%inet%"));

        t.Http.Reachable = true;
        Assert.Equal("True", t.Run("%inet%"));
    }

    [Fact]
    public void ClearAsksTheConsoleAndLeavesNothingBehind() {
        using var t = new TestHost();

        Assert.Equal(string.Empty, t.Run("%clear%"));
        Assert.Equal(1, t.Console.Cleared);
    }

    [Fact]
    public void ExitStopsTheServicesAndEndsTheProcessOnce() {
        using var t = new TestHost();

        Assert.Equal(string.Empty, t.Run("%exit%"));          // nothing is left of the function

        Assert.False(t.Host.IsRunning);
        Assert.True(SpinWait.SpinUntil(() => t.Exits == 1, 2000));
        Assert.False(t.Web.IsRunning);
        Assert.False(t.Socket.IsRunning);
        Assert.False(t.Serial.IsOpen);
        t.Run("%quit%");
        Thread.Sleep(100);
        Assert.Equal(1, t.Exits);                              // asked once, ends once
    }
}

public class PresenceTests
{
    // the events of the default configuration speak; replace them by ones that report what they can see
    static TestHost WithEvents(string checkInAction, string checkOutAction) {
        var t = new TestHost();
        t.Host.Config.RemoveEvent("oncheckin");
        t.Host.Config.RemoveEvent("oncheckout");
        t.Run("judo inset add seen <lock>seen-%whereami%</lock>");
        t.Host.Config.AddEvent("oncheckin", checkInAction);
        t.Host.Config.AddEvent("oncheckout", checkOutAction);
        t.Settle();
        return t;
    }

    [Fact]
    public void TheCheckInEventRunsAfterTheUserCountsAsPresent() {
        using var t = WithEvents("seen", "seen");
        t.Run("%checkout%");
        t.Settle();

        t.Run("%checkin%");

        Assert.True(SpinWait.SpinUntil(() => t.Speaker.Spoken.Any(s => s.Trim() == "seen-present"), 2000));
    }

    [Fact]
    public void TheCheckOutEventRunsWhileTheUserStillCountsAsPresent() {
        using var t = WithEvents("seen", "seen");

        t.Run("%checkout%");

        Assert.True(SpinWait.SpinUntil(() => t.Speaker.Spoken.Any(s => s.Trim() == "seen-present"), 2000));
        Assert.Equal("absent", t.Run("%whereami%"));
    }

    [Fact]
    public void ChangingToTheSameStateDoesNotFireTheEventAgain() {
        using var t = WithEvents("seen", "seen");

        t.Run("%checkin%");      // already present since the start
        Thread.Sleep(200);

        Assert.Empty(t.Speaker.Spoken);
    }
}
