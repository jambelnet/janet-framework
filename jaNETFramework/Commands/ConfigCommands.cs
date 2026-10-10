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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace jaNET.Commands;

/// <summary>judo inset add|remove|list: instruction sets (and their "*launchers")</summary>
internal sealed class InstructionSetCommand : JudoCommand
{
    readonly AppConfigStore _config;

    public InstructionSetCommand(AppConfigStore config) {
        _config = config;

        On(Add, "add", "new", "set", "setup");
        On(i => _config.RemoveInstructionSet(i.Args[3]), "remove", "rm", "delete", "del", "kill");
        On(List, "list", "ls");
        Otherwise(List);
    }

    public override IReadOnlyList<string> Names { get; } = new[] { "inset" };

    string Add(JudoInvocation i) {
        IReadOnlyList<string> a = i.Args;

        // judo inset add [ID] <lock>[Action]</lock> [Category] [Header] [Short description] [Description] [Thumbnail url] [Reference]
        InstructionSetEntry callable = i.Count == 5
            ? new InstructionSetEntry(a[3], "*" + a[3])
            : new InstructionSetEntry(a[3], "*" + a[3], Category: a[5], Header: a[6], ShortDescription: a[7], Description: a[8], Thumbnail: a[9], Reference: a[10]);

        return _config.AddInstructionSets(new[] { new InstructionSetEntry("*" + a[3], a[4]), callable });
    }

    string List(JudoInvocation i) => string.Concat(_config.InstructionSetXml().Select(xml => xml + "\r\n"));
}

/// <summary>judo event add|remove|list: event handlers such as oncheckin</summary>
internal sealed class EventCommand : JudoCommand
{
    readonly AppConfigStore _config;

    public EventCommand(AppConfigStore config) {
        _config = config;

        On(i => _config.AddEvent(i.Args[3], i.Args[4]), "add", "new", "set", "setup");
        On(i => _config.RemoveEvent(i.Args[3]), "remove", "rm", "delete", "del", "kill");
        On(List, "list", "ls");
        Otherwise(List);
    }

    public override IReadOnlyList<string> Names { get; } = new[] { "event" };

    string List(JudoInvocation i) => string.Concat(_config.EventXml().Select(xml => xml + "\r\n"));
}

/// <summary>judo trusted settings</summary>
internal sealed class TrustedCommand : JudoCommand
{
    public TrustedCommand(AppConfigStore config) {
        On(i => config.Comm.Trusted, "settings");
    }

    public override IReadOnlyList<string> Names { get; } = new[] { "trusted" };
}

/// <summary>judo weather set|settings|key: the address of the weather service and the API key (kept encrypted, never shown)</summary>
internal sealed class WeatherCommand : JudoCommand
{
    public WeatherCommand(AppConfigStore config, ISettingsStore settings) {
        On(i => config.UpdateWeatherUrl(i.Args[3]), "add", "new", "set", "setup");
        On(i => config.WeatherUrl, "settings");
        On(i => i.Count > 3 ? config.UpdateWeatherLocation(i.Args[3]) : config.WeatherLocation, "location");
        On(i => {
            if (i.Count < 5 || !double.TryParse(i.Args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude) ||
                !double.TryParse(i.Args[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude) ||
                !double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180)
                return "Weather: enter a latitude from -90 to 90 and longitude from -180 to 180.";
            string url = "https://api.open-meteo.com/v1/forecast?latitude=" + latitude.ToString(CultureInfo.InvariantCulture) +
                "&longitude=" + longitude.ToString(CultureInfo.InvariantCulture) +
                "&current=temperature_2m,relative_humidity_2m,pressure_msl,surface_pressure,weather_code,is_day" +
                "&daily=temperature_2m_max,temperature_2m_min,weather_code&timezone=auto&forecast_days=2";
            if (i.Count > 5) config.UpdateWeatherLocation(i.Args[5]);
            return config.UpdateWeatherUrl(url);
        }, "openmeteo");
        On(i => i.Count > 3
                ? settings.Save(SettingsFiles.Weather, i.Args[3])
                : "Weather API key: " + (WeatherKey.Stored(settings) == null ? "not set" : "set"), "key");
    }

    public override IReadOnlyList<string> Names { get; } = new[] { "weather" };
}

/// <summary>judo mailheaders set|settings: sender, recipient and subject of notification mails</summary>
internal sealed class MailHeadersCommand : JudoCommand
{
    public MailHeadersCommand(AppConfigStore config) {
        On(i => config.UpdateMailHeaders(i.Args[3], i.Args[4], i.Args[5]), "add", "new", "set", "setup");
        On(i => $"{config.MailHeaders.From}\r\n{config.MailHeaders.To}\r\n{config.MailHeaders.Subject}", "settings");
    }

    public override IReadOnlyList<string> Names { get; } = new[] { "mailheaders", "mailheader" };
}
