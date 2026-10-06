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

using jaNET.Servers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace jaNET.Commands;

/// <summary>judo schedule add|enable|disable|remove|list ...: run instructions at a time or at an interval</summary>
internal sealed class ScheduleCommand : JudoCommand
{
    readonly SchedulerService _scheduler;

    public ScheduleCommand(SchedulerService scheduler) {
        _scheduler = scheduler;

        On(Add, "add", "new", "set", "setup");
        On(i => _scheduler.Change(ScheduleChange.Enable, i.Args[3]), "enable", "activate", "start", "on");
        On(i => _scheduler.Change(ScheduleChange.EnableAll), "enable-all", "activate-all", "start-all", "on-all");
        On(i => _scheduler.Change(ScheduleChange.Disable, i.Args[3]), "disable", "deactivate", "stop", "off");
        On(i => _scheduler.Change(ScheduleChange.DisableAll), "disable-all", "deactivate-all", "stop-all", "off-all");
        On(i => _scheduler.Change(ScheduleChange.Remove, i.Args[3]), "remove", "rm", "delete", "del");
        On(i => _scheduler.Change(ScheduleChange.RemoveAll), "remove-all", "delete-all", "del-all", "cleanup", "clear", "empty");

        On(i => NameList(s => s.Enabled), "active", "actives", "active-list", "active-ls", "list-actives", "ls-actives");
        On(i => NameList(s => !s.Enabled), "inactive", "inactives", "inactive-list", "inactive-ls", "list-inactives", "ls-inactives");
        On(i => NameList(_ => true), "names", "name-list", "name-ls", "list-names", "ls-names");

        On(i => Details(s => s.Enabled), "active-details", "actives-details", "active-list-details", "active-ls-details", "list-actives-details", "ls-actives-details");
        On(i => Details(s => !s.Enabled), "inactive-details", "inactives-details", "inactive-list-details", "inactive-ls-details", "list-inactives-details", "ls-inactives-details");
        On(List, "details", "state", "status", "list", "ls");
        Otherwise(List);
    }

    public override IReadOnlyList<string> Names { get; } = new[] { "schedule" };

    string Add(JudoInvocation i) {
        // judo schedule add [Name] [Date|daily|workdays|weekend|Monday...|repeat] [hh:mm | interval in ms] [instruction]
        Schedule schedule = Schedule.FromArguments(i.Args.Skip(3).ToList());

        return schedule.IsRepeating
            ? _scheduler.Add(schedule, Convert.ToInt32(schedule.Time))
            : _scheduler.Add(schedule);
    }

    string NameList(Func<Schedule, bool> filter) =>
        string.Concat(_scheduler.Schedules.Where(filter).Select(s => s.Name + "\r\n"));

    string Details(Func<Schedule, bool> filter) =>
        string.Concat(_scheduler.Schedules.Where(filter).Select(Row));

    // all schedules, or the one named after the sub command
    string List(JudoInvocation i) =>
        string.Concat(_scheduler.Schedules.Where(s => i.Count <= 3 || i.Args[3] == s.Name).Select(Row));

    static string Row(Schedule s) =>
        $"{s.Name} | {s.Date} | {s.Time} | {s.Action} | {(s.Enabled ? "Active" : "Inactive")}\r\n";
}
