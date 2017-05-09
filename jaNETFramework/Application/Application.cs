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

using jaNET.Environment.AppConfig;
using jaNET.Environment.Core;
using jaNET.Extensions;
using jaNET.IO;
using jaNET.IO.Ports;
using jaNET.Net.Http;
using jaNET.Net.Sockets;
using jaNET.Providers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace jaNET.Environment
{
    public static class Application
    {
        static DateTime _Uptime;

        internal static class Uptime
        {
            internal static string GetAll {
                get {
                    return string.Format("Days[{0}], Hours[{1}], Minutes[{2}], Seconds[{3}]",
                                              GetDays, GetHours, GetMinutes, GetSeconds);
                }
            }

            internal static int GetDays {
                get { return (DateTime.Now - _Uptime).Days; }
            }

            internal static int GetHours {
                get { return (DateTime.Now - _Uptime).Hours; }
            }

            internal static int GetMinutes {
                get { return (DateTime.Now - _Uptime).Minutes; }
            }

            internal static int GetSeconds {
                get { return (DateTime.Now - _Uptime).Seconds; }
            }
        }

        public static async void Initialize() {
            _Uptime = DateTime.Now;

            string AppPath = Methods.Instance.GetApplicationPath;

            if (!File.Exists(AppPath + "AppConfig.xml"))
                await GenerateDumpAppConfig();

            if (!File.Exists(AppPath + ".htaccess"))
                new Settings().Save(".htaccess", "admin\r\nadmin");

            Schedule.Init();

            if (!string.IsNullOrEmpty(Comm.GetHostname))
                WebServer.Start();
            if (!string.IsNullOrEmpty(Comm.GetLocalHost))
                TCP.Start();
            if (!string.IsNullOrEmpty(Comm.GetComPort))
                SerialComm.ActivateSerialPort(string.Empty); // throws exception in linux?

            "%checkin%".ToValues();
        }

        static async Task GenerateDumpAppConfig() {
            // Root
            var root = new AppConfig.jaNET();
            // Nodes
            root.Instructions = new Instructions();
            root.Events = new Events();
            root.System = new AppConfig.System();
            // 
            root.System.Alerts = new Alerts();
            root.System.Alerts.MailHeaders = new MailHeaders {
                MailFrom = "noreply@xxx.org",
                MailTo = "me@yyy.org",
                MailSubject = "Alert from Jubito"
            };
            root.System.Comm = new Comm {
                Trusted = "localhost; 192.168.1.1",
                LocalHost = "localhost",
                LocalPort = "5744",
                Hostname = "localhost",
                HttpPort = "8080",
                Authentication = "none",
                ComPort = "/dev/ttyACM0",
                BaudRate = "9600"
            };
            root.System.Others = new Others();
            root.System.Others.Weather = "http://api.openweathermap.org/data/2.5/weather?q=Athens,GR&units=metric&APPID=44e99238e8b13d4085b1cc545ab9a70c";

            // Events
            var le = new List<Event>();
            le.Add(new Event() {
                Id = "oncheckin",
                Action = "%unmute%; salute; weathertoday"
            });
            le.Add(new Event() {
                Id = "oncheckout",
                Action = "judo sleep 5000; goodbye; %unmute%"
            });
            root.Events.Event = le;

            // Instruction Sets
            var li = new List<InstructionSet>();
            li.Add(new InstructionSet() {
                Id = "*salute",
                Action = "Good %salute% %user%."
            });
            li.Add(new InstructionSet() {
                Id = "salute",
                Action = "*salute"
            });
            li.Add(new InstructionSet() {
                Id = "*goodbye",
                Action = "Good bye %user%."
            });
            li.Add(new InstructionSet() {
                Id = "goodbye",
                Action = "*goodbye"
            });
            li.Add(new InstructionSet() {
                Id = "*whoami",
                Action = "You are, %user%."
            });
            li.Add(new InstructionSet() {
                Id = "*whoamiwidget",
                Action = "%user%"
            });
            li.Add(new InstructionSet() {
                Id = "whoamiwidget",
                Action = "*whoamiwidget"
            });
            li.Add(new InstructionSet() {
                Id = "whoami",
                Action = "*whoami",
                ShortDescription = "Get user login",
                Header = "Login name",
                Category = "System",
                ThumbnailUrl = "/www/images/icon-set/user.png",
                Reference = "whoamiwidget"
            });
            li.Add(new InstructionSet() {
                Id = "*whereami",
                Action = "You are, %whereami%."
            });
            li.Add(new InstructionSet() {
                Id = "*whereamiwidget",
                Action = "%whereami%"
            });
            li.Add(new InstructionSet() {
                Id = "whereamiwidget",
                Action = "*whereamiwidget"
            });
            li.Add(new InstructionSet() {
                Id = "whereami",
                Action = "*whereami",
                ShortDescription = "Get user status",
                Header = "Where am I?",
                Category = "System",
                ThumbnailUrl = "/www/images/icon-set/home.png",
                Reference = "whereamiwidget"
            });
            li.Add(new InstructionSet() {
                Id = "*whoru",
                Action = "I am Jubito, nice to meet you."
            });
            li.Add(new InstructionSet() {
                Id = "whoru",
                Action = "*whoru",
                ShortDescription = "Get jubito's name :)",
                Header = "Who are you?",
                Category = "System",
                ThumbnailUrl = "/www/images/logo.png"
            });
            li.Add(new InstructionSet() {
                Id = "*weathertoday",
                Action = "Today %todayday%, %todayconditions%, between %todaylow% and %todayhigh% celcius degrees."
            });
            li.Add(new InstructionSet() {
                Id = "*forecastwidget",
                Action = "%todayconditions% %todaytemp%"
            });
            li.Add(new InstructionSet() {
                Id = "forecastwidget",
                Action = "*forecastwidget°C"
            });
            li.Add(new InstructionSet() {
                Id = "weathertoday",
                Action = "*weathertoday",
                Description = "Feed from weather service",
                ShortDescription = "Get today's weather",
                Header = "Today's weather",
                Category = "Weather",
                ThumbnailUrl = "/www/images/icon-set/weather.png",
                Reference = "forecastwidget"
            });
            li.Add(new InstructionSet() {
                Id = "*gmailone",
                Action = "You have, 1, unread message to your g-mail inbox."
            });
            li.Add(new InstructionSet() {
                Id = "gmailone",
                Action = "*gmailone"
            });
            li.Add(new InstructionSet() {
                Id = "*gmailcount",
                Action = "You have, %gmailcount%, unread messages to your g-mail inbox."
            });
            li.Add(new InstructionSet() {
                Id = "gmailcount",
                Action = "*gmailcount"
            });
            li.Add(new InstructionSet() {
                Id = "*pop3one",
                Action = "You have, 1, unread message to pop-3 inbox."
            });
            li.Add(new InstructionSet() {
                Id = "pop3one",
                Action = "*pop3one"
            });
            li.Add(new InstructionSet() {
                Id = "*pop3count",
                Action = "You have, %pop3count%, unread messages to pop-3 inbox."
            });
            li.Add(new InstructionSet() {
                Id = "pop3count",
                Action = "*pop3count"
            });
            li.Add(new InstructionSet() {
                Id = "*mail",
                Action = "{ evalBool(%gmailcount% == 1); gmailone; ; }{ evalBool(%gmailcount% > 1); gmailcount; ; }{ evalBool(%pop3count% == 1); pop3one; ; }{ evalBool(%pop3count% > 1); pop3count; ; }"
            });
            li.Add(new InstructionSet() {
                Id = "mail",
                Action = "*mail",
                Description = "Gmail/Pop3",
                ShortDescription = "Check for unread messages from all accounts",
                Header = "Check unread messages",
                Category = "Network",
                ThumbnailUrl = "/www/images/icon-set/email.png"
            });
            li.Add(new InstructionSet() {
                Id = "*allmail",
                Action = "You have, %pop3count%, mail messages, to pop-3 inbox, and, %gmailcount%, mail messages to your g-mail inbox."
            });
            li.Add(new InstructionSet() {
                Id = "allmail",
                Action = "*allmail",
                Description = "Gmail/Pop3",
                ShortDescription = "Count messages from all accounts",
                Header = "Check all",
                Category = "Network",
                ThumbnailUrl = "/www/images/icon-set/allmail.png"
            });
            li.Add(new InstructionSet() {
                Id = "*gmail",
                Action = "{ evalBool(%gmailcount% == 1); gmailone; gmailcount; }"
            });
            li.Add(new InstructionSet() {
                Id = "*gmailwidget",
                Action = "%gmailcount%"
            });
            li.Add(new InstructionSet() {
                Id = "gmailwidget",
                Action = "*gmailwidget"
            });
            li.Add(new InstructionSet() {
                Id = "gmail",
                Action = "*gmail",
                Description = "Gmail account",
                ShortDescription = "Check for unread messages",
                Header = "Check Gmail",
                Category = "Network",
                ThumbnailUrl = "/www/images/icon-set/gmail.png",
                Reference = "gmailwidget"
            });
            li.Add(new InstructionSet() {
                Id = "*gmailreader",
                Action = "%gmailreader%"
            });
            li.Add(new InstructionSet() {
                Id = "gmailreader",
                Action = "*gmailreader",
                Description = "Gmail account",
                ShortDescription = "Read headers from unread messages",
                Header = "Gmail headers",
                Category = "Network",
                ThumbnailUrl = "/www/images/icon-set/gmailreader.png"
            });
            li.Add(new InstructionSet() {
                Id = "*time",
                Action = "Time is, %time%."
            });
            li.Add(new InstructionSet() {
                Id = "*timewidget",
                Action = "%time%"
            });
            li.Add(new InstructionSet() {
                Id = "timewidget",
                Action = "*timewidget"
            });
            li.Add(new InstructionSet() {
                Id = "time",
                Action = "*time",
                Description = "AM/PM",
                ShortDescription = "Get system time in AM/PM",
                Header = "Time",
                Category = "Localization",
                ThumbnailUrl = "/www/images/icon-set/time.png",
                Reference = "timewidget"
            });
            li.Add(new InstructionSet() {
                Id = "*time24",
                Action = "Time is, %time24%."
            });
            li.Add(new InstructionSet() {
                Id = "*time24widget",
                Action = "%time24%"
            });
            li.Add(new InstructionSet() {
                Id = "time24widget",
                Action = "*time24widget"
            });
            li.Add(new InstructionSet() {
                Id = "time24",
                Action = "*time24",
                Description = "Get system time in 24 format",
                ShortDescription = "Time 24h",
                Category = "Localization",
                ThumbnailUrl = "/www/images/icon-set/time.png",
                Reference = "time24widget"
            });
            li.Add(new InstructionSet() {
                Id = "*date",
                Action = "Date is, %date%."
            });
            li.Add(new InstructionSet() {
                Id = "*datewidget",
                Action = "%date%"
            });
            li.Add(new InstructionSet() {
                Id = "datewidget",
                Action = "*datewidget"
            });
            li.Add(new InstructionSet() {
                Id = "date",
                Action = "*date",
                Description = "Get system date",
                ShortDescription = "Date",
                Category = "Localization",
                ThumbnailUrl = "/www/images/icon-set/date.png",
                Reference = "datewidget"
            });
            li.Add(new InstructionSet() {
                Id = "*day",
                Action = "Today is, %day%."
            });
            li.Add(new InstructionSet() {
                Id = "*daywidget",
                Action = "%day%"
            });
            li.Add(new InstructionSet() {
                Id = "daywidget",
                Action = "*daywidget"
            });
            li.Add(new InstructionSet() {
                Id = "day",
                Action = "*day",
                ShortDescription = "Get system day",
                Header = "Day",
                Category = "Localization",
                ThumbnailUrl = "/www/images/icon-set/day.png",
                Reference = "daywidget"
            });
            li.Add(new InstructionSet() {
                Id = "*partofday",
                Action = "Now is, %daypart%."
            });
            li.Add(new InstructionSet() {
                Id = "*partofdaywidget",
                Action = "%partofday%"
            });
            li.Add(new InstructionSet() {
                Id = "partofdaywidget",
                Action = "*partofdaywidget"
            });
            li.Add(new InstructionSet() {
                Id = "partofday",
                Action = "*partofday",
                Description = "e.g. morning, evening, afternoon, etc.",
                ShortDescription = "Acknowledge part of day",
                Header = "Part of day",
                Category = "Localization",
                ThumbnailUrl = "/www/images/icon-set/daypart.png",
                Reference = "partofdaywidget"
            });
            li.Add(new InstructionSet() {
                Id = "*check_me_in",
                Action = "%checkin%"
            });
            li.Add(new InstructionSet() {
                Id = "check_me_in",
                Action = "*check_me_in",
                Description = "Change user status to present",
                ShortDescription = "User check-in",
                Header = "Check-in",
                Category = "User",
                ThumbnailUrl = "/www/images/checkin.png"
            });
            li.Add(new InstructionSet() {
                Id = "*check_me_out",
                Action = "%checkout%"
            });
            li.Add(new InstructionSet() {
                Id = "check_me_out",
                Action = "*check_me_out",
                Description = "Change user status to absent",
                ShortDescription = "User check-out",
                Header = "Check-out",
                Category = "User",
                ThumbnailUrl = "/www/images/checkout.png"
            });
            li.Add(new InstructionSet() {
                Id = "*checkinout",
                Action = @"{ evalBool(""%whereami%"" == ""absent""); check_me_in; check_me_out; }"
            });
            li.Add(new InstructionSet() {
                Id = "checkinout",
                Action = "*checkinout"
            });
            li.Add(new InstructionSet() {
                Id = "*userstatuswidget",
                Action = "%whereami%"
            });
            li.Add(new InstructionSet() {
                Id = "userstatuswidget",
                Action = "*userstatuswidget"
            });
            li.Add(new InstructionSet() {
                Id = "*eval_check_in_out",
                Action = "Evaluating your status. *checkinout *sleeper"
            });
            li.Add(new InstructionSet() {
                Id = "eval_check_in_out",
                Action = "*eval_check_in_out Your status has been changed.",
                Description = "Change user status by evaluating current status",
                ShortDescription = "User check-in/check-out",
                Header = "Check-in/Check-out",
                Category = "User",
                ThumbnailUrl = "/www/images/icon-set/location.png",
                Reference = "userstatuswidget"
            });
            li.Add(new InstructionSet() {
                Id = "*sleeper",
                Action = "judo sleep 2000"
            });
            li.Add(new InstructionSet() {
                Id = "sleeper",
                Action = "*sleeper"
            });
            root.Instructions.InstructionSet = li;

            root.SerializeObject(Methods.Instance.GetApplicationPath + "AppConfig.xml");
        }

        public static void Dispose() {
            SerialComm.DeactivateSerialPort();
            WebServer.Stop();
            TCP.Stop();
            Parser.ParserState = false;
            System.Environment.Exit(0);
        }
    }
}