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

/// <summary>Reads OpenWeatherMap or Open-Meteo at the configured URL. Answers are reused for 30 seconds.</summary>
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
    string _cacheKey = string.Empty;

    public OpenWeatherSource(AppConfigStore config, IHttpFetcher http, IClock clock, ISettingsStore? settings = null) {
        _config = config;
        _http = http;
        _clock = clock;
        _settings = settings;
    }

    public WeatherReport Current() {
        lock (_gate) {
            string key = _config.WeatherUrl + "\n" + _config.WeatherLocation + "\n" + (_settings == null ? "" : WeatherKey.Stored(_settings));
            if (_report != null && key == _cacheKey && _clock.Now - _fetchedAt < Lifetime) return _report;

            _report = Fetch();
            _fetchedAt = _clock.Now;
            _cacheKey = key;
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
            string endpoint = _config.WeatherUrl;
            bool meteo = Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) &&
                uri.Host.Equals("api.open-meteo.com", StringComparison.OrdinalIgnoreCase);
            string url = meteo ? endpoint : WeatherKey.Apply(endpoint, _settings == null ? null : WeatherKey.Stored(_settings));
            if (meteo) return ReadMeteo(_http.Get(url), report);
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
                WeatherIcon = "https://openweathermap.org/img/wn/" + root.Weather[0].Icon + "@2x.png"
            };
        }
        catch (Exception) {
            // no connection, no or an unusable answer: the functions stay empty
            return report;
        }
    }

    WeatherReport ReadMeteo(string json, WeatherReport fallback) {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("current", out JsonElement current) || !current.TryGetProperty("temperature_2m", out JsonElement temperature) ||
            temperature.ValueKind != JsonValueKind.Number) return fallback;
        string Number(JsonElement source, string name) => source.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double n)
            ? Math.Round(n, 1).ToString(CultureInfo.InvariantCulture) : string.Empty;
        int code = current.TryGetProperty("weather_code", out JsonElement weatherCode) && weatherCode.ValueKind == JsonValueKind.Number && weatherCode.TryGetInt32(out int c) ? c : -1;
        (string conditions, string icon) Weather(int value) => value switch {
            0 => ("Clear", "01"), 1 or 2 => ("Partly cloudy", "02"), 3 => ("Overcast", "04"),
            45 or 48 => ("Fog", "50"), 51 or 53 or 55 or 56 or 57 => ("Drizzle", "09"),
            61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => ("Rain", "10"),
            71 or 73 or 75 or 77 or 85 or 86 => ("Snow", "13"), 95 or 96 or 99 => ("Thunderstorm", "11"),
            _ => ("Unknown", "03")
        };
        var today = Weather(code);
        string Daily(string name, int index) {
            if (!root.TryGetProperty("daily", out JsonElement daily) || !daily.TryGetProperty(name, out JsonElement values) ||
                values.ValueKind != JsonValueKind.Array || values.GetArrayLength() <= index) return string.Empty;
            JsonElement value = values[index];
            return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double n)
                ? Math.Round(n, 1).ToString(CultureInfo.InvariantCulture) : value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        }
        string Day(int index, string defaultDay) => DateTime.TryParse(Daily("time", index), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)
            ? date.DayOfWeek.ToString() : defaultDay;
        string tomorrow = int.TryParse(Daily("weather_code", 1), out int tomorrowCode) ? Weather(tomorrowCode).conditions : string.Empty;
        bool night = current.TryGetProperty("is_day", out JsonElement isDay) && isDay.ValueKind == JsonValueKind.Number && isDay.TryGetInt32(out int day) && day == 0;
        // OpenWeatherMap's main.pressure is sea-level pressure; preserve that meaning across providers.
        string pressure = Number(current, "pressure_msl");
        if (pressure.Length == 0) pressure = Number(current, "surface_pressure");
        return new WeatherReport {
            CurrentTemp = Number(current, "temperature_2m"), CurrentHumidity = Number(current, "relative_humidity_2m"),
            CurrentPressure = pressure, CurrentCity = _config.WeatherLocation,
            TodayConditions = today.conditions, TodayLow = Daily("temperature_2m_min", 0), TodayHigh = Daily("temperature_2m_max", 0),
            TodayDay = Day(0, fallback.TodayDay), TomorrowDay = Day(1, fallback.TomorrowDay),
            TomorrowConditions = tomorrow, TomorrowLow = Daily("temperature_2m_min", 1), TomorrowHigh = Daily("temperature_2m_max", 1),
            WeatherIcon = "https://openweathermap.org/img/wn/" + today.icon + (night ? "n" : "d") + "@2x.png"
        };
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
