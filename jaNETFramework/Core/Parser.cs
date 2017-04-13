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

using jaNETFramework.AppConfig;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using static jaNETFramework.Server.Web.Request;

namespace jaNETFramework
{
    public class Parser
    {
        internal static Parser Instance { get { return Singleton<Parser>.Instance; } }

        static volatile bool _parserState = true;

        public static bool ParserState {
            get { return _parserState; }
            internal set { _parserState = value; }
        }

        internal static volatile bool Mute;
        static readonly object _speech_locker = new object();

        public string Parse(string args) {
            return Parse(args, DataType.text, false);
        }

        internal string Parse(string args, DataType dataType, bool disableSpeech) {
            if (args.Contains("{mute}") || args.Contains("{widget}")) {
                args = Regex.Replace(args, "{mute}|{widget}", string.Empty);
                disableSpeech = true;
            }

            if (args.Contains("</lock>")) // lock is extension of judo parser. No need for extra parsing
                if (dataType.Equals(DataType.html))
                    return Judoers.JudoParser(args).Replace("\r", string.Empty).Replace("\n", "<br />");
                else
                    return Judoers.JudoParser(args);

            var InstructionSets = args.Replace('&', ';').Split(';')
                                                        .Where(s => !string.IsNullOrEmpty(s.Trim()))
                                                        .Select(s => s.Trim())
                                                        .Distinct().ToList();
            var results = new Dictionary<string, KeyValuePair<string, string>>();

            foreach (string Instruction in InstructionSets) {
                var exe = Execute(Instruction.Trim(), disableSpeech).Replace("\r", string.Empty);
                if (exe.EndsWith("\n"))
                    exe = exe.Substring(0, exe.LastIndexOf("\n"));
                var key = Instruction.Trim().Replace(" ", "_").Replace("%", string.Empty);
                results.Add(key, new KeyValuePair<string, string>(Instruction.Trim(), exe));
            }

            switch (dataType) {
                case DataType.html:
                    return results.ToDebugString().Replace("<", "&lt;").Replace(">", "&gt;").Replace("\n", "<br />");
                case DataType.json:
                    return results.ToJson();
            }
            return results.ToDebugString();
        }

        string Execute(string arg, bool disableSpeech) {
            string output = string.Empty;
            var method = Methods.Instance;

            try {
                if (arg.StartsWith("%") ||
                    arg.StartsWith("./") ||
                    arg.StartsWith("judo")) {
                    return ParsingTools.ParseTokens(arg);
                }
                else {
                    XmlNodeList xList = method.GetInstructionSet(arg.Replace("*", string.Empty));

                    if (xList.Count <= 0 && !arg.Contains("*")) {
                        if (output == string.Empty) {
                            Logger.Instance.Append(string.Format("obj [ Parser.Execute ]: arg [ {0}, not found. ]", arg));
                            output = arg + ", not found.";
                        }
                    }
                    else
                        foreach (XmlNode nodeItem in xList)
                            output += string.Format("{0}\r\n", ParsingTools.ParseTokens(nodeItem.InnerText));
                }

                if (method.HasInternetConnection() && !disableSpeech) {
                    if (!User.Status && output.Trim() != string.Empty && File.Exists(method.GetApplicationPath + ".smtpsettings")) {
                        XmlNodeList xList = method.GetMailHeaders;

                        foreach (XmlNode nodeItem in xList) {
                            Process.CallWithTimeout(() => new Net.Mail().Send(nodeItem.SelectSingleNode("MailFrom").InnerText,
                                                                              nodeItem.SelectSingleNode("MailTo").InnerText,
                                                                              nodeItem.SelectSingleNode("MailSubject").InnerText,
                                                                              output), 10000);
                        }
                    }
                }

                if (output.Trim() != string.Empty && !Mute && !disableSpeech) {
                    Task.Run(() => SayText(output));
                }

                if (!ParserState) {
                    Thread.Sleep(1000);
                    Environment.Exit(0);
                }

                return string.Format("{0}\r\n", output.Trim());
            }
            catch (Exception e) {
                if (!e.Message.Contains("Parameter name: length")) {
                    Logger.Instance.Append(string.Format("obj [ Parser.Execute <Exception> ]: Argument: [ {0} ] Exception: [ {1} ]", arg, e.Message));
                    return e.Message;
                }
                return string.Empty;
            }
        }

