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

using jaNET.Infrastructure;
using System;
using System.Globalization;

namespace jaNET.Services;

/// <summary>The time, date and part-of-day texts behind %time%, %date%, %salute% and friends (current culture, like before).</summary>
internal sealed class TimeText
{
    readonly IClock _clock;

    public TimeText(IClock clock) {
        _clock = clock;
    }

    public string Time => string.Format("{0:t}", _clock.Now);

    public string Time24 => string.Format("{0:HH:mm}", _clock.Now);

    public string Hour => string.Format("{0:HH}", _clock.Now);

    public string Minute => string.Format("{0:mm}", _clock.Now);

    public string Date => string.Format("{0:M}", _clock.Now);

    /// <summary>d/M/yyyy with slashes in every culture: the format of schedule dates, which the scheduler compares against.</summary>
    public string CalendarDate => _clock.Now.ToString("d/M/yyyy", CultureInfo.InvariantCulture);

    public string Day => _clock.Now.DayOfWeek.ToString();

    public string CalendarDay => _clock.Now.Day.ToString();

    public string CalendarMonth => _clock.Now.Month.ToString();

    public string CalendarYear => _clock.Now.Year.ToString();

    /// <summary>"morning", "afternoon", ... as used in greetings (<paramref name="salute"/>) or as a plain part of the day.</summary>
    public string PartOfDay(bool salute) {
        int hour = _clock.Now.Hour;

        if (hour < 6) return salute ? "morning" : "midnight";
        if (hour < 12) return "morning";
        if (hour < 18) return "afternoon";
        if (hour < 20) return "evening";
        return salute ? "evening" : "night";
    }

    public string Salute => PartOfDay(true);
}

/// <summary>How long jaNET has been running (%uptime%).</summary>
internal sealed class Uptime
{
    readonly IClock _clock;
    readonly DateTime _started;

    public Uptime(IClock clock) {
        _clock = clock;
        _started = clock.Now;
    }

    TimeSpan Elapsed => _clock.Now - _started;

    public int Days => Elapsed.Days;
    public int Hours => Elapsed.Hours;
    public int Minutes => Elapsed.Minutes;
    public int Seconds => Elapsed.Seconds;

    public string All {
        get {
            TimeSpan t = Elapsed;
            return $"Days[{t.Days}], Hours[{t.Hours}], Minutes[{t.Minutes}], Seconds[{t.Seconds}]";
        }
    }
}
