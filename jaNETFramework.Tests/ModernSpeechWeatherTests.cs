/* (c) J@mBeL.net 2010-2026, John Ambeliotis. This file is part of jaNET Framework.
 * Licensed under the GNU General Public License, version 3 or later; see LICENSE. */

using jaNET.Configuration;
using jaNET.Services;
using jaNET.Servers;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace jaNETFramework.Tests;

public class ModernWeatherTests
{
    const string Url = "https://api.open-meteo.com/v1/forecast?latitude=49.6116&longitude=6.1319";
    const string Sample = """
        {"current":{"temperature_2m":12.64,"relative_humidity_2m":72,"surface_pressure":1014.0,"weather_code":3,"is_day":0},
         "daily":{"time":["2026-10-10","2026-10-11"],"temperature_2m_min":[8.1,7.3],"temperature_2m_max":[14.4,15.6],"weather_code":[3,61]}}
        """;

    [Fact]
    public void MeteoReadsCurrentAndForecastWithoutLeakingTheLegacyKey() {
        using var app = new TempApp();
        var config = app.NewConfig();
        config.UpdateWeatherUrl(Url);
        config.UpdateWeatherLocation("Luxembourg, LU");
        var settings = new SettingsStore(app.Paths, app.Log);
        settings.Save(SettingsFiles.Weather, "legacy-secret");
        var http = new FakeHttp(); http.Pages[Url] = Sample;
        var source = new OpenWeatherSource(config, http, new FakeClock(), settings);
        var report = source.Current();
        Assert.Equal("12.6", report.CurrentTemp);
        Assert.Equal("72", report.CurrentHumidity);
        Assert.Equal("1014", report.CurrentPressure);
        Assert.Equal("Luxembourg, LU", report.CurrentCity);
        Assert.Equal("Overcast", report.TodayConditions);
        Assert.Equal("8.1", report.TodayLow);
        Assert.Equal("14.4", report.TodayHigh);
        Assert.Equal("Saturday", report.TodayDay);
        Assert.Equal("Sunday", report.TomorrowDay);
        Assert.Equal("Rain", report.TomorrowConditions);
        Assert.Equal("7.3", report.TomorrowLow);
        Assert.Equal("15.6", report.TomorrowHigh);
        Assert.EndsWith("04n@2x.png", report.WeatherIcon);
        Assert.Equal(new[] { Url }, http.Requests);
        source.Current(); Assert.Single(http.Requests);
        config.UpdateWeatherLocation("Luxembourg");
        Assert.Equal("Luxembourg", source.Current().CurrentCity);
        Assert.Equal(2, http.Requests.Count);
        const string other = Url + "&timezone=auto";
        http.Pages[other] = Sample.Replace("12.64", "19.5");
        config.UpdateWeatherUrl(other);
        Assert.Equal("19.5", source.Current().CurrentTemp);
        Assert.Equal("legacy-secret", settings.Load(SettingsFiles.Weather)[0]);
    }

    [Theory]
    [InlineData("{\"current\":{\"temperature_2m\":12,\"weather_code\":null,\"is_day\":null}}", "12")]
    [InlineData("{\"current\":{\"temperature_2m\":null}}", "")]
    [InlineData("{\"error\":true,\"reason\":\"bad request\"}", "")]
    [InlineData("not json", "")]
    public void MeteoHandlesMissingOrInvalidMeasurements(string json, string temperature) {
        using var app = new TempApp(); var config = app.NewConfig(); config.UpdateWeatherUrl(Url);
        var http = new FakeHttp(); http.Pages[Url] = json;
        var report = new OpenWeatherSource(config, http, new FakeClock()).Current();
        Assert.Equal(temperature, report.CurrentTemp);
        Assert.Empty(report.CurrentHumidity);
    }

    [Fact]
    public void MeteoPrefersSeaLevelPressureForLegacyCompatibility() {
        using var app = new TempApp(); var config = app.NewConfig(); config.UpdateWeatherUrl(Url);
        var http = new FakeHttp(); http.Pages[Url] = Sample.Replace("\"surface_pressure\":1014.0", "\"pressure_msl\":1022.4,\"surface_pressure\":980");
        Assert.Equal("1022.4", new OpenWeatherSource(config, http, new FakeClock()).Current().CurrentPressure);
    }

