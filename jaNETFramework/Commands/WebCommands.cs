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
using System.Collections.Generic;

namespace jaNET.Commands;

/// <summary>judo json add|get: values from JSON web services</summary>
internal sealed class JsonCommand : JudoCommand
{
    public JsonCommand(AppConfigStore config, RemoteData remote) {
        On(i => config.AddInstructionSets(new[] {
            new InstructionSetEntry("*" + i.Args[3], "judo json get " + UriCodec.Encode(i.Args[4]) + " " + i.Args[5]),
            new InstructionSetEntry(i.Args[3], "*" + i.Args[3])
        }), "add", "new", "set", "setup");

        On(i => remote.JsonValue(UriCodec.Decode(i.Args[3]), i.Args[4]), "get", "response", "consume", "extract");
    }

    public override IReadOnlyList<string> Names { get; } = new[] { "json" };
}

/// <summary>judo xml add|get: values from XML web services</summary>
internal sealed class XmlCommand : JudoCommand
{
    readonly AppConfigStore _config;
    readonly RemoteData _remote;

    public XmlCommand(AppConfigStore config, RemoteData remote) {
        _config = config;
        _remote = remote;

        On(Add, "add", "new", "set", "setup");
        On(Get, "get", "response", "consume", "extract");
    }

    public override IReadOnlyList<string> Names { get; } = new[] { "xml" };

    string Add(JudoInvocation i) {
        IReadOnlyList<string> a = i.Args;

        // endpoint, then [namespace] node [attribute]: 3 to 5 arguments after the name
        string? action = i.Count switch {
            6 => "judo xml get " + UriCodec.Encode(a[4]) + " " + a[5],
            7 => "judo xml get " + UriCodec.Encode(a[4]) + " " + a[5] + " " + a[6],
            8 => "judo xml get " + UriCodec.Encode(a[4]) + " " + a[5] + " " + a[6] + " " + a[7],
            _ => null
        };
        if (action == null) return string.Empty;

        return _config.AddInstructionSets(new[] { new InstructionSetEntry("*" + a[3], action), new InstructionSetEntry(a[3], "*" + a[3]) });
    }

    string Get(JudoInvocation i) {
        IReadOnlyList<string> a = i.Args;

        switch (i.Count) {
            case 5:
                return _remote.XmlNode(UriCodec.Decode(a[3]), a[4]) ?? string.Empty;
            case 6:
                return a[4].Contains('=')
                    ? _remote.XmlNodes(UriCodec.Decode(a[3]), a[4], a[5])[0]
                    : _remote.XmlNode(UriCodec.Decode(a[3]), a[4], Convert.ToInt32(a[5])) ?? string.Empty;
            case 7:
                return _remote.XmlNodes(UriCodec.Decode(a[3]), a[4], a[5])[Convert.ToInt32(a[6])];
            default:
                return string.Empty;
        }
    }
}

/// <summary>judo http get [url]</summary>
internal sealed class HttpCommand : JudoCommand
{
    public HttpCommand(IHttpFetcher http) {
        On(i => http.Get(UriCodec.Decode(i.Args[3])), "get");
    }

    public override IReadOnlyList<string> Names { get; } = new[] { "http" };
}

/// <summary>judo noip set|settings|update: dynamic DNS</summary>
internal sealed class DynDnsCommand : JudoCommand
{
    public DynDnsCommand(ISettingsStore settings, DynDnsClient client) {
        On(i => settings.Save(SettingsFiles.DynDns, $"{i.Args[3]}\r\n{i.Args[4]}\r\n{i.Args[5]}"), "add", "new", "setup", "set");
        On(i => {
            DynDnsSettings? s = settings.LoadDynDns();
            return $"{s?.Hostname}\r\n{s?.Username}\r\n{s?.Password}";
        }, "settings");
        On(i => {
            // with host, user and password given they are used, otherwise the saved settings
            DynDnsSettings target = i.Count == 6
                ? new DynDnsSettings(i.Args[3], i.Args[4], i.Args[5])
                : settings.LoadDynDns() ?? new DynDnsSettings(string.Empty, string.Empty, string.Empty);

            _ = client.UpdateAsync(target);       // runs in the background, failures are logged
            return string.Empty;
        }, "update");
    }

    public override IReadOnlyList<string> Names { get; } = new[] { "dyndns", "ddns", "noip", "no-ip" };
}

/// <summary>judo ping [host] [timeout in ms]</summary>
internal sealed class PingCommand : JudoCommand
{
    readonly Pinger _pinger;

    public PingCommand(Pinger pinger) {
        _pinger = pinger;
    }

    public override IReadOnlyList<string> Names { get; } = new[] { "ping" };

    public override string Execute(JudoInvocation i) =>
        (i.Count == 3 ? _pinger.Ping(i.Args[2]) : _pinger.Ping(i.Args[2], Convert.ToInt32(i.Args[3]))).ToString();
}

/// <summary>judo sleep [ms]: pauses, e.g. between the actions of an event</summary>
internal sealed class SleepCommand : JudoCommand
{
    public override IReadOnlyList<string> Names { get; } = new[] { "timer", "sleep" };

    public override string Execute(JudoInvocation i) {
        System.Threading.Thread.Sleep(Convert.ToInt32(i.Args[2]));
        return string.Empty;
    }
}

/// <summary>judo help [topic]</summary>
internal sealed class HelpCommand : JudoCommand
{
    public override IReadOnlyList<string> Names { get; } = new[] { "help", "?" };

    public override string Execute(JudoInvocation i) => HelpText.For(i.Count > 2 ? i.Args[2] : "all");
}
