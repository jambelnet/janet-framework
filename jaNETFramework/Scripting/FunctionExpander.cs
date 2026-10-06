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
using jaNET.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace jaNET.Scripting;

/// <summary>
/// Replaces the built-in %functions% in a text by their values: %user%, %time%, %todaytemp%, %gmailcount%, ...
/// Functions that only do something (%mute%, %checkin%, %clear%, %exit%) run first, in the order below, and vanish from the text.
/// </summary>
internal sealed class FunctionExpander
{
    sealed record Effect(string[] Names, Action Run);

    sealed record Value(string[] Names, Func<string> Get)
    {
        public Regex Pattern { get; } = new(string.Join("|", Names.Select(n => "%" + Regex.Escape(n) + "%")), RegexOptions.Compiled);
    }

    static readonly Regex CustomName = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    readonly List<Effect> _effects;
    readonly List<Value> _values;
    readonly Regex _effectPattern;
    readonly AppConfigStore _config;
    readonly Func<IInstructionExecutor> _executor;

    public FunctionExpander(
        AppConfigStore config, Func<IInstructionExecutor> executor, HostLifetime lifetime, IConsoleControl console, ISpeaker speaker,
        UserPresence presence, TimeText time, Uptime uptime, AppInfo info, AppPaths paths, InternetConnection internet,
        MailService mail, IWeatherSource weather, DynDnsClient dynDns, string userName,
        IReadOnlyDictionary<string, Func<string>>? custom = null) {
        _config = config;
        _executor = executor;

        _effects = new List<Effect> {
            new(new[] { "exit", "quit" }, lifetime.RequestStop),
            new(new[] { "clear", "cls" }, console.Clear),
            new(new[] { "mute" }, () => speaker.Muted = true),
            new(new[] { "unmute" }, () => speaker.Muted = false),
            new(new[] { "checkin", "usercheckin" }, () => presence.IsPresent = true),
            new(new[] { "checkout", "usercheckout" }, () => presence.IsPresent = false)
        };

        _values = new List<Value> {
            new(new[] { "inet", "inetcon" }, () => internet.IsAvailable().ToString()),
            new(new[] { "gmailcount", "gcount" }, () => mail.GmailCheck(true)),
            new(new[] { "gmailreader", "gmailheaders", "greader", "gheaders" }, () => mail.GmailCheck(false)),
            new(new[] { "pop3count" }, () => mail.Pop3Check().ToString()),
            new(new[] { "user", "whoami" }, () => userName),
            new(new[] { "time" }, () => time.Time),
            new(new[] { "time24" }, () => time.Time24),
            new(new[] { "hour" }, () => time.Hour),
            new(new[] { "minute" }, () => time.Minute),
            new(new[] { "date" }, () => time.Date),
            new(new[] { "calendardate" }, () => time.CalendarDate),
            new(new[] { "day" }, () => time.Day),
            new(new[] { "calendarday" }, () => time.CalendarDay),
            new(new[] { "calendarmonth" }, () => time.CalendarMonth),
            new(new[] { "calendaryear" }, () => time.CalendarYear),
            new(new[] { "salute" }, () => time.Salute),
            new(new[] { "daypart", "partofday" }, () => time.PartOfDay(false)),
            new(new[] { "todayday" }, () => weather.Current().TodayDay),
            new(new[] { "todayconditions" }, () => weather.Current().TodayConditions),
            new(new[] { "todaylow" }, () => weather.Current().TodayLow),
            new(new[] { "todayhigh" }, () => weather.Current().TodayHigh),
            new(new[] { "currenttemperature", "currenttemp", "todaytemp", "todaytemperature" }, () => weather.Current().CurrentTemp),
            new(new[] { "currenthumidity" }, () => weather.Current().CurrentHumidity),
            new(new[] { "currentpressure" }, () => weather.Current().CurrentPressure),
            new(new[] { "currentcity" }, () => weather.Current().CurrentCity),
            new(new[] { "weathericon" }, () => weather.Current().WeatherIcon),
            new(new[] { "tomorrowday" }, () => weather.Current().TomorrowDay),
            new(new[] { "tomorrowconditions" }, () => weather.Current().TomorrowConditions),
            new(new[] { "tomorrowlow" }, () => weather.Current().TomorrowLow),
            new(new[] { "tomorrowhigh" }, () => weather.Current().TomorrowHigh),
            new(new[] { "whereami", "userstat", "userstatus" }, () => presence.Description),
            new(new[] { "uptime" }, () => uptime.All),
            new(new[] { "updays" }, () => uptime.Days.ToString()),
            new(new[] { "uphours" }, () => uptime.Hours.ToString()),
            new(new[] { "upminutes" }, () => uptime.Minutes.ToString()),
            new(new[] { "upseconds" }, () => uptime.Seconds.ToString()),
            new(new[] { "about", "copyright" }, () => info.Copyright),
            new(new[] { "apppath", "applicationpath" }, () => paths.Root),
            new(new[] { "publicip", "checkip" }, () => dynDns.CheckIpAsync().GetAwaiter().GetResult())
        };

        foreach (var (name, get) in custom ?? new Dictionary<string, Func<string>>())
            AddCustom(name, get);

        // everything that only does something is removed afterwards
        _effectPattern = new Regex(string.Join("|", _effects.SelectMany(e => e.Names).Select(n => "%" + n + "%")), RegexOptions.Compiled);
    }

    // an application can add its own %functions%; their names must be new
    void AddCustom(string name, Func<string> get) {
        if (!CustomName.IsMatch(name))
            throw new ArgumentException($"'{name}' is not a valid function name: use letters, digits and underscores only, without percent signs.");
        if (_effects.Any(e => e.Names.Contains(name)) || _values.Any(v => v.Names.Contains(name)))
            throw new ArgumentException($"A function named %{name}% already exists.");

        _values.Add(new Value(new[] { name }, get));
    }

    /// <summary>All function names including the percent signs, e.g. "%user%" (for completion).</summary>
    public IReadOnlyList<string> Names =>
        _effects.SelectMany(e => e.Names).Concat(_values.SelectMany(v => v.Names)).Select(n => "%" + n + "%").ToList();

    public string Expand(string text) {
        foreach (Effect effect in _effects)
            if (effect.Names.Any(name => text.Contains("%" + name + "%")))
                effect.Run();

        foreach (Value value in _values) {
            if (!value.Pattern.IsMatch(text)) continue;

            string replacement = value.Get();
            text = value.Pattern.Replace(text, _ => replacement);      // a function, so "$1" in a value is not a group reference
        }

        text = _effectPattern.Replace(text, string.Empty);

        if (text.Contains("%~>")) {
            // %~>name% runs the event "name" and leaves nothing behind
            string eventId = text.Replace("%~>", string.Empty).Replace("%", string.Empty);
            string? action = _config.EventActions(eventId).FirstOrDefault();
            if (action != null) _executor().Run(action);
            text = string.Empty;
        }

        return text;
    }
}
