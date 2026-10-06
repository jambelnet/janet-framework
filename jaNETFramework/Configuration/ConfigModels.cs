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

using System;
using System.Collections.Generic;

namespace jaNET.Configuration;

/// <summary>The "Comm" section of AppConfig.xml. Missing values read as empty strings.</summary>
internal sealed record CommSettings(
    string Trusted, string LocalHost, string LocalPort, string Hostname, string HttpPort,
    string Authentication, string ComPort, string BaudRate,
    string HttpsPort = "", string Certificate = "");

/// <summary>
/// The "Mqtt" section: the broker, how to reach it and whether jaNET connects at start-up. <see cref="Tls"/> is "false", "true" or
/// "insecure" (tls without checking the broker's certificate). The login is kept encrypted in .mqttsettings.
/// </summary>
internal sealed record MqttSettings(string Broker, string Port, string Tls, string ClientId, string Enabled)
{
    public bool IsEnabled => Enabled.Equals("true", StringComparison.OrdinalIgnoreCase);

    public bool UsesTls => Tls.Equals("true", StringComparison.OrdinalIgnoreCase) || IsInsecure;

    public bool IsInsecure => Tls.Equals("insecure", StringComparison.OrdinalIgnoreCase);

    public int PortNumber => int.TryParse(Port, out int port) && port is > 0 and <= 65535 ? port : UsesTls ? 8883 : 1883;
}

/// <summary>What to run when a message arrives on a topic (the topic may have the wildcards + and #).</summary>
internal sealed record MqttSubscription(string Topic, string Action);

/// <summary>Values to change in the "Mqtt" section; null or blank entries are left as they are.</summary>
internal sealed record MqttUpdate
{
    public string? Broker { get; init; }
    public string? Port { get; init; }
    public string? Tls { get; init; }
    public string? ClientId { get; init; }
    public string? Enabled { get; init; }
}

internal sealed record MailHeaderSettings(string From, string To, string Subject);

/// <summary>
/// Values to change in the "Comm" section; null or blank entries are left as they are. <see cref="HttpsPort"/> and <see cref="Certificate"/>
/// can also be switched off: pass <see cref="Clear"/>.
/// </summary>
internal sealed record CommUpdate
{
    public string? Trusted { get; init; }
    public string? LocalHost { get; init; }
    public string? LocalPort { get; init; }
    public string? Hostname { get; init; }
    public string? HttpPort { get; init; }
    public string? Authentication { get; init; }
    public string? ComPort { get; init; }
    public string? BaudRate { get; init; }

    /// <summary>The port of the https address; empty (<see cref="Clear"/>) = no https.</summary>
    public string? HttpsPort { get; init; }

    /// <summary>Path of a .pfx/.p12 certificate; empty (<see cref="Clear"/>) = the self-signed certificate that jaNET makes.</summary>
    public string? Certificate { get; init; }

    /// <summary>Marks a value to be emptied (null means "leave it").</summary>
    public const string Clear = "\u0001clear";
}

/// <summary>An instruction set as written to AppConfig.xml. Blank optional values are not written.</summary>
internal sealed record InstructionSetEntry(
    string Id, string Action,
    string? Thumbnail = null, string? Description = null, string? ShortDescription = null,
    string? Header = null, string? Category = null, string? Reference = null);

/// <summary>What the web UI shows of an instruction set (not its action). Blank attributes are null.</summary>
internal sealed record InstructionSetInfo(
    string Id, string? Category, string? Header, string? ShortDescription, string? Description, string? Thumbnail, string? Reference);
