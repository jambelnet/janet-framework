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
using jaNET.Scripting;
using jaNET.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace jaNET.Servers;

/// <summary>When a schedule is due. Pure logic, separated so that it can be tested with a fixed time.</summary>
internal static class ScheduleRules
{
    static readonly DayOfWeek[] WorkDays = { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
    static readonly DayOfWeek[] Weekend = { DayOfWeek.Saturday, DayOfWeek.Sunday };

    /// <summary>
    /// Repeating schedules are due on every round; the others during the minute they name:
    /// daily/everyday, workdays, weekend, a day name ("Monday") or a date (d/M/yyyy).
    /// </summary>
    public static bool IsDue(Schedule s, DateTime now) {
        if (s.IsRepeating) return true;
        if (!TimeMatches(s.Time, now)) return false;

        return s.Date is Schedule.Daily or Schedule.Everyday
            || (s.Date == Schedule.Workdays && WorkDays.Contains(now.DayOfWeek))
            || (s.Date == Schedule.Weekends && Weekend.Contains(now.DayOfWeek))
            || s.Date.Contains(now.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase)
            || IsToday(s, now);
    }

    /// <summary>A schedule for one date: it is removed after it ran.</summary>
    public static bool IsToday(Schedule s, DateTime now) =>
        s.Date == now.ToString("d/M/yyyy", System.Globalization.CultureInfo.InvariantCulture);

    // "8:30" and "08:30" are the same time
    static bool TimeMatches(string time, DateTime now) =>
        TimeSpan.TryParse(time, out TimeSpan t) && t.Hours == now.Hour && t.Minutes == now.Minute;
}

/// <summary>
/// Runs instructions at the times and intervals of the schedules. The list is kept in the encrypted ".scheduler" file,
/// so schedules survive a restart. Every enabled schedule has its own loop that wakes up once per interval.
/// </summary>
internal sealed class SchedulerService : IDisposable
{
    readonly ISettingsStore _settings;
    readonly IClock _clock;
    readonly AppConfigStore _config;
    readonly ISpeaker _speaker;
    readonly Func<IInstructionExecutor> _executor;
    readonly ILog _log;
    readonly object _gate = new();
    readonly List<Schedule> _schedules = new();
    readonly Dictionary<Schedule, CancellationTokenSource> _loops = new();
    readonly int _minimumIntervalMs;

    /// <param name="minimumIntervalMs">Shortest time between two looks at the clock; tests use a smaller value than the default second.</param>
    public SchedulerService(ISettingsStore settings, IClock clock, AppConfigStore config, ISpeaker speaker, Func<IInstructionExecutor> executor, ILog log, int minimumIntervalMs = 1000) {
        _minimumIntervalMs = minimumIntervalMs;
        _settings = settings;
        _clock = clock;
        _config = config;
        _speaker = speaker;
        _executor = executor;
        _log = log;
    }

    /// <summary>A copy of the schedules at this moment.</summary>
    public IReadOnlyList<Schedule> Schedules {
        get { lock (_gate) return _schedules.ToList(); }
    }

    /// <summary>Loads the saved schedules and starts the enabled ones.</summary>
    public void Load() {
        IReadOnlyList<string>? lines = _settings.Load(SettingsFiles.Scheduler);
        if (lines == null) return;

        foreach (string line in lines.Where(l => l.Length > 0)) {
            Schedule schedule = Schedule.FromArguments(ArgumentSplitter.Split(line));
            Add(schedule, int.TryParse(schedule.Time, out int interval) ? interval : 1000);
        }
    }

    /// <param name="intervalMs">How often a "repeat" schedule fires, or how often time based schedules look at the clock</param>
    public string Add(Schedule schedule, int intervalMs = 1000) {
        try {
            lock (_gate) {
                if (!_schedules.Contains(schedule)) _schedules.Add(schedule);
                Save();
                if (schedule.Enabled) StartLoop(schedule, Math.Max(_minimumIntervalMs, intervalMs));
            }
            return $"Schedule {schedule.Name} added";
        }
        catch (Exception e) {
            _log.Write($"obj [ SchedulerService.Add <{e.GetType().Name}> ] Exception Message: [ {e.Message} ]");
            return $"Failed to add {schedule.Name} schedule";
        }
    }

