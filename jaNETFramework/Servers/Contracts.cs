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

using jaNET.Hosting;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace jaNET.Servers;

/// <summary>A service with a listening state: the web server and the socket server.</summary>
internal interface IServer
{
    bool IsRunning { get; }

    /// <summary>Why the last <see cref="Start"/> failed; null while the service runs, after <see cref="Stop"/> and before the first start.</summary>
    ServiceProblem? Problem => null;

    void Start();

    void Stop();
}

internal enum SerialMessageKind
{
    None,
    Send,
    Listen,
    Monitor
}

/// <summary>The serial port (Arduino and friends).</summary>
internal interface ISerialService
{
    bool IsOpen { get; }

    /// <summary>Why the last <see cref="Open"/> failed; null while the port is open, after <see cref="Close"/> and before the first try.</summary>
    ServiceProblem? Problem => null;

    /// <param name="portName">Port to open, or empty for the configured one</param>
    void Open(string portName);

    void Close();

    /// <summary>Sends a line (kind Send) and/or waits up to <paramref name="timeoutMs"/> for the answer line.</summary>
    string Write(string message, SerialMessageKind kind, int timeoutMs = 1000);
}

internal enum ScheduleChange
{
    Disable = 0,
    Enable = 1,
    Remove = 2,
    DisableAll = 3,
    EnableAll = 4,
    RemoveAll = 5
}

/// <summary>One entry of the scheduler: when (date + time, or an interval) and what to run.</summary>
internal sealed class Schedule
{
    public const string Repeat = "repeat";
    public const string Interval = "interval";
    public const string Timer = "timer";
    public const string Daily = "daily";
    public const string Everyday = "everyday";
    public const string Workdays = "workdays";
    public const string Weekends = "weekend";

    public string Name { get; init; } = string.Empty;

    /// <summary>"daily", "workdays", "weekend", a day name, a date (d/M/yyyy) or "repeat"/"interval"/"timer".</summary>
    public string Date { get; init; } = string.Empty;

    /// <summary>hh:mm, or the interval in milliseconds for repeating schedules.</summary>
    public string Time { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public bool Enabled { get; set; }

    public bool IsRepeating => Date is Repeat or Interval or Timer;

    /// <summary>name date time 'action' [True|False], quotes already removed by the argument splitter.</summary>
    public static Schedule FromArguments(IReadOnlyList<string> args) => new() {
        Name = args[0],
        Date = NormalizeDate(args[1].ToLowerInvariant()),
        Time = args[2],
        Action = args[3].Replace("\"", string.Empty).Replace("'", string.Empty),
        Enabled = args.Count > 4 ? Convert.ToBoolean(args[4]) : true
    };

    /// <summary>dd/MM/yyyy (also with - or . as separators) becomes d/M/yyyy; anything else is kept.</summary>
    public static string NormalizeDate(string date) {
        string candidate = date.Replace("-", "/").Replace(".", "/");

        return DateTime.TryParseExact(candidate, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
            ? parsed.ToString("d/M/yyyy", CultureInfo.InvariantCulture)
            : date;
    }
}