    [Fact]
    public void ProviderCommandsPreserveLegacyEndpointAndKey() {
        using var app = new TestHost(start: false);
        app.Host.Config.EnsureExists();
        const string legacy = "http://api.openweathermap.org/data/2.5/weather?q=Luxembourg,LU&units=metric&APPID=YOUR_OPENWEATHERMAP_API_KEY";
        app.Run("judo weather set <lock>" + legacy + "</lock>");
        app.Run("judo weather key secret-test-key");
        Assert.Equal("Element added.", app.Run("judo weather openmeteo 49.6116 6.1319 <lock>Luxembourg, LU</lock>"));
        Assert.StartsWith(Url, app.Run("judo weather settings"));
        Assert.Contains("forecast_days=2", app.Run("judo weather settings"));
        Assert.Equal("Luxembourg, LU", app.Run("judo weather location"));
        Assert.Equal("Weather API key: set", app.Run("judo weather key"));
        app.Run("judo weather set <lock>" + legacy + "</lock>");
        Assert.Equal(legacy, app.Run("judo weather settings"));
    }

    [Theory]
    [InlineData("91 6")]
    [InlineData("49 -181")]
    [InlineData("NaN 6")]
    [InlineData("49 Infinity")]
    [InlineData("49,6 6")]
    [InlineData("49")]
    public void InvalidCoordinatesDoNotModifyWeather(string coordinates) {
        using var app = new TestHost(start: false);
        app.Host.Config.EnsureExists();
        var before = app.Run("judo weather settings");
        Assert.StartsWith("Weather:", app.Run("judo weather openmeteo " + coordinates));
        Assert.Equal(before, app.Run("judo weather settings"));
    }
}