    public string Change(ScheduleChange change, string name = "") {
        try {
            lock (_gate) {
                switch (change) {
                    case ScheduleChange.Remove:
                    case ScheduleChange.RemoveAll:
                        foreach (Schedule s in _schedules.Where(s => change == ScheduleChange.RemoveAll || s.Name == name).ToList()) {
                            s.Enabled = false;
                            StopLoop(s);
                            _schedules.Remove(s);
                        }
                        break;
                    case ScheduleChange.Enable:
                        foreach (Schedule s in _schedules.Where(s => s.Name == name && !s.Enabled).ToList())
                            Enable(s);
                        break;
                    case ScheduleChange.Disable:
                        foreach (Schedule s in _schedules.Where(s => s.Name == name && s.Enabled).ToList())
                            Disable(s);
                        break;
                    case ScheduleChange.EnableAll:
                        foreach (Schedule s in _schedules.Where(s => !s.Enabled).ToList())
                            Enable(s);
                        break;
                    case ScheduleChange.DisableAll:
                        foreach (Schedule s in _schedules.Where(s => s.Enabled).ToList())
                            Disable(s);
                        break;
                }
                Save();
            }
            return $"Scheduler updated [{name}:{change}]";
        }
        catch (Exception e) {
            _log.Write($"obj [ SchedulerService.Change <{e.GetType().Name}> ] Exception Message: [ {e.Message} ]");
            return $"Failed to {change} schedule {name}";
        }
    }

    public void Dispose() {
        lock (_gate) {
            foreach (CancellationTokenSource loop in _loops.Values) loop.Cancel();
            _loops.Clear();
        }
    }

    void Enable(Schedule s) {
        s.Enabled = true;
        StartLoop(s, int.TryParse(s.Time, out int interval) ? Math.Max(_minimumIntervalMs, interval) : _minimumIntervalMs);
    }

    void Disable(Schedule s) {
        s.Enabled = false;
        StopLoop(s);
    }

    void StartLoop(Schedule schedule, int intervalMs) {
        StopLoop(schedule);                          // never two loops for one schedule

        var cts = new CancellationTokenSource();
        _loops[schedule] = cts;
        _ = Task.Run(() => Loop(schedule, intervalMs, cts.Token));
    }

    void StopLoop(Schedule schedule) {
        if (_loops.Remove(schedule, out CancellationTokenSource? cts)) cts.Cancel();
    }

    async Task Loop(Schedule schedule, int intervalMs, CancellationToken cancel) {
        bool done = false;     // a time based schedule runs once per matching minute, not once per round

        while (!cancel.IsCancellationRequested) {
            DateTime now = _clock.Now;

            if (ScheduleRules.IsDue(schedule, now)) {
                if (!done) {
                    Run(schedule);
                    done |= !schedule.IsRepeating;

                    // a schedule for one date is finished after it ran
                    if (ScheduleRules.IsToday(schedule, now) && !cancel.IsCancellationRequested)
                        Change(ScheduleChange.Remove, schedule.Name);
                }
            }
            else
                done = false;

            try { await Task.Delay(intervalMs, cancel).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    void Run(Schedule schedule) {
        try {
            // the action is the name of an instruction set, otherwise it is a sentence to say
            if (_config.InstructionActions(schedule.Action).Count > 0)
                _executor().Run(schedule.Action);
            else
                _speaker.Say(schedule.Action);
        }
        catch (Exception e) {
            _log.Write($"obj [ SchedulerService.Run <{e.GetType().Name}> ] Schedule: [ {schedule.Name} ] Exception Message: [ {e.Message} ]");
        }
    }

    // name date time 'action' enabled, one line each, as the previous versions wrote them
    void Save() {
        var text = new StringBuilder();
        foreach (Schedule s in _schedules)
            text.Append($"{s.Name} {s.Date} {s.Time} '{s.Action}' {s.Enabled}\r\n");

        _settings.Save(SettingsFiles.Scheduler, text.ToString());
    }
}
