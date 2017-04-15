/* *****************************************************************************************************************************
 * (c) J@mBeL.net 2010-2017
 * Author: John Ambeliotis
 * Created: 24 Apr. 2010
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

using jaNET.Environment;
using jaNET.IO;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace jaNET.Providers
{
    class Schedule
    {
        internal enum State
        {
            Disable = 0,
            Enable = 1,
            Remove = 2,
            DisableAll = 3,
            EnableAll = 4,
            RemoveAll = 5
        }

        internal struct Period
        {
            internal const string Repeat = "repeat";
            internal const string Interval = "interval";
            internal const string Timer = "timer";
            internal const string Daily = "daily";
            internal const string Everyday = "everyday";
            internal const string Workdays = "workdays";
            internal const string Weekends = "weekend";
        }

        static readonly object _schedule_locker = new object();

        internal string Name { get; set; }
        internal string Date { get; set; }
        internal string Time { get; set; }
        internal string Action { get; set; }
        bool _status;
        public bool Status {
            get { return _status; }
            set {
                if (value != _status) {
                    _status = value;
                    SaveList();
                }
            }
        }

        internal Schedule() {

        }

        internal Schedule(string sName, string sDate, string sTime, string sAction, bool bStatus) {
            Name = sName;
            Date = sDate;
            Time = sTime;
            Action = sAction;
            Status = bStatus;
        }

        static ObservableCollection<Schedule> _ScheduleList;
        internal static ObservableCollection<Schedule> ScheduleList {
            get {
                if (_ScheduleList == null) {
                    _ScheduleList = new ObservableCollection<Schedule>();
                    _ScheduleList.CollectionChanged += ScheduleList_Changed;
                }
                return _ScheduleList;
            }
        }

        static void ScheduleList_Changed(object sender, NotifyCollectionChangedEventArgs e) {
            SaveList();
        }

        internal static void Init() {
            new Thread(() => {
                const string schedulerFilename = ".scheduler";

                if (File.Exists(Methods.Instance.GetApplicationPath + schedulerFilename)) {
                    if (ScheduleList.Count > 0)
                        ScheduleList.Clear();
                    var scheduleSettings = new Settings();
                    var Schedules = scheduleSettings.Load(schedulerFilename);
                    foreach (string schedule in Schedules)
                        if (schedule != string.Empty) {
                            var s = schedule.ToSchedule();
                            int i;
                            if (int.TryParse(s.Time, out i))
                                Add(s, i);
                            else
                                Add(s);
                        }
                }
            }).Start();
        }

        static void SaveList() {
            string schedules = string.Empty;
            const string schedulerPath = ".scheduler";

            var scheduleSettings = new Settings();

            foreach (Schedule schedule in ScheduleList)
                schedules += string.Format("{0} {1} {2} '{3}' {4}\r\n", schedule.Name, schedule.Date, schedule.Time, schedule.Action, schedule.Status);

            scheduleSettings.Save(schedulerPath, schedules);
        }

        internal static string Add(Schedule ss) {
            return Add(ss, 1000);
        }

        internal static string Add(Schedule ss, int interval) {
            interval = interval < 1000 ? 1000 : interval;
            try {
                if (!ScheduleList.Contains(ss))
                    ScheduleList.Add(ss);

                lock (_schedule_locker)
                    Task.Factory.StartNew(() => ScheduleListener(ss, interval));

                return string.Format("Schedule {0} added", ss.Name);
            }
            catch { return string.Format("Failed to add {0} schedule", ss.Name); }
        }

        static void ScheduleListener(Schedule oSchedule, int interval) {
            bool _done = false;

            var WorkDays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
            var Weekend = new[] { DayOfWeek.Saturday, DayOfWeek.Sunday };

            var method = Methods.Instance;

            lock (_schedule_locker) {
                while (oSchedule.Status) {
                    if ((oSchedule.Date == Period.Repeat || oSchedule.Date == Period.Interval || oSchedule.Date == Period.Timer) ||                 // repeated (ms)
                        (oSchedule.Date == Period.Daily || oSchedule.Date == Period.Everyday) && oSchedule.Time == method.GetTime24 ||              // every day
                         oSchedule.Date == Period.Workdays && oSchedule.Time == method.GetTime24 && WorkDays.Contains(DateTime.Now.DayOfWeek) ||    // workdays
                         oSchedule.Date == Period.Weekends && oSchedule.Time == method.GetTime24 && Weekend.Contains(DateTime.Now.DayOfWeek) ||     // weekends
                         oSchedule.Date.ToUpper().Contains(method.GetDay.ToUpper()) && oSchedule.Time == method.GetTime24 ||                        // specific day
                         oSchedule.Date == method.GetCalendarDate && oSchedule.Time == method.GetTime24)                                            // specific date - only once, then deleted
                    {
                        if (!_done) {
                            if (method.GetInstructionSet(oSchedule.Action).Count > 0)
                                oSchedule.Action.Parse();
                            else
                                Parser.Instance.SayText(oSchedule.Action);

                            if (oSchedule.Date != Period.Repeat && oSchedule.Date != Period.Interval && oSchedule.Date != Period.Timer)
                                _done = true;

                            if (oSchedule.Date == method.GetCalendarDate && oSchedule.Time == method.GetTime24)
                                ChangeStatus(oSchedule.Name, State.Remove);
                        }
                    }
                    else
                        _done = false;

                    Monitor.Wait(_schedule_locker, interval);
                }
            }
        }

        internal static string ChangeStatus(State stat) {
            return ChangeStatus(string.Empty, stat);
        }

        internal static string ChangeStatus(string scheduleName, State stat) {
            try {
                switch (stat) {
                    case State.Remove:
                    case State.RemoveAll:
                        for (int i = ScheduleList.Count - 1; i >= 0; i--) {
                            if (ScheduleList[i].Name == scheduleName || stat == State.RemoveAll) {
                                ScheduleList[i].Status = false;
                                ScheduleList.RemoveAt(i);
                            }
                        }
                        break;
                    case State.Enable:
                    case State.Disable:
                        foreach (var s in ScheduleList.Where(s => s.Name == scheduleName)
                                                      .Where(s => s.Status != Convert.ToBoolean(stat))) {
                            s.Status = Convert.ToBoolean(stat);
                            if (s.Status)
                                if (s.Time.Contains(":"))
                                    Add(s);
                                else
                                    Add(s, Convert.ToInt32(s.Time));
                        }
                        break;
                    case State.EnableAll:
                        foreach (var s in ScheduleList.Where(s => !s.Status)) {
                            s.Status = true;
                            Add(s);
                        }
                        break;
                    case State.DisableAll:
                        foreach (var s in ScheduleList.Where(s => s.Status))
                            s.Status = false;
                        break;
                }

                lock (_schedule_locker)
                    Monitor.PulseAll(_schedule_locker);

                return string.Format("Scheduler updated [{0}:{1}]", scheduleName, stat);
            }
            catch { return string.Format("Failed to {0} schedule {1}", stat, scheduleName); }
        }
    }
}