public class ModernSpeechTests
{
    internal static byte[] Wave(int samples = 160, int rate = 16000, short channels = 1, short bits = 16) {
        using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1);
        writer.Write(channels); writer.Write(rate); writer.Write(rate * channels * bits / 8);
        writer.Write((short)(channels * bits / 8)); writer.Write(bits);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2); writer.Write(new byte[samples * 2]);
        return memory.ToArray();
    }

    [Fact]
    public void SettingsAreEncryptedAndInvalidChangesLeaveThemUntouched() {
        using var app = new TempApp(); var store = new SettingsStore(app.Paths, app.Log);
        using var speech = new SpeechService(store, app.Paths);
        var saved = new SpeechSettings("piper", "piper.exe", "en", "voice.onnx", "private-model-folder");
        Assert.Equal("Settings saved.", speech.Save(saved));
        Assert.Equal(saved, speech.Settings);
        Assert.DoesNotContain("private-model-folder", File.ReadAllText(app.Paths.File(SettingsFiles.Speech)));
        Assert.Throws<ArgumentException>(() => speech.Save(saved with { Engine = "cloud" }));
        Assert.Throws<ArgumentException>(() => speech.Save(saved with { Executable = "piper\ncommand" }));
        Assert.Throws<ArgumentException>(() => speech.Save(saved with { PiperModel = "" }));
        Assert.Equal(saved, speech.Settings);
    }

    [Fact]
    public void WaveReaderAcceptsTwentySecondsAndRejectsAnythingLonger() {
        Assert.Equal(640000, SpeechService.ReadPcm(Wave(320000)).Length);
        Assert.Throws<ArgumentException>(() => SpeechService.ReadPcm(Wave(320001)));
        Assert.Throws<ArgumentException>(() => SpeechService.ReadPcm(Wave(0)));
        Assert.Throws<ArgumentException>(() => SpeechService.ReadPcm(Wave()[..45]));
        Assert.Throws<ArgumentException>(() => SpeechService.ReadPcm(new byte[44]));
    }

    [Theory]
    [InlineData(48000, 1, 16)]
    [InlineData(16000, 2, 16)]
    [InlineData(16000, 1, 8)]
    public void WaveReaderRejectsOtherAudioFormats(int rate, short channels, short bits) {
        Assert.Throws<ArgumentException>(() => SpeechService.ReadPcm(Wave(rate: rate, channels: channels, bits: bits)));
    }

    [Fact]
    public async Task MissingModelOrEngineIsReportedWithoutLoadingOrDownloading() {
        using var app = new TempApp(); using var speech = new SpeechService(new SettingsStore(app.Paths, app.Log), app.Paths);
        Assert.Contains("No recognition model", (await Assert.ThrowsAsync<InvalidOperationException>(() => speech.Recognize(Wave(), CancellationToken.None))).Message);
        speech.Save(new SpeechSettings(Executable: "does-not-exist-janet-test"));
        Assert.Contains("not found", (await Assert.ThrowsAsync<InvalidOperationException>(() => speech.Synthesize("hello", CancellationToken.None))).Message);
        speech.Save(new SpeechSettings(Engine: "off"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => speech.Synthesize("hello", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => speech.InstallModel("unknown", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => speech.InstallModel("fr", CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(app.Directory, "speech-models")));
    }
}

public class SpeechApiTests : IDisposable
{
    readonly TempApp _app = new();
    readonly RecordingExecutor _executor = new();
    readonly HttpClient _client = new();
    readonly WebServer _server;
    readonly AppConfigStore _config;
    readonly SettingsStore _settings;
    readonly SilentSpeaker _speaker = new();

    public SpeechApiTests() {
        _config = _app.NewConfig(); _settings = new SettingsStore(_app.Paths, _app.Log);
        int port = Ports.Free(); _config.Update(new CommUpdate { Hostname = "localhost", HttpPort = port.ToString() });
        _server = new WebServer(_config, _settings, _app.Paths, () => _executor, _app.Log, isMuted: () => _speaker.Muted);
        _client.BaseAddress = new Uri("http://localhost:" + port);
    }

    public void Dispose() { _server.Dispose(); _client.Dispose(); _app.Dispose(); }

    [Fact]
    public async Task SpeechStatusFollowsMuteAndMutedSynthesisDoesNotStartAnEngine() {
        _server.Start();
        foreach (bool muted in new[] { false, true, false }) {
            _speaker.Muted = muted;
            using var status = JsonDocument.Parse(await _client.GetStringAsync("/api/speech"));
            Assert.Equal(muted, status.RootElement.GetProperty("muted").GetBoolean());
            if (muted) {
                var audio = await _client.PostAsJsonAsync("/api/speech/synthesize", new { text = "Must not be spoken." });
                Assert.Equal(HttpStatusCode.Conflict, audio.StatusCode);
                Assert.Contains("muted", await audio.Content.ReadAsStringAsync());
            }
        }
        Assert.Empty(_executor.Calls);
    }

    [Fact]
    public async Task VoiceSettingsRoundTripAndNeverExecuteCommands() {
        _server.Start();
        var save = await _client.PostAsJsonAsync("/api/speech/settings", new { engine = "off", executable = "", voice = "en", piperModel = "", recognitionModel = "" });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        using var status = JsonDocument.Parse(await _client.GetStringAsync("/api/speech"));
        Assert.Equal("off", status.RootElement.GetProperty("settings").GetProperty("engine").GetString());
        Assert.False(status.RootElement.GetProperty("modelLoaded").GetBoolean());
        Assert.False(status.RootElement.GetProperty("recognitionReady").GetBoolean());
        var audio = await _client.PostAsync("/api/speech/recognize", new ByteArrayContent(ModernSpeechTests.Wave()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, audio.StatusCode);
        Assert.Contains("No recognition model", await audio.Content.ReadAsStringAsync());
        Assert.Empty(_executor.Calls);
    }

    [Fact]
    public async Task RejectsBadBodiesCrossOriginAndWrongMethod() {
        _server.Start();
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _client.GetAsync("/api/speech/recognize")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/speech/recognize", new ByteArrayContent(new byte[100]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/speech/model", new { language = "unknown" })).StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/speech/settings") { Content = JsonContent.Create(new { engine = "off" }) };
        request.Headers.Add("Origin", "https://other.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(request)).StatusCode);
        Assert.Empty(_executor.Calls);
    }

    [Fact]
    public async Task SpeechEndpointsUseTheExistingAuthentication() {
        _settings.Save(SettingsFiles.WebLogin, "bob\r\npw1");
        _config.Update(new CommUpdate { Authentication = "basic" }); _server.Start();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/speech")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/speech/settings", new { engine = "off" })).StatusCode);
    }

    [Fact]
    public async Task ASpecificLanAddressAlsoAllowsLocalVoiceAccess() {
        var address = Dns.GetHostAddresses(Dns.GetHostName()).FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip));
        if (address == null) return; // Some isolated CI runners have only a loopback adapter.
        _config.Update(new CommUpdate { Hostname = address.ToString() });
        _server.Start();
        Assert.True(_server.IsRunning, _server.Problem?.Message);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/speech")).StatusCode);
    }
}