        //public void SayText(string sText) {
        //    SayText((object)sText);
        //}

        public void SayText(string sText) {
            lock (_speech_locker) {
                sText = sText.Replace("_", " ");
                if (OperatingSystem.Version == OperatingSystem.Type.Unix) {
                    if (File.Exists("/usr/bin/festival"))
                        Process.Instance.Start(string.Format("festival -b '(SayText \"{0}\")'", sText));
                    //Process.Start("festival -b '(SayText " + "\"" + sText.ToString().Replace("_", string.Empty) + "\"" + ")'");
                    else
                        Process.Instance.Start("say " + sText);
                }
                else {
                    string jspeechPath = Methods.Instance.GetApplicationPath + "jspeech.exe";
                    if (File.Exists(jspeechPath))
                        Process.Instance.Start(jspeechPath, sText);
                }
            }
        }
    }

    static class Judoers
    {
        static readonly object _serial_locker = new object();

        // params object[] args or DYNAMIC object?
        internal static string JudoParser(string arg) {
            var method = Methods.Instance;
            var com = new Comm();

            string output = string.Empty;
            List<string> args;

            args = !arg.Contains("</lock>") ?
                ParsingTools.SplitArguments(arg.ToValues()) // Parse it normally
                : ParsingTools.SplitArguments(arg);         // Leave it as is, code container

            switch (args[1]) {
                // TIMER
                case "timer":
                case "sleep":
                    Thread.Sleep(Convert.ToInt32(args[2]));
                    break;
                // SERIAL
                case "serial":
                    SerialComm.SerialData = string.Empty;
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
                            output = string.Format("{0}\r\n{1}", com.getComPort, com.getBaudRate);
                            break;
                        case "state":
                        case "status":
                        default:
                            output = string.Format("Serial port state: {0}", SerialComm.port.IsOpen);
                            break;
                    }
                    break;
                // HELP
                case "help":
                case "?":
                    output = args.Count() > 2 ?
                        output = method.getHelp(args[2])
                        :
                        output = method.getHelp("all");
                    break;
                // INSTRUCTION SETS
                case "inset":
                    switch (args[2]) {
                        case "add":
                        case "new":
                        case "set":
                        case "setup":
                            var isl = new List<InstructionSet>();
                            if (args.Count() == 5) {
                                isl.Add(new InstructionSet { Id = "*" + args[3], Action = args[4] });
                                isl.Add(new InstructionSet { Id = args[3], Action = "*" + args[3] });
                            }
                            else {
                                isl.Add(new InstructionSet { Id = "*" + args[3], Action = args[4] });
                                isl.Add(new InstructionSet {
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
                            isl.ForEach(item => output = method.AddToXML(item, AppStructure.SystemInstructionsRoot));
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
                // EVENTS
                case "event":
                    switch (args[2]) {
                        case "add":
                        case "new":
                        case "set":
                        case "setup":
                            method.AddToXML(new Event { Id = args[3], Action = args[4] }, AppStructure.SystemEventsRoot);
                            output = method.AddToXML(new InstructionSet { Id = args[3], Action = "%~>" + args[3] + "%" }, AppStructure.SystemEventsRoot);
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
                // SOCKET LISTENING MODE
                case "socket":
                    switch (args[2]) {
                        case "on":
                        case "enable":
                        case "start":
                        case "listen":
                        case "open":
                            Server.TCP.Start();
                            Thread.Sleep(50);
                            output = string.Format("Socket state: {0}", Server.TCP.ServerState);
                            break;
                        case "off":
                        case "disable":
                        case "stop":
                        case "close":
                            Server.TCP.Stop();
                            Thread.Sleep(50);
                            output = string.Format("Socket state: {0}", Server.TCP.ServerState);
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
                            output = string.Format("{0}\r\n{1}", com.getLocalHost, com.getLocalPort);
                            break;
                        case "state":
                        case "status":
                        default:
                            output = string.Format("Socket state: {0}", Server.TCP.ServerState);
                            break;
                    }
                    break;
                // WEB SERVER MODE
                case "server":
                    switch (args[2]) {
                        case "on":
                        case "enable":
                        case "start":
                        case "listen":
                            Server.Web.Start();
                            Thread.Sleep(50);
                            output = string.Format("Web server state: {0}", Server.Web.httplistener.IsListening);
                            break;
                        case "off":
                        case "disable":
                        case "stop":
                            Server.Web.Stop();
                            Thread.Sleep(50);
                            output = string.Format("Web server state: {0}", Server.Web.httplistener.IsListening);
                            break;
                        case "login":
                        case "cred":
                        case "credentials":
                            output = new Settings().SaveSettings(".htaccess", string.Format("{0}\r\n{1}", args[3], args[4]));
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
                            output = string.Format("{0}\r\n{1}\r\n{2}", com.getHostname, com.getHttpPort, com.getAuthentication);
                            break;
                        case "state":
                        case "status":
                        default:
                            output = string.Format("Web server state: {0}", Server.Web.httplistener.IsListening);
                            break;
                    }
                    break;
                // SCHEDULER
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
                // SMTP
                case "smtp":
                    switch (args[2]) {
                        case "add":
                        case "setup":
                        case "set":
                            output = new Settings().SaveSettings(".smtpsettings",
                                                                  string.Format("{0}\r\n{1}\r\n{2}\r\n{3}\r\n{4}",
                                                                  args[3], args[4], args[5], args[6], args[7]));
                            break;
                        case "settings":
                            var smtpSettings = new Net.Mail.SmtpSettings();
                            output = string.Format("{0}\r\n{1}\r\n{2}\r\n{3}\r\n{4}",
                                                    smtpSettings.Host, smtpSettings.Username, smtpSettings.Password, smtpSettings.Port, smtpSettings.SSL);
                            break;
                    }
                    break;
                // POP3
                case "pop3":
                    switch (args[2]) {
                        case "add":
                        case "setup":
                        case "set":
                            output = new Settings().SaveSettings(".pop3settings",
                                                                  string.Format("{0}\r\n{1}\r\n{2}\r\n{3}\r\n{4}",
                                                                  args[3], args[4], args[5], args[6], args[7]));
                            break;
                        case "settings":
                            var pop3Settings = new Net.Mail.Pop3Settings();
                            output = string.Format("{0}\r\n{1}\r\n{2}\r\n{3}\r\n{4}",
                                                    pop3Settings.Host, pop3Settings.Username, pop3Settings.Password, pop3Settings.Port, pop3Settings.SSL);
                            break;
                    }
                    break;
                // GMAIL
                case "gmail":
                    switch (args[2]) {
                        case "add":
                        case "setup":
                        case "set":
                            output = new Settings().SaveSettings(".gmailsettings", string.Format("{0}\r\n{1}", args[3], args[4]));
                            break;
                        case "settings":
                            var gmailSettings = new Net.Mail.GmailSettings();
                            output = string.Format("{0}\r\n{1}", gmailSettings.Username, gmailSettings.Password);
                            break;
                    }
                    break;
                // MAIL
                case "mail":
                    switch (args[2]) {
                        case "send":
                            output = new Net.Mail().Send(args[3], args[4], args[5], args[6]).ToString()
                                                   .Replace("True", "Mail sent!").Replace("False", "Mail could not be sent");
                            break;
                    }
                    break;
                // SMS
                case "sms":
                    switch (args[2]) {
                        case "add":
                        case "setup":
                        case "set":
                            output = new Settings().SaveSettings(".smssettings", string.Format("{0}\r\n{1}\r\n{2}", args[3], args[4], args[5]));
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
                // WEB API
                case "json":
                    switch (args[2]) {
                        case "add":
                        case "new":
                        case "set":
                        case "setup":
                            var isl = new List<InstructionSet>();
                            isl.Add(new InstructionSet {Id = "*" + args[3],
                                Action = "judo json get " + Server.Web.SimpleUriEncode(args[4]) + " " + args[5] });
                            isl.Add(new InstructionSet {Id = args[3],
                                Action = "*" + args[3] });
                            isl.ForEach(item => output = method.AddToXML(item, AppStructure.SystemInstructionsRoot));
                            break;
                        case "get":
                        case "response":
                        case "consume":
                        case "extract":
                            output = new Helpers.Json().SelectSingleNode(Server.Web.SimpleUriDecode(args[3]), args[4]);
                            break;
                    }
                    break;
                // WEB SERVICE
                case "xml":
                    switch (args[2]) {
                        case "add":
                        case "new":
                        case "set":
                        case "setup":
                            var isl = new List<InstructionSet>();
                            switch (args.Count()) {
                                case 6:
                                    isl.Add(new InstructionSet { Id = "*" + args[3],
                                        Action = "judo xml get " + Server.Web.SimpleUriEncode(args[4]) + " " + args[5] });
                                    isl.Add(new InstructionSet { Id = args[3], Action = "*" + args[3] });
                                    break;
                                case 7:
                                    isl.Add(new InstructionSet { Id = "*" + args[3],
                                        Action = "judo xml get " + Server.Web.SimpleUriEncode(args[4]) + " " + args[5] + " " + args[6] });
                                    isl.Add(new InstructionSet { Id = args[3], Action = "*" + args[3] });
                                    break;
                                case 8:
                                    isl.Add(new InstructionSet { Id = "*" + args[3],
                                        Action = "judo xml get " + Server.Web.SimpleUriEncode(args[4]) + " " + args[5] + " " + args[6] + " " + args[7] });
                                    isl.Add(new InstructionSet { Id = args[3], Action = "*" + args[3] });
                                    break;
                            }
                            isl.ForEach(item => output = method.AddToXML(item, AppStructure.SystemInstructionsRoot));
                            break;
                        case "get":
                        case "response":
                        case "consume":
                        case "extract":
                            switch (args.Count()) {
                                case 5:
                                    output = Helpers.Xml.SelectSingleNode(Server.Web.SimpleUriDecode(args[3]), args[4]);
                                    break;
                                case 6:
                                    output = args[4].Contains("=") ?
                                        Helpers.Xml.SelectNodes(Server.Web.SimpleUriDecode(args[3]), args[4], args[5])[0] :
                                        Helpers.Xml.SelectSingleNode(Server.Web.SimpleUriDecode(args[3]), args[4], Convert.ToInt32(args[5]));
                                    break;
                                case 7:
                                    output = Helpers.Xml.SelectNodes(Server.Web.SimpleUriDecode(args[3]), args[4], args[5])[Convert.ToInt32(args[6])];
                                    break;
                            }
                            break;
                    }
                    break;
                // HTTP
                case "http":
                    switch (args[2]) {
                        case "get":
                            output = Helpers.Http.Get(Server.Web.SimpleUriDecode(args[3]));
                            break;
                    }
                    break;
                // WEATHER
                case "weather":
                    switch (args[2]) {
                        case "set":
                        case "setup":
                            output = method.AddToXML(new Others { Weather = args[3] }, AppStructure.SystemOthersRoot);
                            break;
                        case "settings":
                            output = new Others().getWeather;
                            break;
                    }
                    break;
                // PINGER
                case "ping":
                    output = args.Count() == 3 ?
                        Net.SimplePing.Ping(args[2]).ToString() :
                        Net.SimplePing.Ping(args[2], Convert.ToInt32(args[3])).ToString();
                    break;
            }
            return output;
        }
    }
}