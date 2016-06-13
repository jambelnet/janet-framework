/* *****************************************************************************************************************************
 * (c) J@mBeL.net 2010-2016
 * Author: John Ambeliotis
 * Created: 24 Apr. 2010
 *
 * License:
 *  This file is part of Project jaNET.

    Project jaNET is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    Project jaNET is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with Project jaNET. If not, see <http://www.gnu.org/licenses/>. */

using System;
using System.IO;
using System.Xml;
using System.Threading;
using System.Linq;

namespace jaNETFramework
{
    public class Parser
    {
        internal static Parser Instance { get { return Singleton<Parser>.Instance; } }
    
        /// <summary>
        /// returns the status of the parser.
        /// </summary>
        public static volatile bool ParserState = true;
        internal static volatile bool Mute;
        static readonly object _speech_locker = new object();

        /// <summary>
        /// args - more than one in-line argument separated by semicolon.
        /// Example: Parse ("judo serial open; judo server start; %checkin%);
        /// ReturnAsHTML - In web request it formats the \r\n to html break <br />.
        /// </summary>
        /// <param name="args">
        /// A <see cref="System.String"/>
        /// </param>
        /// <param name = "returnAsHTML"></param>
        /// <param name="ReturnAsHTML">
        /// A <see cref="System.Boolean"/>
        /// </param>
        /// <returns>
        /// A <see cref="System.String"/>
        /// </returns>
        /// <param name="disableSpeech"></param>
        internal string Parse(string args, bool returnAsHTML, bool disableSpeech)
        {
            if (args.Contains("{mute}") || args.Contains("{widget}"))
            {
                args = args.Replace("{mute}", string.Empty).Replace("{widget}", string.Empty);
                disableSpeech = true;
            }
            if (args.Contains("</lock>"))
                if (returnAsHTML)
                    return Judoers.JudoParser(args).Replace("\r", string.Empty).Replace("\n", "<br />");
                else
                    return Judoers.JudoParser(args);

            string[] InstructionSet = args.Split(';');
            string results = string.Empty;

            foreach (string Instruction in InstructionSet)
                if (Instruction.Trim() != string.Empty)
                    results += Execute(Instruction.Trim(), disableSpeech);

            return returnAsHTML ?
                results.Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r", string.Empty).Replace("\n", "<br />") : results;
        }

        string Execute(string arg, bool disableSpeech)
        {
            string output = string.Empty;
            var method = Methods.Instance;

            try
            {
                if (arg.Substring(0, 1) == "%" ||
                    arg.Substring(0, 2) == "./" ||
                    arg.Length >= 4 && (arg.Substring(0, 4) == "judo"))
                    return ParsingTools.ParseTokens(arg);

                else
                {
                    XmlNodeList xList = method.GetInstructionSet(arg.Replace("*", string.Empty));

                    if (xList.Count <= 0 && !arg.Contains("*"))
                    {
                        output = Judoers.IntelParser(arg);
                        if (output == string.Empty)
                        {
                            Logger.Instance.Append("obj [ Parser.Execute ]: arg [ " + arg + ", not found. ]");
                            output = arg + ", not found.";
                        }
                    }
                    else
                        foreach (XmlNode nodeItem in xList)
                            output += string.Format("{0}\r\n", ParsingTools.ParseTokens(nodeItem.InnerText));
                }

                if (method.HasInternetConnection() && !disableSpeech)
                {
                    if (!User.Status && output.Trim() != string.Empty && File.Exists(method.GetApplicationPath() + ".smtpsettings"))
                    {
                        XmlNodeList xList = method.GetMailHeaders();

                        foreach (XmlNode nodeItem in xList)
                        {
                            Action SendNotification = () => new Net.Mail().Send(nodeItem.SelectSingleNode("MailFrom").InnerText,
                                                                                nodeItem.SelectSingleNode("MailTo").InnerText,
                                                                                nodeItem.SelectSingleNode("MailSubject").InnerText,
                                                                                output);
                                //Judoers.JudoParser("judo mail send " + nodeItem.SelectSingleNode("MailFrom").InnerText + " " +
                                                                                                   //nodeItem.SelectSingleNode("MailTo").InnerText + " `" +
                                                                                                   //nodeItem.SelectSingleNode("MailSubject").InnerText + "` `" +
                                                                                                   //output + "`");
                            Process.CallWithTimeout(SendNotification, 10000);
                        }
                    }
                }

                if (output.Trim() != string.Empty && !Mute && !disableSpeech)
                {
                    var t = new Thread(() => SayText(output.Replace("Parser: ", string.Empty)));
                    t.IsBackground = true;
                    t.Start();
                }

                if (!ParserState)
                {
                    Thread.Sleep(1000);
                    Environment.Exit(0);
                }

                return string.Format("{0}\r\n", output.Trim());
            }
            catch (Exception e)
            {
                if (!e.Message.Contains("Parameter name: length"))
                {
                    Logger.Instance.Append("obj [ Parser.Execute <Exception> ]: Argument: [ " + arg + " ] Exception: [ " + e.Message + " ]");
                    return e.Message;
                }
                return string.Empty;
            }
        }

