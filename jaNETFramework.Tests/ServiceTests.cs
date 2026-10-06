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
using jaNET.Scripting;
using jaNET.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;

namespace jaNETFramework.Tests;

public class TimeTextTests
{
    public TimeTextTests() {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Theory]
    [InlineData(0, "morning", "midnight")]
    [InlineData(5, "morning", "midnight")]
    [InlineData(6, "morning", "morning")]
    [InlineData(11, "morning", "morning")]
    [InlineData(12, "afternoon", "afternoon")]
    [InlineData(17, "afternoon", "afternoon")]
    [InlineData(18, "evening", "evening")]
    [InlineData(19, "evening", "evening")]
    [InlineData(20, "evening", "night")]
    [InlineData(23, "evening", "night")]
    public void PartsOfTheDay(int hour, string salute, string partOfDay) {
        var clock = new FakeClock { Now = new DateTime(2026, 10, 1, hour, 5, 0) };
        var text = new TimeText(clock);

        Assert.Equal(salute, text.Salute);
        Assert.Equal(partOfDay, text.PartOfDay(false));
    }

    [Fact]
    public void FormatsFollowTheClock() {
        var text = new TimeText(new FakeClock { Now = new DateTime(2026, 3, 7, 14, 5, 9) });

        Assert.Equal("14:05", text.Time24);
        Assert.Equal("14", text.Hour);
        Assert.Equal("05", text.Minute);
        Assert.Equal("7/3/2026", text.CalendarDate);
        Assert.Equal("7", text.CalendarDay);
        Assert.Equal("3", text.CalendarMonth);
        Assert.Equal("2026", text.CalendarYear);
        Assert.Equal("Saturday", text.Day);
    }

    [Fact]
    public void UptimeCountsFromItsCreation() {
        var clock = new FakeClock();
        var uptime = new Uptime(clock);

        clock.Now = clock.Now.AddDays(2).AddHours(3).AddMinutes(4).AddSeconds(5);

        Assert.Equal("Days[2], Hours[3], Minutes[4], Seconds[5]", uptime.All);
        Assert.Equal(2, uptime.Days);
    }
}

public class AppInfoTests
{
    readonly FakeHttp _http = new FakeHttp();
    readonly FakeClock _clock = new FakeClock();

    [Fact]
    public void NoNoticeWhenTheSiteCannotBeReached() {
        var info = new AppInfo(_http, _clock);

        Assert.False(info.UpdateAvailable());
        Assert.DoesNotContain("New update", info.Copyright);
    }

    [Fact]
    public void NoticeWhenANewerVersionIsPublished() {
        _http.Pages["http://www.jubito.org/current-version.txt"] = "999.0.0";

        string text = new AppInfo(_http, _clock).Copyright;

        Assert.Contains("New update available.", text);
        Assert.Contains("http://www.jubito.org/download.html", text);
    }

