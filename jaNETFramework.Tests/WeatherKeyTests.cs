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
using jaNET.Services;
using System.IO;
using System.Linq;
using Xunit;

namespace jaNETFramework.Tests;

/// <summary>The OpenWeatherMap key is kept with the other secrets, not in AppConfig.xml.</summary>
public class WeatherKeyTests
{
    const string Real = "0123456789abcdef0123456789abcdef";
    const string Sample =
        "{\"weather\":[{\"id\":801,\"main\":\"Clouds\",\"icon\":\"02d\"}],\"main\":{\"temp\":17.46,\"pressure\":1012.4,\"humidity\":60,\"temp_min\":11.04,\"temp_max\":19.96},\"name\":\"Athens\",\"cod\":200}";

    [Fact]
    public void TheStoredKeyReplacesThePlaceholderInTheUrl() {
        string url = "http://api.example/weather?q=Athens&units=metric&APPID=" + WeatherKey.Placeholder + "&lang=en";

        Assert.Equal("http://api.example/weather?q=Athens&units=metric&APPID=abc123&lang=en", WeatherKey.Apply(url, "abc123"));
    }

    [Fact]
    public void AUrlWithoutAKeyParameterGetsOne() {
        Assert.Equal("http://api.example/w?q=x&APPID=k", WeatherKey.Apply("http://api.example/w?q=x", "k"));
        Assert.Equal("http://api.example/w?APPID=k", WeatherKey.Apply("http://api.example/w", "k"));
    }

    [Fact]
    public void WithoutAStoredKeyTheUrlIsLeftAlone() {
        string url = "http://api.example/w?APPID=" + Real;

        Assert.Equal(url, WeatherKey.Apply(url, null));
        Assert.Equal(url, WeatherKey.Apply(url, ""));
    }

    [Fact]
    public void ARealKeyInTheUrlIsTakenOutAndThePlaceholderIsLeft() {
        Assert.True(WeatherKey.TryTake("http://api.example/w?q=x&APPID=" + Real + "&units=metric", out string key, out string cleaned));

        Assert.Equal(Real, key);
        Assert.Equal("http://api.example/w?q=x&APPID=" + WeatherKey.Placeholder + "&units=metric", cleaned);
    }

    [Theory]
    [InlineData("http://api.example/w?APPID=" + WeatherKey.Placeholder)]
    [InlineData("http://api.example/w?q=x")]
    [InlineData("")]
    public void NothingIsTakenFromAUrlWithoutARealKey(string url) {
        Assert.False(WeatherKey.TryTake(url, out _, out string cleaned));
        Assert.Equal(url, cleaned);
    }

    [Fact]
    public void TheWeatherServiceAsksWithTheStoredKey() {
        using var app = new TempApp();
        var config = app.NewConfig();
        config.UpdateWeatherUrl("http://weather.example/api?APPID=" + WeatherKey.Placeholder);
        var settings = new SettingsStore(app.Paths, app.Log);
        settings.Save(SettingsFiles.Weather, "secret-key");
        var http = new FakeHttp();
        http.Pages["http://weather.example/api?APPID=secret-key"] = Sample;

        WeatherReport report = new OpenWeatherSource(config, http, new FakeClock(), settings).Current();

        Assert.Equal("Clouds", report.TodayConditions);
        Assert.Equal(new[] { "http://weather.example/api?APPID=secret-key" }, http.Requests);
    }

    [Fact]
    public void StartMovesTheKeyOfAnOlderConfigurationIntoTheEncryptedSettings() {
        using var t = new TestHost(start: false, weatherKey: false);
        t.Host.Config.EnsureExists();
        t.Host.Config.UpdateWeatherUrl("http://api.openweathermap.org/data/2.5/weather?q=Athens,GR&units=metric&APPID=" + Real);

        t.Host.Start();

        Assert.DoesNotContain(Real, File.ReadAllText(Path.Combine(t.Directory, "AppConfig.xml")));
        Assert.Contains(WeatherKey.Placeholder, t.Run("judo weather settings"));
        Assert.Equal("Weather API key: set", t.Run("judo weather key"));
        string stored = File.ReadAllText(Path.Combine(t.Directory, ".weathersettings"));
        Assert.StartsWith("v2:", stored.Trim());
        Assert.DoesNotContain(Real, stored);
        Assert.DoesNotContain(t.Host.Notices, n => n.Source == "Weather");
    }

    [Fact]
    public void AKeyThatIsAlreadyStoredIsNotReplacedByTheOneInTheUrl() {
        using var t = new TestHost(start: false);                      // has the key "test-key"
        t.Host.Config.EnsureExists();
        t.Host.Config.UpdateWeatherUrl("http://api.openweathermap.org/data/2.5/weather?APPID=" + Real);

        t.Host.Start();

        Assert.DoesNotContain(Real, File.ReadAllText(Path.Combine(t.Directory, "AppConfig.xml")));
        Assert.Equal("Weather API key: set", t.Run("judo weather key"));
    }

    [Fact]
    public void WithoutAKeyThereIsAHintAndSettingOneRemovesIt() {
        using var t = new TestHost(weatherKey: false);

        Assert.Equal("Weather API key: not set", t.Run("judo weather key"));
        HostNoticeCheck(t, expected: true);

        Assert.Equal("Settings saved.", t.Run("judo weather key abc"));
        HostNoticeCheck(t, expected: false);
        Assert.DoesNotContain("abc", t.Run("judo weather settings"));       // the key is never shown
    }

    static void HostNoticeCheck(TestHost t, bool expected) {
        var notice = t.Host.Notices.FirstOrDefault(n => n.Source == "Weather");
        Assert.Equal(expected, notice != null);
        if (notice != null) Assert.Equal(jaNET.Hosting.NoticeLevel.Info, notice.Level);
    }
}
