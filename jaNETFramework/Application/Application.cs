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
            internal static string GetAll => string.Format(
                "Days[{0}], Hours[{1}], Minutes[{2}], Seconds[{3}]",
                GetDays, GetHours, GetMinutes, GetSeconds);

            internal static int GetDays => (DateTime.Now - _Uptime).Days;

            internal static int GetHours => (DateTime.Now - _Uptime).Hours;

            internal static int GetMinutes => (DateTime.Now - _Uptime).Minutes;

            internal static int GetSeconds => (DateTime.Now - _Uptime).Seconds;
        }

        public static async void Initialize() {
            _Uptime = DateTime.Now;

            string AppPath = Methods.Instance.GetApplicationPath;

            if (!File.Exists(AppPath + "AppConfig.xml"))
                await GenerateDumpAppConfigAsync();

            if (!File.Exists(AppPath + ".htaccess"))
                new Settings().Save(".htaccess", "admin\r\nadmin");

            Schedule.Init();

            if (!string.IsNullOrWhiteSpace(Comm.GetHostname))
                WebServer.Start();
            if (!string.IsNullOrWhiteSpace(Comm.GetLocalHost))
                TcpServer.Start();
            if (!string.IsNullOrWhiteSpace(Comm.GetComPort))
                SerialComm.ActivateSerialPort(string.Empty); // throws exception in linux?

            "%checkin%".ToValues();
        }

        static async Task GenerateDumpAppConfigAsync() {
            await Task.Run(() => {
                // Root
                var root = new AppConfig.jaNET {
                    // Nodes
                    Instructions = new Instructions(),
                    Events = new Events(),
                    System = new AppConfig.System()
                };
                // 
                root.System.Alerts = new Alerts {
                    MailHeaders = new MailHeaders {
                        MailFrom = "noreply@xxx.org",
                        MailTo = "me@yyy.org",
                        MailSubject = "Alert from Jubito"
                    }
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
                root.System.Others = new Others {
                    Weather = "http://api.openweathermap.org/data/2.5/weather?q=Athens,GR&units=metric&APPID=44e99238e8b13d4085b1cc545ab9a70c"
                };

                // Events
                var le = new List<Event> {
                    new Event {
                        Id = "oncheckin",
                        Action = "%unmute%; salute; weathertoday"
                    },
                    new Event {
                        Id = "oncheckout",
                        Action = "judo sleep 5000; goodbye; %unmute%"
                    }
                };
                root.Events.Event = le;

                // Instruction Sets
                var li = new List<InstructionSet> {
                    new InstructionSet {
                        Id = "*salute",
                        Action = "Good %salute% %user%."
                    },
                    new InstructionSet {
                        Id = "salute",
                        Action = "*salute"
                    },
                    new InstructionSet {
                        Id = "*goodbye",
                        Action = "Good bye %user%."
                    },
                    new InstructionSet {
                        Id = "goodbye",
                        Action = "*goodbye"
                    },
                    new InstructionSet {
                        Id = "*whoami",
                        Action = "You are, %user%."
                    },
                    new InstructionSet {
                        Id = "*whoamiwidget",
                        Action = "%user%"
                    },
                    new InstructionSet {
                        Id = "whoamiwidget",
                        Action = "*whoamiwidget"
                    },
                    new InstructionSet {
                        Id = "whoami",
                        Action = "*whoami",
                        Header = "Login name",
                        ShortDescription = "Get user login",
                        Description = "System login",
                        Category = "System",
                        ThumbnailUrl = "/www/images/icon-set/user.png",
                        Reference = "whoamiwidget"
                    },
                    new InstructionSet {
                        Id = "*whereami",
                        Action = "You are, %whereami%."
                    },
                    new InstructionSet {
                        Id = "*whereamiwidget",
                        Action = "%whereami%"
                    },
                    new InstructionSet {
                        Id = "whereamiwidget",
                        Action = "*whereamiwidget"
                    },
                    new InstructionSet {
                        Id = "whereami",
                        Action = "*whereami",
                        Header = "Where am I?",
                        ShortDescription = "Get user status",
                        Description = "Present or absent",
                        Category = "System",
                        ThumbnailUrl = "/www/images/icon-set/home.png",
                        Reference = "whereamiwidget"
                    },
                    new InstructionSet {
                        Id = "*mute",
                        Action = "Disabling speech synthesis. %mute%"
                    },
                    new InstructionSet {
                        Id = "mute",
                        Action = "*mute",
                        Header = "Mute",
                        ShortDescription = "Disable speech synthesizer",
                        Description = "Turn off Text-To-Speech",
                        Category = "System",
                        ThumbnailUrl = "/www/images/icon-set/mute.png"
                    },
                    new InstructionSet {
                        Id = "*unmute",
                        Action = "%unmute% Speech synthesis enabled."
                    },
                    new InstructionSet {
                        Id = "unmute",
                        Action = "*unmute",
                        Header = "Unmute",
                        ShortDescription = "Enable speech synthesizer",
                        Description = "Turn on Text-To-Speech",
                        Category = "System",
                        ThumbnailUrl = "/www/images/icon-set/unmute.png"
                    },
                    new InstructionSet {
                        Id = "*whoru",
                        Action = "I am Jubito, nice to meet you."
                    },
                    new InstructionSet {
                        Id = "whoru",
                        Action = "*whoru",
                        Header = "Who are you?",
                        ShortDescription = "Get jubito's name",
                        Description = "A polite system introduction :)",
                        Category = "System",
                        ThumbnailUrl = "/www/images/logo.png"
                    },
                    new InstructionSet {
                        Id = "*weathertoday",
                        Action = "Today %todayday%, %todayconditions%, between %todaylow% and %todayhigh% celcius degrees."
                    },
                    new InstructionSet {
                        Id = "*forecastwidget",
                        Action = "%todayconditions% %todaytemp%"
                    },
                    new InstructionSet {
                        Id = "forecastwidget",
                        Action = "*forecastwidget°C"
                    },
                    new InstructionSet {
                        Id = "weathertoday",
                        Action = "*weathertoday",
                        Header = "Today's weather",
                        ShortDescription = "Get today's weather",
                        Description = "Feed from weather service",
                        Category = "Weather",
                        ThumbnailUrl = "/www/images/icon-set/weather.png",
                        Reference = "forecastwidget"
                    },
                    new InstructionSet {
                        Id = "*gmailone",
                        Action = "You have, 1, unread message to your g-mail inbox."
                    },
                    new InstructionSet {
                        Id = "gmailone",
                        Action = "*gmailone"
                    },
                    new InstructionSet {
                        Id = "*gmailcount",
                        Action = "You have, %gmailcount%, unread messages to your g-mail inbox."
                    },
                    new InstructionSet {
                        Id = "gmailcount",
                        Action = "*gmailcount"
                    },
                    new InstructionSet {
                        Id = "*pop3one",
                        Action = "You have, 1, unread message to pop-3 inbox."
                    },
                    new InstructionSet {
                        Id = "pop3one",
                        Action = "*pop3one"
                    },
                    new InstructionSet {
                        Id = "*pop3count",
                        Action = "You have, %pop3count%, unread messages to pop-3 inbox."
                    },
                    new InstructionSet {
                        Id = "pop3count",
                        Action = "*pop3count"
                    },
                    new InstructionSet {
                        Id = "*mail",
                        Action = "{ evalBool(%gmailcount% == 1); gmailone; ; }{ evalBool(%gmailcount% > 1); gmailcount; ; }{ evalBool(%pop3count% == 1); pop3one; ; }{ evalBool(%pop3count% > 1); pop3count; ; }"
                    },
                    new InstructionSet {
                        Id = "mail",
                        Action = "*mail",
                        Header = "Check unread messages",
                        ShortDescription = "Gmail/Pop3",
                        Description = "Check for unread messages from all accounts",
                        Category = "Network",
                        ThumbnailUrl = "/www/images/icon-set/email.png"
                    },
                    new InstructionSet {
                        Id = "*allmail",
                        Action = "You have, %pop3count%, mail messages, to pop-3 inbox, and, %gmailcount%, mail messages to your g-mail inbox."
                    },
                    new InstructionSet {
                        Id = "allmail",
                        Action = "*allmail",
                        Header = "Check all",
                        ShortDescription = "Gmail/Pop3",
                        Description = "Count messages from all accounts",
                        Category = "Network",
                        ThumbnailUrl = "/www/images/icon-set/allmail.png"
                    },
                    new InstructionSet {
                        Id = "*gmail",
                        Action = "{ evalBool(%gmailcount% == 1); gmailone; gmailcount; }"
                    },
                    new InstructionSet {
                        Id = "*gmailwidget",
                        Action = "%gmailcount%"
                    },
                    new InstructionSet {
                        Id = "gmailwidget",
                        Action = "*gmailwidget"
                    },
                    new InstructionSet {
                        Id = "gmail",
                        Action = "*gmail",
                        Header = "Check Gmail",
                        ShortDescription = "Gmail account",
                        Description = "Check for unread messages",
                        Category = "Network",
                        ThumbnailUrl = "/www/images/icon-set/gmail.png",
                        Reference = "gmailwidget"
                    },
                    new InstructionSet {
                        Id = "*gmailreader",
                        Action = "%gmailreader%"
                    },
                    new InstructionSet {
                        Id = "gmailreader",
                        Action = "*gmailreader",
                        Header = "Gmail headers",
                        ShortDescription = "Gmail account",
                        Description = "Read headers from unread messages",
                        Category = "Network",
                        ThumbnailUrl = "/www/images/icon-set/gmailreader.png"
                    },
                    new InstructionSet {
                        Id = "*time",
                        Action = "Time is, %time%."
                    },
                    new InstructionSet {
                        Id = "*timewidget",
                        Action = "%time%"
                    },
                    new InstructionSet {
                        Id = "timewidget",
                        Action = "*timewidget"
                    },
                    new InstructionSet {
                        Id = "time",
                        Action = "*time",
                        Header = "Time",
                        ShortDescription = "AM/PM",
                        Description = "Get system time in AM/PM format",
                        Category = "Localization",
                        ThumbnailUrl = "/www/images/icon-set/time.png",
                        Reference = "timewidget"
                    },
                    new InstructionSet {
                        Id = "*time24",
                        Action = "Time is, %time24%."
                    },
                    new InstructionSet {
                        Id = "*time24widget",
                        Action = "%time24%"
                    },
                    new InstructionSet {
                        Id = "time24widget",
                        Action = "*time24widget"
                    },
                    new InstructionSet {
                        Id = "time24",
                        Action = "*time24",
                        Header = "Time 24h",
                        ShortDescription = "Time in 24h format",
                        Description = "Get system time in 24 hours format",
                        Category = "Localization",
                        ThumbnailUrl = "/www/images/icon-set/time24.png",
                        Reference = "time24widget"
                    },
                    new InstructionSet {
                        Id = "*date",
                        Action = "Date is, %date%."
                    },
                    new InstructionSet {
                        Id = "*datewidget",
                        Action = "%date%"
                    },
                    new InstructionSet {
                        Id = "datewidget",
                        Action = "*datewidget"
                    },
                    new InstructionSet {
                        Id = "date",
                        Action = "*date",
                        Header = "Date",
                        ShortDescription = "Get system date",
                        Description = "e.g. May 9",
                        Category = "Localization",
                        ThumbnailUrl = "/www/images/icon-set/calendar.png",
                        Reference = "datewidget"
                    },
                    new InstructionSet {
                        Id = "*day",
                        Action = "Today is, %day%."
                    },
                    new InstructionSet {
                        Id = "*daywidget",
                        Action = "%day%"
                    },
                    new InstructionSet {
                        Id = "daywidget",
                        Action = "*daywidget"
                    },
                    new InstructionSet {
                        Id = "day",
                        Action = "*day",
                        Header = "Day",
                        ShortDescription = "Get system day",
                        Description = "e.g. Friday",
                        Category = "Localization",
                        ThumbnailUrl = "/www/images/icon-set/day.png",
                        Reference = "daywidget"
                    },
                    new InstructionSet {
                        Id = "*partofday",
                        Action = "Now is, %daypart%."
                    },
                    new InstructionSet {
                        Id = "*partofdaywidget",
                        Action = "%partofday%"
                    },
                    new InstructionSet {
                        Id = "partofdaywidget",
                        Action = "*partofdaywidget"
                    },
                    new InstructionSet {
                        Id = "partofday",
                        Action = "*partofday",
                        Header = "Part of day",
                        ShortDescription = "Acknowledge part of day",
                        Description = "e.g. morning, evening, afternoon, etc.",
                        Category = "Localization",
                        ThumbnailUrl = "/www/images/icon-set/daypart.png",
                        Reference = "partofdaywidget"
                    },
                    new InstructionSet {
                        Id = "*check_me_in",
                        Action = "%checkin%"
                    },
                    new InstructionSet {
                        Id = "check_me_in",
                        Action = "*check_me_in",
                        Header = "Check-in",
                        ShortDescription = "User check-in",
                        Description = "Change user status to present",
                        Category = "User",
                        ThumbnailUrl = "/www/images/icon-set/checkin.png"
                    },
                    new InstructionSet {
                        Id = "*check_me_out",
                        Action = "%checkout%"
                    },
                    new InstructionSet {
                        Id = "check_me_out",
                        Action = "*check_me_out",
                        Header = "Check-out",
                        ShortDescription = "User check-out",
                        Description = "Change user status to absent",
                        Category = "User",
                        ThumbnailUrl = "/www/images/icon-set/checkout.png"
                    },
                    new InstructionSet {
                        Id = "*checkinout",
                        Action = @"{ evalBool(""%whereami%"" == ""absent""); check_me_in; check_me_out; }"
                    },
                    new InstructionSet {
                        Id = "checkinout",
                        Action = "*checkinout"
                    },
                    new InstructionSet {
                        Id = "*userstatuswidget",
                        Action = "%whereami%"
                    },
                    new InstructionSet {
                        Id = "userstatuswidget",
                        Action = "*userstatuswidget"
                    },
                    new InstructionSet {
                        Id = "*eval_check_in_out",
                        Action = "Evaluating your status. *checkinout *sleeper"
                    },
                    new InstructionSet {
                        Id = "eval_check_in_out",
                        Action = "*eval_check_in_out Your status has been changed.",
                        Header = "Check-in/Check-out",
                        ShortDescription = "User check-in/check-out",
                        Description = "Change user status by evaluating current status",
                        Category = "User",
                        ThumbnailUrl = "/www/images/icon-set/location.png",
                        Reference = "userstatuswidget"
                    },
                    new InstructionSet {
                        Id = "*sleeper",
                        Action = "judo sleep 2000"
                    },
                    new InstructionSet {
                        Id = "sleeper",
                        Action = "*sleeper"
                    }
                };
                root.Instructions.InstructionSet = li;

                root.SerializeObject(Methods.Instance.GetApplicationPath + "AppConfig.xml");
            });
        }

        public static void Dispose() {
            SerialComm.DeactivateSerialPort();
            WebServer.Stop();
            TcpServer.Stop();
            Parser.ParserState = false;
            System.Environment.Exit(0);
        }
    }
}