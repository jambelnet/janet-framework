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

using jaNET;
using jaNET.Environment;
using jaNET.Environment.AppConfig;
using jaNET.Extensions;
using jaNET.IO;
using jaNET.IO.Ports;
using jaNET.Net;
using jaNET.Net.Http;
using jaNET.Net.Sockets;
using jaNET.Providers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Xml;

namespace jaNET.Environment.Core
{
    static class JudoParser
    {
        internal static string Parse(string arg) {
            var method = Methods.Instance;
            var com = new Comm();
            var settings = new Settings();

            var args = arg.Contains("</lock>") ? arg.SplitArguments() : arg.ToValues().SplitArguments();

            string output = string.Empty;

            switch (args[1]) {
                #region Timer
                case "timer":
                case "sleep":
                    Thread.Sleep(Convert.ToInt32(args[2]));
                    break;
                #endregion
                #region Serial
                case "serial":
                    switch (args[2]) {
                        case "open":
                            if (args.Count() > 3)
                                SerialComm.ActivateSerialPort(args[3]);
                            else
                                SerialComm.ActivateSerialPort(string.Empty);
                            Thread.Sleep(50);
                            output = string.Format("Serial port state: {0}", SerialComm.port.IsOpen);
                            break;
                        case "close":
                            SerialComm.DeactivateSerialPort();
                            Thread.Sleep(50);
                            output = string.Format("Serial port state: {0}", SerialComm.port.IsOpen);
                            break;
                        case "send":
                        case "listen":
                        case "monitor":
                            if ((args[2] == "listen" || args[2] == "monitor") && args.Count() > 3)
                                output = SerialComm.WriteToSerialPort(string.Empty, args[2].ToTypeOfSerialMessage(), Convert.ToInt32(args[3]));
                            else if (args.Count() > 4)
                                output = SerialComm.WriteToSerialPort(args[3], args[2].ToTypeOfSerialMessage(), Convert.ToInt32(args[4]));
                            else
                                output = SerialComm.WriteToSerialPort(args[3], args[2].ToTypeOfSerialMessage());
                            break;
                        case "set":
                        case "setup":
                            if (args.Count() >= 4)
                                com.ComPort = args[3];
                            if (args.Count() == 5)
                                com.BaudRate = args[4];
                            output = method.AddToXML(com, AppStructure.SystemCommRoot);
                            break;
                        case "settings":
                            output = string.Format("{0}\r\n{1}", Comm.GetComPort, Comm.GetBaudRate);
                            break;
                        case "state":
                        case "status":
                        default:
                            output = string.Format("Serial port state: {0}", SerialComm.port.IsOpen);
                            break;
                    }
                    break;
                #endregion
                #region Instruction Sets
                case "inset":
                    switch (args[2]) {
                        case "add":
                        case "new":
                        case "set":
                        case "setup":
                            var li = new List<InstructionSet>();
                            if (args.Count() == 5) {
                                li.Add(new InstructionSet { Id = "*" + args[3], Action = args[4] });
                                li.Add(new InstructionSet { Id = args[3], Action = "*" + args[3] });
                            }
                            else {
                                li.Add(new InstructionSet { Id = "*" + args[3], Action = args[4] });
                                li.Add(new InstructionSet {
                                    Id = args[3],
                                    Action = "*" + args[3],
                                    Category = args[5],
                                    Header = args[6],
                                    ShortDescription = args[7],
                                    Description = args[8],
                                    ThumbnailUrl = args[9],
                                    Reference = args[10]
                                });
                            }
                            li.ForEach(item => output = method.AddToXML(item, AppStructure.SystemInstructionsRoot));
                            break;
                        case "remove":
                        case "rm":
                        case "delete":
                        case "del":
                        case "kill":
                            output = method.RemoveFromXML(args[3], AppStructure.SystemInstructionsRoot, "InstructionSet");
                            break;
                        case "list":
                        case "ls":
                        default:
                            XmlNodeList xList = method.GetXmlElementList(AppStructure.SystemInstructionsRoot, "InstructionSet");
                            foreach (XmlNode nodeItem in xList)
                                output += string.Format("{0}\r\n", nodeItem.OuterXml);
                            break;
                    }
                    break;
                #endregion
                #region Events
                case "event":
                    switch (args[2]) {
                        case "add":
                        case "new":
                        case "set":
                        case "setup":
                            method.AddToXML(new Event { Id = args[3], Action = args[4] }, AppStructure.SystemEventsRoot);
                            output = method.AddToXML(new InstructionSet { Id = args[3], Action = "%~>" + args[3] + "%" }, AppStructure.SystemInstructionsRoot);
                            break;
                        case "remove":
                        case "rm":
                        case "delete":
                        case "del":
                        case "kill":
                            output = method.RemoveFromXML(args[3], AppStructure.SystemEventsRoot, "event");
                            break;
                        case "list":
                        case "ls":
                        default:
                            XmlNodeList xList = method.GetXmlElementList(AppStructure.SystemEventsRoot, "event");
                            foreach (XmlNode nodeItem in xList)
                                output += string.Format("{0}\r\n", nodeItem.OuterXml);
                            break;
                    }
                    break;
                #endregion
                #region Trusted
                case "trusted":
                    switch (args[2]) {
                        case "settings":
                            output = string.Format("{0}", Comm.GetTrusted);
                            break;
                    }
                    break;
                #endregion
                #region TCP Socket
                case "socket":
                    switch (args[2]) {
                        case "trust":
                            output = method.AddToXML(new Comm { Trusted = args[3]  }, AppStructure.SystemCommRoot);
                            break;
                        case "on":
                        case "enable":
                        case "start":
                        case "listen":
                        case "open":
                            TcpServer.Start();
                            Thread.Sleep(50);
                            output = string.Format("Socket state: {0}", TcpServer.ServerState);
                            break;
                        case "off":
                        case "disable":
                        case "stop":
                        case "close":
                            TcpServer.Stop();
                            Thread.Sleep(50);
                            output = string.Format("Socket state: {0}", TcpServer.ServerState);
                            break;
                        case "set":
                        case "setup":
                            if (args.Count() >= 4)
                                com.LocalHost = args[3];
                            if (args.Count() == 5)
                                com.LocalPort = args[4];
                            output = method.AddToXML(com, AppStructure.SystemCommRoot);
                            break;
                        case "settings":
                            output = string.Format("{0}\r\n{1}\r\n{2}", Comm.GetLocalHost, Comm.GetLocalPort, Comm.GetTrusted);
                            break;
                        case "state":
                        case "status":
                        default:
                            output = string.Format("Socket state: {0}", TcpServer.ServerState);
                            break;
                    }
                    break;
                #endregion
                #region Webserver
                case "server":
                    switch (args[2]) {
                        case "on":
                        case "enable":
                        case "start":
                        case "listen":
                            WebServer.Start();
                            Thread.Sleep(50);
                            output = string.Format("Web server state: {0}", WebServer.httplistener.IsListening);
                            break;
                        case "off":
                        case "disable":
                        case "stop":
                            WebServer.Stop();
                            Thread.Sleep(50);
                            output = string.Format("Web server state: {0}", WebServer.httplistener.IsListening);
                            break;
                        case "login":
                        case "cred":
                        case "credentials":
                            output = settings.Save(".htaccess", string.Format("{0}\r\n{1}", args[3], args[4]));
                            break;
                        case "set":
                        case "setup":
                            if (args.Count() >= 4)
                                com.Hostname = args[3];
                            if (args.Count() >= 5)
                                com.HttpPort = args[4];
                            if (args.Count() == 6)
                                com.Authentication = args[5];
                            output = method.AddToXML(com, AppStructure.SystemCommRoot);
                            break;
                        case "settings":
                            output = string.Format("{0}\r\n{1}\r\n{2}", Comm.GetHostname, Comm.GetHttpPort, Comm.GetAuthentication);
                            break;
                        case "state":
                        case "status":
                        default:
                            output = string.Format("Web server state: {0}", WebServer.httplistener.IsListening);
                            break;
                    }
                    break;
                #endregion
                #region Scheduler
                case "schedule":
                    switch (args[2]) {
                        case "add":
                        case "new":
                        case "set":
                        case "setup":
                            var s = arg.Replace("judo schedule add ", string.Empty).ToSchedule();

                            if (s.Date == Schedule.Period.Repeat || s.Date == Schedule.Period.Interval || s.Date == Schedule.Period.Timer)
                                output = Schedule.Add(s, Convert.ToInt32(s.Time));
                            else
                                output = Schedule.Add(s);
                            break;
                        case "enable":
                        case "activate":
                        case "start":
                        case "on":
                            output = Schedule.ChangeStatus(args[3], Schedule.State.Enable);
                            break;
                        case "enable-all":
                        case "activate-all":
                        case "start-all":
                        case "on-all":
                            output = Schedule.ChangeStatus(Schedule.State.EnableAll);
                            break;
                        case "disable":
                        case "deactivate":
                        case "stop":
                        case "off":
                            output = Schedule.ChangeStatus(args[3], Schedule.State.Disable);
                            break;
                        case "disable-all":
                        case "deactivate-all":
                        case "stop-all":
                        case "off-all":
                            output = Schedule.ChangeStatus(Schedule.State.DisableAll);
                            break;
                        case "remove":
                        case "rm":
                        case "delete":
                        case "del":
                            output = Schedule.ChangeStatus(args[3], Schedule.State.Remove);
                            break;
                        case "remove-all":
                        case "delete-all":
                        case "del-all":
                        case "cleanup":
                        case "clear":
                        case "empty":
                            output = Schedule.ChangeStatus(Schedule.State.RemoveAll);
                            break;
                        case "active":
                        case "actives":
                        case "active-list":
                        case "active-ls":
                        case "list-actives":
                        case "ls-actives":
                            foreach (Schedule schedule in Schedule.ScheduleList)
                                if (schedule.Status)
                                    output += string.Format("{0}\r\n", schedule.Name);
                            break;
                        case "inactive":
                        case "inactives":
                        case "inactive-list":
                        case "inactive-ls":
                        case "list-inactives":
                        case "ls-inactives":
                            foreach (Schedule schedule in Schedule.ScheduleList)
                                if (!schedule.Status)
                                    output += string.Format("{0}\r\n", schedule.Name);
                            break;
                        case "names":
                        case "name-list":
                        case "name-ls":
                        case "list-names":
                        case "ls-names":
                            foreach (Schedule schedule in Schedule.ScheduleList)
                                output += string.Format("{0}\r\n", schedule.Name);
                            break;
                        case "active-details":
                        case "actives-details":
                        case "active-list-details":
                        case "active-ls-details":
                        case "list-actives-details":
                        case "ls-actives-details":
                            foreach (Schedule schedule in Schedule.ScheduleList)
                                if (schedule.Status)
                                    output += string.Format("{0} | {1} | {2} | {3} | {4}\r\n",
                                                             schedule.Name,
                                                             schedule.Date,
                                                             schedule.Time,
                                                             schedule.Action,
                                                             schedule.Status.ToString().ToLower().Replace("true", "Active"));
                            break;
                        case "inactive-details":
                        case "inactives-details":
                        case "inactive-list-details":
                        case "inactive-ls-details":
                        case "list-inactives-details":
                        case "ls-inactives-details":
                            foreach (Schedule schedule in Schedule.ScheduleList)
                                if (!schedule.Status)
                                    output += string.Format("{0} | {1} | {2} | {3} | {4}\r\n",
                                                             schedule.Name,
                                                             schedule.Date,
                                                             schedule.Time,
                                                             schedule.Action,
                                                             schedule.Status.ToString().ToLower().Replace("false", "Inactive"));
                            break;
                        case "details":
                        case "state":
                        case "status":
                        case "list":
                        case "ls":
                        default:
                            foreach (Schedule schedule in Schedule.ScheduleList)
                                if (args.Count() > 3) {
                                    if (args[3] == schedule.Name)
                                        output += string.Format("{0} | {1} | {2} | {3} | {4}\r\n",
                                                                 schedule.Name,
                                                                 schedule.Date,
                                                                 schedule.Time,
                                                                 schedule.Action,
                                                                 schedule.Status.ToString().ToLower().Replace("true", "Active")
                                                                                                     .Replace("false", "Inactive"));
                                }
                                else
                                    output += string.Format("{0} | {1} | {2} | {3} | {4}\r\n",
                                                             schedule.Name,
                                                             schedule.Date,
                                                             schedule.Time,
                                                             schedule.Action,
                                                             schedule.Status.ToString().ToLower().Replace("true", "Active")
                                                                                                 .Replace("false", "Inactive"));
                            break;
                    }
                    break;
                #endregion
                #region Smtp
                case "smtp":
                    switch (args[2]) {
                        case "add":
                        case "new":
                        case "setup":
                        case "set":
                            output = settings.Save(".smtpsettings",
                                                    string.Format("{0}\r\n{1}\r\n{2}\r\n{3}\r\n{4}",
                                                    args[3], args[4], args[5], args[6], args[7]));
                            break;
                        case "settings":
                            var smtpSettings = new Mail.SmtpSettings();
                            output = string.Format("{0}\r\n{1}\r\n{2}\r\n{3}\r\n{4}",
                                                    smtpSettings.Host, smtpSettings.Username, smtpSettings.Password, smtpSettings.Port, smtpSettings.SSL);
                            break;
                    }
                    break;
                #endregion
                #region Pop3
                case "pop3":
                    switch (args[2]) {
                        case "add":
                        case "new":
                        case "setup":
                        case "set":
                            output = settings.Save(".pop3settings",
                                                    string.Format("{0}\r\n{1}\r\n{2}\r\n{3}\r\n{4}",
                                                    args[3], args[4], args[5], args[6], args[7]));
                            break;
                        case "settings":
                            var pop3Settings = new Mail.Pop3Settings();
                            output = string.Format("{0}\r\n{1}\r\n{2}\r\n{3}\r\n{4}",
                                                    pop3Settings.Host, pop3Settings.Username, pop3Settings.Password, pop3Settings.Port, pop3Settings.SSL);
                            break;
                    }
                    break;
                #endregion
                #region Gmail
                case "gmail":
                    switch (args[2]) {
                        case "add":
                        case "new":
                        case "setup":
                        case "set":
                            output = settings.Save(".gmailsettings", string.Format("{0}\r\n{1}", args[3], args[4]));
                            break;
                        case "settings":
                            var gmailSettings = new Mail.GmailSettings();
                            output = string.Format("{0}\r\n{1}", gmailSettings.Username, gmailSettings.Password);
                            break;
                    }
                    break;
                #endregion
                #region Mail
                case "mail":
                    switch (args[2]) {
                        case "send":
                            output = new Mail().Send(args[3], args[4], args[5], args[6]).ToString()
                                               .Replace("True", "Mail sent!").Replace("False", "Mail could not be sent");
                            break;
                    }
                    break;
                #endregion
                #region Mail Headers
                case "mailheaders":
                case "mailheader":
                    switch (args[2]) {
                        case "add":
                        case "new":
                        case "set":
                        case "setup":
                            output = method.AddToXML(new MailHeaders { MailFrom = args[3], MailTo = args[4], MailSubject = args[5] }, AppStructure.SystemAlertsMailHeadersRoot);
                            break;
                        case "settings":
                            output = string.Format("{0}\r\n{1}\r\n{2}", MailHeaders.GetMailHeaderFrom, MailHeaders.GetMailHeaderTo, MailHeaders.GetMailHeaderSubject);
                            break;
                    }
                    break;
                #endregion
                #region Sms
                case "sms":
                    switch (args[2]) {
                        case "add":
                        case "new":
                        case "setup":
                        case "set":
                            output = settings.Save(".smssettings", string.Format("{0}\r\n{1}\r\n{2}", args[3], args[4], args[5]));
                            break;
                        case "settings":
                            var smsSettings = new SMS.SmsSettings();
                            output = string.Format("{0}\r\n{1}\r\n{2}", smsSettings.SmsAPI, smsSettings.SmsUsername, smsSettings.SmsPassword);
                            break;
                        case "send":
                            output = new SMS().Send(args[3], args[4]);
                            break;
                    }
                    break;
                #endregion
                #region Web Api
                case "json":
                    switch (args[2]) {
                        case "add":
                        case "new":
                        case "set":
                        case "setup":
                            var li = new List<InstructionSet>();
                            li.Add(new InstructionSet {
                                Id = "*" + args[3],
                                Action = "judo json get " + WebServer.SimpleUriEncode(args[4]) + " " + args[5]
                            });
                            li.Add(new InstructionSet {
                                Id = args[3],
                                Action = "*" + args[3]
                            });
                            li.ForEach(item => output = method.AddToXML(item, AppStructure.SystemInstructionsRoot));
                            break;
                        case "get":
                        case "response":
                        case "consume":
                        case "extract":
                            output = new Helpers.Json().SelectSingleNode(WebServer.SimpleUriDecode(args[3]), args[4]);
                            break;
                    }
                    break;
                #endregion
                #region Web Service
                case "xml":
                    switch (args[2]) {
                        case "add":
                        case "new":
                        case "set":
                        case "setup":
                            var li = new List<InstructionSet>();
                            switch (args.Count()) {
                                case 6:
                                    li.Add(new InstructionSet {
                                        Id = "*" + args[3],
                                        Action = "judo xml get " + WebServer.SimpleUriEncode(args[4]) + " " + args[5]
                                    });
                                    li.Add(new InstructionSet { Id = args[3], Action = "*" + args[3] });
                                    break;
                                case 7:
                                    li.Add(new InstructionSet {
                                        Id = "*" + args[3],
                                        Action = "judo xml get " + WebServer.SimpleUriEncode(args[4]) + " " + args[5] + " " + args[6]
                                    });
                                    li.Add(new InstructionSet { Id = args[3], Action = "*" + args[3] });
                                    break;
                                case 8:
                                    li.Add(new InstructionSet {
                                        Id = "*" + args[3],
                                        Action = "judo xml get " + WebServer.SimpleUriEncode(args[4]) + " " + args[5] + " " + args[6] + " " + args[7]
                                    });
                                    li.Add(new InstructionSet { Id = args[3], Action = "*" + args[3] });
                                    break;
                            }
                            li.ForEach(item => output = method.AddToXML(item, AppStructure.SystemInstructionsRoot));
                            break;
                        case "get":
                        case "response":
                        case "consume":
                        case "extract":
                            switch (args.Count()) {
                                case 5:
                                    output = Helpers.Xml.SelectSingleNode(WebServer.SimpleUriDecode(args[3]), args[4]);
                                    break;
                                case 6:
                                    output = args[4].Contains("=") ?
                                        Helpers.Xml.SelectNodes(WebServer.SimpleUriDecode(args[3]), args[4], args[5])[0] :
                                        Helpers.Xml.SelectSingleNode(WebServer.SimpleUriDecode(args[3]), args[4], Convert.ToInt32(args[5]));
                                    break;
                                case 7:
                                    output = Helpers.Xml.SelectNodes(WebServer.SimpleUriDecode(args[3]), args[4], args[5])[Convert.ToInt32(args[6])];
                                    break;
                            }
                            break;
                    }
                    break;
                #endregion
                #region Http
                case "http":
                    switch (args[2]) {
                        case "get":
                            output = Helpers.Http.Get(WebServer.SimpleUriDecode(args[3]));
                            break;
                    }
                    break;
                #endregion
                #region Weather
                case "weather":
                    switch (args[2]) {
                        case "add":
                        case "new":
                        case "set":
                        case "setup":
                            output = method.AddToXML(new Others { Weather = args[3] }, AppStructure.SystemOthersRoot);
                            break;
                        case "settings":
                            output = Others.GetWeather;
                            break;
                    }
                    break;
                #endregion
                #region Pinger
                case "ping":
                    output = args.Count() == 3 ?
                        NetInfo.SimplePing.Ping(args[2]).ToString() :
                        NetInfo.SimplePing.Ping(args[2], Convert.ToInt32(args[3])).ToString();
                    break;
                #endregion
                #region Help
                case "help":
                case "?":
                    output = args.Count() > 2 ? output = method.GetHelp(args[2]) : output = method.GetHelp("all");
                    break;
                    #endregion
            }
            return output;
        }
    }
}