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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace jaNET.Services;

/// <summary>
/// The OpenWeatherMap API key lives in the encrypted settings (.weathersettings), not in the URL in AppConfig.xml: the URL keeps
/// "APPID=" with a placeholder and the stored key is put in when the request is made.
/// </summary>
internal static class WeatherKey
{
    public const string Placeholder = "YOUR_OPENWEATHERMAP_API_KEY";

    static readonly Regex Value = new(@"(?<=[?&]APPID=)[^&]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Real = new(@"(?<=[?&]APPID=)[0-9a-f]{32}(?=&|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? Stored(ISettingsStore settings) {
        string? key = settings.Load(SettingsFiles.Weather)?.FirstOrDefault()?.Trim();
        return string.IsNullOrEmpty(key) ? null : key;
    }

    /// <summary>The URL with the key in it; without a key the URL is returned as it is.</summary>
    public static string Apply(string url, string? key) {
        if (url.Length == 0 || string.IsNullOrEmpty(key)) return url;
        if (Value.IsMatch(url)) return Value.Replace(url, Uri.EscapeDataString(key), 1);
        return url + (url.Contains('?') ? "&" : "?") + "APPID=" + Uri.EscapeDataString(key);
    }

    /// <summary>A real key in the URL (as older versions kept it): the key, and the URL with the placeholder instead.</summary>
    public static bool TryTake(string url, out string key, out string cleaned) {
        Match match = Real.Match(url);
        key = match.Value;
        cleaned = match.Success ? Real.Replace(url, Placeholder, 1) : url;
        return match.Success;
    }

    public static bool IsPlaceholder(string url) => url.Contains(Placeholder, StringComparison.Ordinal);
}

/// <summary>What the weather functions (%todayconditions%, %currenttemperature%, ...) expand to. Empty when unavailable.</summary>
internal sealed class WeatherReport
{
    public string TodayConditions { get; init; } = string.Empty;
    public string TodayLow { get; init; } = string.Empty;
    public string TodayHigh { get; init; } = string.Empty;
    public string TodayDay { get; init; } = string.Empty;
    public string TomorrowConditions { get; init; } = string.Empty;
    public string TomorrowLow { get; init; } = string.Empty;
    public string TomorrowHigh { get; init; } = string.Empty;
    public string TomorrowDay { get; init; } = string.Empty;
    public string CurrentTemp { get; init; } = string.Empty;
    public string CurrentPressure { get; init; } = string.Empty;
    public string CurrentHumidity { get; init; } = string.Empty;
    public string CurrentCity { get; init; } = string.Empty;
    public string WeatherIcon { get; init; } = string.Empty;
}

internal interface IWeatherSource
{
    WeatherReport Current();
}

/// <summary>Reads the OpenWeatherMap "current weather" answer of the configured URL. Answers are reused for 30 seconds.</summary>
internal sealed class OpenWeatherSource : IWeatherSource
{
    static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    readonly AppConfigStore _config;
    readonly IHttpFetcher _http;
    readonly IClock _clock;
    readonly ISettingsStore? _settings;
    readonly object _gate = new();

    WeatherReport? _report;
    DateTime _fetchedAt;

    public OpenWeatherSource(AppConfigStore config, IHttpFetcher http, IClock clock, ISettingsStore? settings = null) {
        _config = config;
        _http = http;
        _clock = clock;
        _settings = settings;
    }

    public WeatherReport Current() {
        lock (_gate) {
            if (_report != null && _clock.Now - _fetchedAt < Lifetime) return _report;

            _report = Fetch();
            _fetchedAt = _clock.Now;
            return _report;
        }
    }

    WeatherReport Fetch() {
        DateTime now = _clock.Now;
        var report = new WeatherReport {
            TodayDay = now.DayOfWeek.ToString(),
            TomorrowDay = now.AddDays(1).DayOfWeek.ToString()
        };

        try {
            string url = WeatherKey.Apply(_config.WeatherUrl, _settings == null ? null : WeatherKey.Stored(_settings));
            Root? root = url.Length == 0 ? null : JsonSerializer.Deserialize<Root>(_http.Get(url), JsonOptions);
            if (root?.Main == null || root.Weather == null || root.Weather.Count == 0) return report;

            string Number(double value) => Math.Round(value, 1).ToString(CultureInfo.InvariantCulture);

            return new WeatherReport {
                TodayDay = report.TodayDay,
                TomorrowDay = report.TomorrowDay,
                TodayConditions = root.Weather[0].Main ?? string.Empty,
                TodayHigh = Number(root.Main.TempMax),
                TodayLow = Number(root.Main.TempMin),
                CurrentCity = root.Name ?? string.Empty,
                CurrentTemp = Number(root.Main.Temp),
                CurrentHumidity = root.Main.Humidity.ToString(CultureInfo.CurrentCulture),
                CurrentPressure = root.Main.Pressure.ToString(CultureInfo.InvariantCulture),
                WeatherIcon = "http://openweathermap.org/img/w/" + root.Weather[0].Icon + ".png"
            };
        }
        catch (Exception) {
            // no connection, no or an unusable answer: the functions stay empty
            return report;
        }
    }

    sealed class Root
    {
        public List<WeatherEntry>? Weather { get; set; }
        public MainEntry? Main { get; set; }
        public string? Name { get; set; }
    }

    sealed class WeatherEntry
    {
        public string? Main { get; set; }
        public string? Icon { get; set; }
    }

    sealed class MainEntry
    {
        public double Temp { get; set; }
        public double Pressure { get; set; }
        public int Humidity { get; set; }
        [JsonPropertyName("temp_min")] public double TempMin { get; set; }
        [JsonPropertyName("temp_max")] public double TempMax { get; set; }
    }
}
