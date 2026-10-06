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
using jaNET.Servers;
using jaNET.Services;
using System;
using System.Linq;
using System.Threading;
using Xunit;

namespace jaNETFramework.Tests;

public class ScheduleRulesTests
{
    // Thursday 1 October 2026
    static readonly DateTime Thursday0830 = new DateTime(2026, 10, 1, 8, 30, 0);

    static Schedule S(string date, string time) => new Schedule { Name = "s", Date = date, Time = time, Action = "a", Enabled = true };

    [Theory]
    [InlineData("daily", "08:30", true)]
    [InlineData("everyday", "08:30", true)]
    [InlineData("daily", "8:30", true)]            // leading zeros do not matter
    [InlineData("daily", "08:31", false)]
    [InlineData("daily", "not a time", false)]
    [InlineData("workdays", "08:30", true)]
    [InlineData("weekend", "08:30", false)]
    [InlineData("thursday", "08:30", true)]
    [InlineData("monday", "08:30", false)]
    [InlineData("1/10/2026", "08:30", true)]
    [InlineData("2/10/2026", "08:30", false)]
    public void TimeBasedSchedulesAreDueDuringTheirMinute(string date, string time, bool due) {
        Assert.Equal(due, ScheduleRules.IsDue(S(date, time), Thursday0830));
    }

    [Theory]
    [InlineData("repeat")]
    [InlineData("interval")]
    [InlineData("timer")]
    public void RepeatingSchedulesAreDueOnEveryRound(string date) {
        Assert.True(ScheduleRules.IsDue(S(date, "5000"), Thursday0830));
    }

    [Fact]
    public void WeekendAndWorkdaysFollowTheDayOfTheWeek() {
        var saturday = new DateTime(2026, 10, 3, 8, 30, 0);

        Assert.True(ScheduleRules.IsDue(S("weekend", "08:30"), saturday));
        Assert.False(ScheduleRules.IsDue(S("workdays", "08:30"), saturday));
    }

    [Fact]
    public void OnlyTheDateScheduleCountsAsToday() {
        Assert.True(ScheduleRules.IsToday(S("1/10/2026", "08:30"), Thursday0830));
        Assert.False(ScheduleRules.IsToday(S("daily", "08:30"), Thursday0830));
    }
}

public class SchedulerServiceTests : IDisposable
{
    readonly TempApp _app = new TempApp();
    readonly FakeClock _clock = new FakeClock();                 // Thursday 08:30
    readonly MemorySettingsStore _settings = new MemorySettingsStore();
    readonly RecordingExecutor _executor = new RecordingExecutor();
    readonly SilentSpeaker _speaker = new SilentSpeaker();
    readonly AppConfigStore _config;
    readonly SchedulerService _scheduler;

    public SchedulerServiceTests() {
        _config = _app.NewConfig();
        _config.AddInstructionSets(new[] { new InstructionSetEntry("known", "*known") });
        _scheduler = New();
    }

    SchedulerService New() => new SchedulerService(_settings, _clock, _config, _speaker, () => _executor, _app.Log, minimumIntervalMs: 20);

    static Schedule S(string name, string date, string time, string action = "known", bool enabled = true) =>
        new Schedule { Name = name, Date = date, Time = time, Action = action, Enabled = enabled };

    public void Dispose() {
        _scheduler.Dispose();
        _app.Dispose();
    }

    [Fact]
    public void RunsATimeBasedScheduleOncePerMatchingMinute() {
        _scheduler.Add(S("s1", "daily", "08:30"), 20);

        Assert.True(SpinWait.SpinUntil(() => _executor.Calls.Count >= 1, 2000));
        Thread.Sleep(200);
        Assert.Equal(new[] { "known" }, _executor.Calls);          // many rounds, one run

        _clock.Now = _clock.Now.AddMinutes(1);                     // the minute is over ...
        Thread.Sleep(100);
        _clock.Now = _clock.Now.AddMinutes(-1);                    // ... and comes around again
        Assert.True(SpinWait.SpinUntil(() => _executor.Calls.Count == 2, 2000));
    }

    [Fact]
    public void DoesNotRunWhenTheTimeIsWrong() {
        _scheduler.Add(S("s1", "daily", "09:00"));

        Thread.Sleep(200);

        Assert.Empty(_executor.Calls);
    }

    [Fact]
    public void RepeatsAtItsInterval() {
        _scheduler.Add(S("r", "repeat", "30"), 30);

        Assert.True(SpinWait.SpinUntil(() => _executor.Calls.Count >= 4, 3000));
    }

    [Fact]
    public void ADatedScheduleRunsOnceAndIsRemoved() {
        _scheduler.Add(S("once", "1/10/2026", "08:30"), 20);

        Assert.True(SpinWait.SpinUntil(() => _scheduler.Schedules.Count == 0, 2000));
        Assert.Equal(new[] { "known" }, _executor.Calls);
    }