    [Theory]
    [InlineData("03194")]
    [InlineData("3194")]
    [InlineData("999999")]
    [InlineData("0.9.0")]
    [InlineData("")]
    [InlineData("not a version")]
    public void NoNoticeWhenTheSiteHoldsTheOldNumberingOrSomethingOlderOrUnreadable(string published) {
        _http.Pages["http://www.jubito.org/current-version.txt"] = published;

        Assert.False(new AppInfo(_http, _clock).UpdateAvailable());
    }

    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.1.0", "1.0.9", true)]
    [InlineData("2.0.0", "1.9.9", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.1", false)]
    [InlineData("1.0.0", "1.0.0-rc.1", true)]          // the release is newer than its candidate
    [InlineData("1.0.0-rc.2", "1.0.0-rc.1", false)]    // pre-releases of one version are not told apart
    [InlineData("1.0.1-rc.1", "1.0.0", true)]
    [InlineData("v1.2.0", "1.0.0", true)]
    [InlineData(" 1.2.0\r\n", "1.0.0", true)]
    [InlineData("3194", "1.0.0", false)]
    [InlineData("1.0.0", "0.0.0", true)]
    public void VersionsAreComparedAsVersions(string published, string current, bool newer) {
        Assert.Equal(newer, AppInfo.IsNewer(published, current));
    }

    [Fact]
    public void OurVersionIsTheOneOfTheBuildAndLooksLikeOne() {
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$", AppInfo.Version);
        Assert.DoesNotContain("+", AppInfo.Version);
    }

    [Fact]
    public void TheSiteIsAskedOnceAnHour() {
        var info = new AppInfo(_http, _clock);

        info.UpdateAvailable();
        info.UpdateAvailable();
        _clock.Now = _clock.Now.AddMinutes(59);
        info.UpdateAvailable();
        Assert.Single(_http.Requests);

        _clock.Now = _clock.Now.AddMinutes(2);
        info.UpdateAvailable();
        Assert.Equal(2, _http.Requests.Count);
    }
}

public class WeatherTests
{
    const string Url = "http://weather.example/api";

    const string Sample =
        "{\"weather\":[{\"id\":801,\"main\":\"Clouds\",\"icon\":\"02d\"}],\"main\":{\"temp\":17.46,\"pressure\":1012.4,\"humidity\":60,\"temp_min\":11.04,\"temp_max\":19.96},\"name\":\"Athens\",\"cod\":200}";

    readonly TempApp _app = new TempApp();
    readonly FakeHttp _http = new FakeHttp();
    readonly FakeClock _clock = new FakeClock();

    OpenWeatherSource Create(string url = Url) {
        var config = _app.NewConfig();
        config.UpdateWeatherUrl(url);
        return new OpenWeatherSource(config, _http, _clock);
    }

    [Fact]
    public void ReadsTheCurrentWeather() {
        _http.Pages[Url] = Sample;

        WeatherReport w = Create().Current();

        Assert.Equal("Clouds", w.TodayConditions);
        Assert.Equal("20", w.TodayHigh);
        Assert.Equal("11", w.TodayLow);
        Assert.Equal("17.5", w.CurrentTemp);
        Assert.Equal("60", w.CurrentHumidity);
        Assert.Equal("1012.4", w.CurrentPressure);
        Assert.Equal("Athens", w.CurrentCity);
        Assert.Equal("http://openweathermap.org/img/w/02d.png", w.WeatherIcon);
        Assert.Equal("Thursday", w.TodayDay);
        Assert.Equal("Friday", w.TomorrowDay);
    }

    [Fact]
    public void UsesADotAsDecimalSeparatorInEveryCulture() {
        _http.Pages[Url] = Sample;
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("el-GR");
        try {
            Assert.Equal("17.5", Create().Current().CurrentTemp);
        }
        finally {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void FailuresLeaveTheValuesEmpty() {
        WeatherReport offline = Create().Current();                 // no page for the address
        _http.Pages[Url] = "this is not json";
        WeatherReport broken = Create().Current();

        foreach (WeatherReport w in new[] { offline, broken }) {
            Assert.Equal(string.Empty, w.CurrentTemp);
            Assert.Equal(string.Empty, w.WeatherIcon);
            Assert.Equal("Thursday", w.TodayDay);
        }
    }

    [Fact]
    public void NothingIsFetchedWithoutAnAddress() {
        OpenWeatherSource source = Create();
        System.IO.File.WriteAllText(_app.Paths.ConfigFile, "<jaNET><System/></jaNET>");       // a configuration without a weather address

        source.Current();

        Assert.Empty(_http.Requests);
    }

    [Fact]
    public void AnswersAreReusedForHalfAMinute() {
        _http.Pages[Url] = Sample;
        OpenWeatherSource source = Create();

        source.Current();
        _clock.Now = _clock.Now.AddSeconds(29);
        source.Current();
        Assert.Single(_http.Requests);

        _clock.Now = _clock.Now.AddSeconds(2);
        source.Current();
        Assert.Equal(2, _http.Requests.Count);
    }
}

public class UserPresenceTests
{
    [Fact]
    public void NewlyCreatedUsersAreAbsent() {
        var presence = new UserPresence(new TempApp().NewConfig(), () => new RecordingExecutor());

        Assert.False(presence.IsPresent);
        Assert.Equal("absent", presence.Description);
    }

    [Fact]
    public void EventsRunOnChangeOnly() {
        using var app = new TempApp();
        var config = app.NewConfig();
        var executor = new RecordingExecutor();
        var presence = new UserPresence(config, () => executor);

        presence.IsPresent = true;
        presence.IsPresent = true;
        presence.IsPresent = false;
        presence.IsPresent = false;

        Assert.Equal(new[] { "%unmute%; salute; weathertoday", "judo sleep 5000; goodbye; %unmute%" }, executor.Calls);
    }

    [Fact]
    public void AFailingEventDoesNotPreventTheChange() {
        using var app = new TempApp();
        var executor = new RecordingExecutor { Answer = _ => throw new InvalidOperationException("boom") };
        var presence = new UserPresence(app.NewConfig(), () => executor);

        presence.IsPresent = true;

        Assert.True(presence.IsPresent);
    }
}

public class SyntaxAndSplitterTests
{
    [Theory]
    [InlineData("a b", new[] { "a", "b" })]
    [InlineData("a `b c` d", new[] { "a", "b c", "d" })]
    [InlineData("a /* comment text */ b", new[] { "a", " comment text ", "b" })]
    public void SplitsLikeBefore(string input, string[] expected) {
        Assert.Equal(expected, ArgumentSplitter.Split(input));
    }
}