        /// <summary>
        /// speek something.
        /// </summary>
        /// <param name="sText">
        /// A <see cref="System.String"/>
        /// </param>
        public static void SayText(string sText)
        {
            SayText((object)sText);
        }

        static void SayText(object sText)
        {
            lock (_speech_locker)
            {
                if (OperatingSystem.Version == OperatingSystem.Type.Unix)
                {
                    if (File.Exists("/usr/bin/festival"))
                        Process.Instance.Start(string.Format("festival -b '(SayText \"{0}\")'", sText.ToString().Replace("_", string.Empty)));
                        //Process.Start("festival -b '(SayText " + "\"" + sText.ToString().Replace("_", string.Empty) + "\"" + ")'");
                    else
                        Process.Instance.Start("say " + sText.ToString().Replace("_", string.Empty));
                }
                else
                {
                    String jspeechPath = Methods.Instance.GetApplicationPath() + "jspeech.exe";
                    if (File.Exists(jspeechPath))
                        Process.Instance.Start(jspeechPath, sText.ToString().Replace("_", string.Empty));
                }
            }
        }
    }

    static class Judoers
    {
        static readonly object _serial_locker = new object();

        internal static string JudoParser(string arg)
        {
            var appset = new ApplicationSettings();
            var method = Methods.Instance;

            string output = string.Empty;
            string[] args;

            args = !arg.Contains("</lock>") ?
                ParsingTools.SplitArguments(arg.ToValues()) // Parse it normally
                : ParsingTools.SplitArguments(arg); // Leave it as is, code container

            switch (args[1])
            {
                // TIMER
                case "timer":
                case "sleep":
                    Thread.Sleep(Convert.ToInt32(args[2]));
                    break;
                // SERIAL
                case "serial":
                    SerialComm.SerialData = string.Empty;
                    switch (args[2])
                    {
                        case "open":
                            if (args.Count() > 3)
                                SerialComm.ActivateSerialPort(args[3]);
                            else
                                SerialComm.ActivateSerialPort(string.Empty);
                            Thread.Sleep(50);
                            output = "Serial port state: " + SerialComm.port.IsOpen;
                            break;
                        case "close":
                            SerialComm.DeactivateSerialPort();
                            Thread.Sleep(50);
                            output = "Serial port state: " + SerialComm.port.IsOpen;
                            break;
                        case "send":
                        case "listen":
                        case "monitor":
                            try
                            {
                                lock (_serial_locker)
                                {
                                    if (SerialComm.port.IsOpen)
                                    {
                                        if (args[2] == "send")
                                        {
                                            // Clear all buffers
                                            SerialComm.port.DiscardInBuffer();
                                            SerialComm.port.DiscardOutBuffer();
                                            SerialComm.SerialData = string.Empty;
                                            // Send a new argument
                                            SerialComm.port.WriteLine(args[3]);
                                            Thread.Sleep(220);
                                        }
                                        Action getSerialData = () =>
                                        {
                                            while (output == string.Empty)
                                            {
                                                output = SerialComm.SerialData;
                                                Thread.Sleep(50);
                                            }
                                        };
                                        if ((args[2] == "listen" || args[2] == "monitor") && args.Count() > 3)
                                            Process.CallWithTimeout(getSerialData, Convert.ToInt32(args[3]));
                                        else
                                            Process.CallWithTimeout(getSerialData, 10000);
                                    }
                                    else
                                        output = "Serial port state: " + SerialComm.port.IsOpen;
                                }
                            }
                            catch
                            {
                                // Suppress
                            }
                            break;
                        case "set":
                        case "setup":
                            if (args.Count() >= 4)
                                output = method.AddToXML(new InstructionSet(args[3]),
                                                                            ApplicationSettings.ApplicationStructure.SystemCommRoot,
                                                                            ApplicationSettings.ApplicationStructure.ComPortElement);
                            if (args.Count() == 5)
                                output = method.AddToXML(new InstructionSet(args[4]),
                                                                            ApplicationSettings.ApplicationStructure.SystemCommRoot,
                                                                            ApplicationSettings.ApplicationStructure.ComBaudRateElement);
                            break;
                        case "settings":
                            output = string.Format("{0}\r\n{1}", appset.ComPort, appset.Baud);
                            break;
                        case "state":
                        case "status":
                        default:
                            output = "Serial port state: " + SerialComm.port.IsOpen;
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
                    switch (args[2])
                    {
                        case "add":
                        case "new":
                        case "set":
                        case "setup":
                            if (args.Count() == 5)
                                output = method.AddToXML(new InstructionSet(args[3],   // ID
                                                                            args[4]),  // Action
                                                                            ApplicationSettings.ApplicationStructure.SystemInstructionsRoot,
                                                                            "InstructionSet");
                            else
                                output = method.AddToXML(new InstructionSet(args[3],                                                           // ID
                                                                            args[4],                                                           // Action
                                                                            args[5].Replace("\"", string.Empty).Replace("'", string.Empty),    // Category
                                                                            args[6].Replace("\"", string.Empty).Replace("'", string.Empty),    // Header
                                                                            args[7].Replace("\"", string.Empty).Replace("'", string.Empty),    // Short Description
                                                                            args[8].Replace("\"", string.Empty).Replace("'", string.Empty),    // Description
                                                                            args[9].Replace("\"", string.Empty).Replace("'", string.Empty),    // Thumbnail Url
                                                                            args[10].Replace("\"", string.Empty).Replace("'", string.Empty)),  // Reference
                                                                            ApplicationSettings.ApplicationStructure.SystemInstructionsRoot,
                                                                            "InstructionSet");
                            break;
                        case "remove":
                        case "rm":
                        case "delete":
                        case "del":
                        case "kill":
                            output = method.RemoveFromXML(args[3], ApplicationSettings.ApplicationStructure.SystemInstructionsRoot, "InstructionSet");
                            break;
                        case "list":
                        case "ls":
                        default:
                            XmlNodeList xList = method.GetXmlElementList(ApplicationSettings.ApplicationStructure.SystemInstructionsRoot, "InstructionSet");
                            foreach (XmlNode nodeItem in xList)
                                output += string.Format("{0}\r\n", nodeItem.OuterXml);
                            break;
                    }
                    break;
                // EVENTS
                case "event":
                    switch (args[2])
                    {
                        case "add":
                        case "new":
                        case "set":
                        case "setup":
                            output = method.AddToXML(new InstructionSet(args[3],
                                                                        args[4]),
                                                                        ApplicationSettings.ApplicationStructure.SystemEventsRoot,
                                                                        "event");
                            break;
                        case "remove":
                        case "rm":
                        case "delete":
                        case "del":
                        case "kill":
                            output = method.RemoveFromXML(args[3], ApplicationSettings.ApplicationStructure.SystemEventsRoot, "event");
                            break;
                        case "list":
                        case "ls":
                        default:
                            XmlNodeList xList = method.GetXmlElementList(ApplicationSettings.ApplicationStructure.SystemEventsRoot, "event");
                            foreach (XmlNode nodeItem in xList)
                                output += string.Format("{0}\r\n", nodeItem.OuterXml);
                            break;
                    }
                    break;
                // SOCKET LISTENING MODE
                case "socket":
                    switch (args[2])
                    {
                        case "on":
                        case "enable":
                        case "start":
                        case "listen":
                        case "open":
                            Server.TCP.Start();
                            Thread.Sleep(50);
                            output = "Socket state: " + Server.TCP.ServerState;
                            break;
                        case "off":
                        case "disable":
                        case "stop":
                        case "close":
                            Server.TCP.Stop();
                            Thread.Sleep(50);
                            output = "Socket state: " + Server.TCP.ServerState;
                            break;
                        case "set":
                        case "setup":
                            if (args.Count() >= 4)
                                output = method.AddToXML(new InstructionSet(args[3]),
                                                                            ApplicationSettings.ApplicationStructure.SystemCommRoot,
                                                                            ApplicationSettings.ApplicationStructure.LocalHostElement);
                            if (args.Count() == 5)
                                output = method.AddToXML(new InstructionSet(args[4]),
                                                                            ApplicationSettings.ApplicationStructure.SystemCommRoot,
                                                                            ApplicationSettings.ApplicationStructure.LocalPortElement);
                            break;
                        case "settings":
                            output = string.Format("{0}\r\n{1}", appset.LocalHost, appset.LocalPort);
                            break;
                        case "state":
                        case "status":
                        default:
                            output = "Socket state: " + Server.TCP.ServerState;
                            break;
                    }
                    break;
                // WEB SERVER MODE
                case "server":
                    switch (args[2])
                    {
                        case "on":
                        case "enable":
                        case "start":
                        case "listen":
                            Server.Web.Start();
                            Thread.Sleep(50);
                            output = "Web server state: " + Server.Web.httplistener.IsListening;
                            break;
                        case "off":
                        case "disable":
                        case "stop":
                            Server.Web.Stop();
                            Thread.Sleep(50);
                            output = "Web server state: " + Server.Web.httplistener.IsListening;
                            break;
                        case "login":
                        case "cred":
                        case "credentials":
                            output = new Settings().SaveSettings(".htaccess", string.Format("{0}\r\n{1}", args[3], args[4]));
                            break;
                        case "set":
                        case "setup":
                            if (args.Count() >= 4)
                                output = method.AddToXML(new InstructionSet(args[3]),
                                                                            ApplicationSettings.ApplicationStructure.SystemCommRoot,
                                                                            ApplicationSettings.ApplicationStructure.HttpHostNameElement);
                            if (args.Count() >= 5)
                                output = method.AddToXML(new InstructionSet(args[4]),
                                                                            ApplicationSettings.ApplicationStructure.SystemCommRoot,
                                                                            ApplicationSettings.ApplicationStructure.HttpPortElement);
                            if (args.Count() == 6)
                                output = method.AddToXML(new InstructionSet(args[5]),
                                                                            ApplicationSettings.ApplicationStructure.SystemCommRoot,
                                                                            ApplicationSettings.ApplicationStructure.HttpAuthenticationElement);
                            break;
                        case "settings":
                            output = string.Format("{0}\r\n{1}\r\n{2}", appset.HostName, appset.HttpPort, appset.Authentication);
                            break;
                        case "state":
                        case "status":
                        default:
                            output = "Web server state: " + Server.Web.httplistener.IsListening;
                            break;
                    }
                    break;
                // SCHEDULER
                case "schedule":
                    switch (args[2])
                    {
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
                                if (args.Count() > 3)
                                {
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
                    switch (args[2])
                    {
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
                    switch (args[2])
                    {
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
                    switch (args[2])
                    {
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
                    switch (args[2])
                    {
                        case "send":
                            output = new Net.Mail().Send(args[3], args[4], args[5], args[6]).ToString()
                                                   .Replace("True", "Mail sent!").Replace("False", "Mail could not be sent");
                            break;
                    }
                    break;
                // SMS
                case "sms":
                    switch (args[2])
                    {
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
                    switch (args[2])
                    {
                        case "add":
                        case "new":
                        case "set":
                        case "setup":
                            output = method.AddToXML(new InstructionSet(args[3], // ID
                                                                        "judo json get " + Server.Web.SimpleUriEncode(args[4]) + " " + args[5]), //Action
                                                                        ApplicationSettings.ApplicationStructure.SystemInstructionsRoot,
                                                                        "InstructionSet");
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
                    switch (args[2])
                    {
                        case "add":
                        case "new":
                        case "set":
                        case "setup":
                            switch (args.Count())
                            {
                                case 6:
                                    output = method.AddToXML(new InstructionSet(args[3], // ID
                                                             "judo xml get " + Server.Web.SimpleUriEncode(args[4]) + " " + args[5]), //Action
                                                             ApplicationSettings.ApplicationStructure.SystemInstructionsRoot, "InstructionSet");
                                    break;
                                case 7:
                                    output = method.AddToXML(new InstructionSet(args[3], // ID
                                                             "judo xml get " + Server.Web.SimpleUriEncode(args[4]) + " " + args[5] + " " + args[6]), //Action
                                                             ApplicationSettings.ApplicationStructure.SystemInstructionsRoot, "InstructionSet");
                                    break;
                                case 8:
                                    output = method.AddToXML(new InstructionSet(args[3], // ID
                                                             "judo xml get " + Server.Web.SimpleUriEncode(args[4]) + " " + args[5] + " " + args[6] + " " + args[7]), //Action
                                                             ApplicationSettings.ApplicationStructure.SystemInstructionsRoot, "InstructionSet");
                                    break;
                            }
                            break;
                        case "get":
                        case "response":
                        case "consume":
                        case "extract":
                            switch (args.Count())
                            {
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
                    switch (args[2])
                    {
                        case "get":
                            output = Helpers.Http.Get(Server.Web.SimpleUriDecode(args[3]));
                            break;
                    }
                    break;
                // WEATHER
                case "weather":
                    switch (args[2])
                    {
                        case "set":
                        case "setup":
                            output = method.AddToXML(new InstructionSet(args[3]),
                                                     ApplicationSettings.ApplicationStructure.SystemOthersRoot,
                                                     ApplicationSettings.ApplicationStructure.Weather);
                            break;
                        case "settings":
                            output = appset.Weather;
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

        internal static string IntelParser(string arg)
        {
            string output = string.Empty;
            var method = Methods.Instance;

            string tmpSchedule = "schedule-" + method.GetDay() + method.GetCalendarYear() + "_" + method.GetHour() + method.GetMinute() + DateTime.Now.Second;

            arg = arg.ToLower();

            if (arg.Contains("set alarm at") || arg.Contains("set an alarm for") || arg.Contains("set alarm for"))
            {
                string t = arg.Replace("set alarm at", string.Empty).Replace(".", string.Empty)
                              .Replace("set an alarm for", string.Empty).Replace(".", string.Empty)
                              .Replace("set alarm for", string.Empty).Replace(".", string.Empty)
                              .Trim();
                string when = "%calendardate%";

                if (Convert.ToInt32(t.ToHour24().Substring(0, t.ToHour24().IndexOf(':'))) < DateTime.Now.Hour ||
                    Convert.ToInt32(t.ToHour24().Substring(0, t.ToHour24().IndexOf(':'))) >= DateTime.Now.Hour && Convert.ToInt32(t.ToHour24().Substring(t.ToHour24().IndexOf(':') + 1)) < DateTime.Now.Minute)
                    when = String.Format("{0:d/M/yyyy}", DateTime.Now.AddDays(1).Date);

                Judoers.JudoParser("judo schedule add " + tmpSchedule + " " + when + " " + t.ToHour24() + " __SYS_ALARM");
                output = "Setting alarm for " + t;
            }
            if (arg.Contains("repeat after me"))
                Parser.SayText(arg.Replace("repeat after me", string.Empty).Trim());

            return output;
        }
    }
}