    [Fact]
    public void ActionsThatAreNoInstructionSetAreSpoken() {
        _scheduler.Add(S("say", "daily", "08:30", action: "Time for tea"));

        Assert.True(SpinWait.SpinUntil(() => _speaker.Spoken.Contains("Time for tea"), 2000));
        Assert.Empty(_executor.Calls);
    }

    [Fact]
    public void DisabledSchedulesDoNotRunUntilEnabled() {
        _scheduler.Add(S("s1", "daily", "08:30", enabled: false));
        Thread.Sleep(150);
        Assert.Empty(_executor.Calls);

        Assert.Equal("Scheduler updated [s1:Enable]", _scheduler.Change(ScheduleChange.Enable, "s1"));

        Assert.True(SpinWait.SpinUntil(() => _executor.Calls.Count >= 1, 2000));
    }

    [Fact]
    public void DisablingStopsAScheduleThatIsRunning() {
        _scheduler.Add(S("r", "repeat", "30"), 30);
        Assert.True(SpinWait.SpinUntil(() => _executor.Calls.Count >= 1, 2000));

        _scheduler.Change(ScheduleChange.Disable, "r");
        Thread.Sleep(100);
        int callsAfterStop = _executor.Calls.Count;
        Thread.Sleep(200);

        Assert.Equal(callsAfterStop, _executor.Calls.Count);
        Assert.False(_scheduler.Schedules.Single().Enabled);
    }

    [Fact]
    public void EnablingTwiceDoesNotDoubleTheRuns() {
        _scheduler.Add(S("r", "repeat", "100"), 100);
        _scheduler.Change(ScheduleChange.Disable, "r");
        _scheduler.Change(ScheduleChange.Enable, "r");
        _scheduler.Change(ScheduleChange.Disable, "r");
        _scheduler.Change(ScheduleChange.Enable, "r");
        Thread.Sleep(50);
        _executor.Calls.Clear();

        Thread.Sleep(550);

        Assert.InRange(_executor.Calls.Count, 3, 7);               // one loop: about 5 runs, two loops would give about 10
    }

    [Fact]
    public void ChangeReportsWhatItDid() {
        _scheduler.Add(S("a", "daily", "09:00"));
        _scheduler.Add(S("b", "daily", "10:00"));

        Assert.Equal("Scheduler updated [a:Disable]", _scheduler.Change(ScheduleChange.Disable, "a"));
        Assert.Equal("Scheduler updated [:EnableAll]", _scheduler.Change(ScheduleChange.EnableAll));
        Assert.Equal("Scheduler updated [:DisableAll]", _scheduler.Change(ScheduleChange.DisableAll));
        Assert.Equal("Scheduler updated [b:Remove]", _scheduler.Change(ScheduleChange.Remove, "b"));
        Assert.Equal(new[] { "a" }, _scheduler.Schedules.Select(s => s.Name));
        Assert.Equal("Scheduler updated [:RemoveAll]", _scheduler.Change(ScheduleChange.RemoveAll));
        Assert.Empty(_scheduler.Schedules);
    }

    [Fact]
    public void AddingReportsTheNameAndKeepsASingleCopy() {
        var s = S("a", "daily", "09:00");

        Assert.Equal("Schedule a added", _scheduler.Add(s));
        _scheduler.Add(s);

        Assert.Single(_scheduler.Schedules);
    }

    [Fact]
    public void SchedulesSurviveARestart() {
        _scheduler.Add(S("a", "daily", "09:00"));
        _scheduler.Add(S("b", "Monday", "10:15", action: "Good morning", enabled: false));
        _scheduler.Add(S("c", "repeat", "60000"), 60000);

        using var restarted = New();
        restarted.Load();

        Assert.Equal(new[] { "a", "b", "c" }, restarted.Schedules.Select(s => s.Name));
        Assert.Equal("Good morning", restarted.Schedules[1].Action);
        Assert.Equal("monday", restarted.Schedules[1].Date);
        Assert.Equal(new[] { true, false, true }, restarted.Schedules.Select(s => s.Enabled));
    }

    [Fact]
    public void TheFileFormatIsTheOneOfThePreviousVersions() {
        _scheduler.Add(S("a", "daily", "09:00", action: "say hi"));

        Assert.Equal("a daily 09:00 'say hi' True", _settings.Load(SettingsFiles.Scheduler)[0]);
    }

    [Fact]
    public void AFailingActionDoesNotEndTheSchedule() {
        int calls = 0;
        _executor.Answer = _ => { calls++; throw new InvalidOperationException("boom"); };
        _scheduler.Add(S("r", "repeat", "30"), 30);

        Assert.True(SpinWait.SpinUntil(() => calls >= 3, 3000));
        Assert.Contains(_app.Log.Entries, e => e.Contains("boom"));
    }
}
