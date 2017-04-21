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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Xml;
using System.Xml.Linq;

namespace jaNET.Environment
{
    public class Methods
    {
        public static Methods Instance { get { return Singleton<Methods>.Instance; } }

        public const string AssemblyVersion = "0.3.0.13";

        public string GetCopyright {
            get {
                string cp = string.Format("jaNET Framework [Version {0}]\r\nCopyright (c) 2010-{1} J@mBeL.net", AssemblyVersion, DateTime.Now.Year);

                if (UpdateAvailable(Convert.ToInt32(AssemblyVersion.Replace(".", string.Empty))))
                    cp += "\r\n\r\nNew update available.\r\nPlease visit http://www.jubito.org/download.html";

                return cp;
            }
        }

        public bool UpdateAvailable(int assemblyVersion) {
            int currentVersion;
            try {
                int.TryParse(Helpers.GetRawData("http://www.jubito.org/current-version.txt"), out currentVersion);
            }
            catch (WebException) {
                // 404 Not Found
                return false;
            }
            return (currentVersion > assemblyVersion);
        }

        public string GetWinPath {
            get {
                return Directory.GetCurrentDirectory();
            }
        }

        public string GetLinuxPath {
            get {
                return System.Environment.CommandLine.Substring(0, System.Environment.CommandLine.LastIndexOf("/"));
            }
        }

        public string GetApplicationPath {
            get {
                string Path = string.Empty;

                if (OperatingSystem.Version == OperatingSystem.Type.Unix ||
                    OperatingSystem.Version == OperatingSystem.Type.MacOS)
                    Path = GetLinuxPath + "/";
                else if (OperatingSystem.Version == OperatingSystem.Type.Windows)
                    Path = GetWinPath + @"\";

                return Path;
            }
        }

        public string GetPartOfDay(bool salute) {
            if (DateTime.Now.Hour < 6)
                if (salute) return "morning";
                else return "midnight";

            if (DateTime.Now.Hour < 12)
                return "morning";

            if (DateTime.Now.Hour < 18)
                return "afternoon";

            if (DateTime.Now.Hour < 20)
                return "evening";

            if (DateTime.Now.Hour < 24)
                if (salute) return "evening";
                else return "night";

            return string.Empty;
        }

        public string GetSalute {
            get {
                return GetPartOfDay(true);
            }
        }

        public string GetTime {
            get {
                return (string.Format("{0:t}", DateTime.Now));
            }
        }

        public string GetTime24 {
            get {
                return (string.Format("{0:HH:mm}", DateTime.Now));
            }
        }

        public string GetHour {
            get {
                return (string.Format("{0:HH}", DateTime.Now));
            }
        }

        public string GetMinute {
            get {
                return (string.Format("{0:mm}", DateTime.Now));
            }
        }

        public string GetDate {
            get {
                return (string.Format("{0:M}", DateTime.Now));
            }
        }

        public string GetCalendarDate {
            get {
                return (string.Format("{0:d/M/yyyy}", DateTime.Now));
            }
        }

        public string GetDay {
            get {
                return (DateTime.Now.DayOfWeek.ToString());
            }
        }

        public string GetCalendarDay {
            get {
                return (DateTime.Now.Day.ToString());
            }
        }

        public string GetCalendarMonth {
            get {
                return (DateTime.Now.Month.ToString());
            }
        }

        public string GetCalendarYear {
            get {
                return (DateTime.Now.Year.ToString());
            }
        }

        public string WhoAmI {
            get {
                return System.Environment.UserName;
            }
        }

        public XmlNodeList GetEvent(string eventID) {
            return Helpers.Xml.AppConfigQuery(
                AppStructure.SystemEventsRoot +
                "/event[@id='" + eventID + "']");
        }

        public XmlNodeList GetInstructionSet(string instructionID) {
            return Helpers.Xml.AppConfigQuery(
                AppStructure.SystemInstructionsRoot +
                "/InstructionSet[@id='" + instructionID + "']");
        }

        public XmlNodeList GetMailHeaders {
            get {
                return Helpers.Xml.AppConfigQuery("jaNET/System/Alerts/MailHeaders");
            }
        }

        public XmlNodeList GetXmlElementList(string xPath, string elementName) {
            return Helpers.Xml.AppConfigQuery(xPath + "/" + elementName);
        }

        public bool HasInternetConnection() {
            return HasInternetConnection(string.Empty);
        }

        public bool HasInternetConnection(string host) {
            try {
                if (host == string.Empty)
                    host = "http://www.google.com";
                using (var client = new WebClient())
                using (var stream = client.OpenRead(host))
                    return true;
            }
            catch {
                return false;
            }
        }

        internal string AddToXML(object newElement, string xPath) {
            // http://stackoverflow.com/questions/6637679/reflection-get-attribute-name-and-value-on-property

            string path = GetApplicationPath + "AppConfig.xml";
            string backup = path + ".bak";

            try {
                var xdoc = XDocument.Load(path);

                var props = newElement.GetType().GetProperties().ToList();
                props.RemoveAll(item => item.GetValue(newElement, null) == null ||
                                item.GetValue(newElement, null).ToString().Trim() == string.Empty ||
                                item.CustomAttributes.FirstOrDefault().AttributeType.Name == "XmlTextAttribute");

                File.Copy(path, backup, true);
                File.Delete(path);

                xPath = xPath.Substring(xPath.LastIndexOf('/') + 1);
                string element = newElement.GetType().CustomAttributes.FirstOrDefault().NamedArguments.FirstOrDefault().TypedValue.Value.ToString();

                if (newElement is InstructionSet || newElement is Event) {
                    var xattrs = new List<XAttribute>();

                    props.ForEach(p => xattrs.Add(
                        new XAttribute(
                            typeof(InstructionSet).GetProperty(p.Name).GetCustomAttributesData().FirstOrDefault()
                            .NamedArguments.FirstOrDefault().TypedValue.Value.ToString(),
                            p.GetValue(newElement))));

                    xdoc.Root.Element(xPath)
                                .Add(
                                    new XElement(element,
                                        xattrs,
                                        new XText((string)newElement.GetType().GetProperty("Action").GetValue(newElement, null).ToString().Trim())
                                    ));
                }
                else
                    props.ForEach(a => xdoc.Descendants(a.CustomAttributes.FirstOrDefault().NamedArguments.FirstOrDefault().TypedValue.Value.ToString())
                                           .Single().Value = a.GetValue(newElement, null).ToString());

                xdoc.Save(path);
                return "Element added.";
            }
            catch (Exception e) {
                // Restore the AppConfig.xml
                File.Copy(backup, path);
                return string.Format("{0}\r\nPlease try again.", e.Message);
            }
        }

        internal string RemoveFromXML(string elementID, string xPath, string elementName) {
            string path = GetApplicationPath + "AppConfig.xml";
            string backup = path + ".bak";

            try {
                var xmldoc = new XmlDocument();
                using (XmlReader xmlReader = XmlReader.Create(path))
                    xmldoc.Load(xmlReader);

                File.Copy(path, path + ".bak", true);
                File.Delete(path);

                // Remove Handler
                XmlNodeList xmlnodelist = xmldoc.SelectNodes(xPath + "/" + elementName + "[@id='" + elementID + "']");
                RemoveElement(xmlnodelist);
                // Remove Launcher
                xmlnodelist = xmldoc.SelectNodes(xPath + "/" + elementName + "[@id='*" + elementID + "']");
                RemoveElement(xmlnodelist);

                xmldoc.Save(path);

                return "Element removed.";
            }
            catch (Exception e) {
                // Restore the AppConfig.xml
                File.Copy(backup, path);
                return string.Format("{0}\r\nPlease try again.", e.Message);
            }
        }

        void RemoveElement(XmlNodeList xmlnodelist) {
            for (int i = xmlnodelist.Count - 1; i >= 0; i--) {
                XmlNode xmlnode = xmlnodelist[i];
                xmlnode.ParentNode.RemoveChild(xmlnode);
            }
        }

        internal string GetHelp(string appendix) {
            const
            string _inset = "1.  Instruction Sets & Events\r\n" +
                                "     1.1 Add New Instruction Set\r\n" +
                                "         + judo inset add [ID] <lock>[Action]</lock>\r\n" +
                                "         + judo inset new [ID] <lock>[Action]</lock>\r\n" +
                                "         + judo inset set [ID] <lock>[Action]</lock>\r\n" +
                                "         + judo inset setup [ID] <lock>[Action]</lock>\r\n" +
                                "         + judo inset add [ID] <lock>[Action]</lock> `[Category]` `[Header]` `[Short  Description]` `[Long Description]` `[Thumbnail Url]` `[Reference]`\r\n" +
                                "         + judo inset new [ID] <lock>[Action]</lock> `[Category]` `[Header]` `[Short  Description]` `[Long Description]` `[Thumbnail Url]` `[Reference]`\r\n" +
                                "         + judo inset set [ID] <lock>[Action]</lock> `[Category]` `[Header]` `[Short  Description]` `[Long Description]` `[Thumbnail Url]` `[Reference]`\r\n" +
                                "         + judo inset setup  [ID] <lock>[Action]</lock> `[Category]` `[Header]` `[Short Description]` `[Long Description]` `[Thumbnail Url]` `[Reference]`\r\n" +
                                "     1.2 Remove Instruction Set\r\n" +
                                "         + judo inset remove [ID]\r\n" +
                                "         + judo inset delete [ID]\r\n" +
                                "         + judo inset del [ID]\r\n" +
                                "         + judo inset kill [ID]\r\n" +
                                "     1.3 List Items\r\n" +
                                "         + judo inset list\r\n" +
                                "         + judo inset ls\r\n" +
                                "     1.4 Add New Event Handler\r\n" +
                                "         + judo event add [ID] <lock>[Action]</lock>\r\n" +
                                "         + judo event new [ID] <lock>[Action]</lock>\r\n" +
                                "         + judo event set [ID] <lock>[Action]</lock>\r\n" +
                                "         + judo event setup [ID] <lock>[Action]</lock>\r\n" +
                                "     1.5 Remove Event Handler\r\n" +
                                "         + judo event remove [ID]\r\n" +
                                "         + judo event delete [ID]\r\n" +
                                "         + judo event del [ID]\r\n" +
                                "         + judo event kill [ID]\r\n" +
                                "     1.6 Delay Between Actions\r\n" +
                                "         + judo sleep [timeout in ms]\r\n" +
                                "         + judo timer [timeout in ms]\r\n" +
                                "     1.7 List Items\r\n" +
                                "         + judo event list\r\n" +
                                "         + judo event ls";
            const
            string _mail = "2.  Mail\r\n" +
                                "     2.1 Smtp Settings\r\n" +
                                "         + judo smtp add [Host] [Username] [Password] [Port] [Ssl]\r\n" +
                                "         + judo smtp setup [Host] [Username] [Password] [Port] [Ssl]\r\n" +
                                "         + judo smtp set [Host] [Username] [Password] [Port] [Ssl]\r\n" +
                                "         + judo smtp settings\r\n" +
                                "     2.2 Pop3 Settings\r\n" +
                                "         + judo pop3 add [Host] [Username] [Password] [Port] [Ssl]\r\n" +
                                "         + judo pop3 setup [Host] [Username] [Password] [Port] [Ssl]\r\n" +
                                "         + judo pop3 set [Host] [Username] [Password] [Port] [Ssl]\r\n" +
                                "         + judo pop3 settings\r\n" +
                                "     2.3 Gmail Settings\r\n" +
                                "         + judo gmail add [Username] [Password]\r\n" +
                                "         + judo gmail setup [Username] [Password]\r\n" +
                                "         + judo gmail set [Username] [Password]\r\n" +
                                "         + judo gmail settings\r\n" +
                                "     2.4 Send\r\n" +
                                "         + judo mail send [From Address] [To Address] `[Subject]` `[Message]`";
            const
            string _sms = "3.  SMS\r\n" +
                                "     3.1 Settings\r\n" +
                                "         + judo sms add [Api Id] [Username] [Password]\r\n" +
                                "         + judo sms setup [Api Id] [Username] [Password]\r\n" +
                                "         + judo sms set [Api Id] [Username] [Password]\r\n" +
                                "         + judo sms settings\r\n" +
                                "     3.2 Send\r\n" +
                                "         + judo sms send [Phone Number] [Message]";
            const
            string _schedule = "4.  Scheduler\r\n" +
                                "     4.1 New Schedule\r\n" +
                                "         + judo schedule add [Name] [{single day: e.g.Monday} {d/m/yyyy} {daily} {workdays} {weekend}] [hh:mm] [{Instruction Set} || {Verbal Notification}]\r\n" +
                                "         + judo schedule new [Name] [{single day: e.g.Monday} {d/m/yyyy} {daily} {workdays} {weekend}] [hh:mm] [{Instruction Set} || {Verbal Notification}]\r\n" +
                                "         + judo schedule set [Name] [{single day: e.g.Monday} {d/m/yyyy} {daily} {workdays} {weekend}] [hh:mm] [{Instruction Set} || {Verbal Notification}]\r\n" +
                                "         + judo schedule setup [Name] [{single day: e.g.Monday} {d/m/yyyy} {daily} {workdays} {weekend}] [hh:mm] [{Instruction Set} || {Verbal Notification}]\r\n" +
                                "         + judo schedule add [Name] [{repeat} {timer} {interval}] [Interval in ms] [{Instruction Set} || {Verbal Notification}]\r\n" +
                                "         + judo schedule new [Name] [{repeat} {timer} {interval}] [Interval in ms] [{Instruction Set} || {Verbal Notification}]\r\n" +
                                "         + judo schedule set [Name] [{repeat} {timer} {interval}] [Interval in ms] [{Instruction Set} || {Verbal Notification}]\r\n" +
                                "         + judo schedule setup [Name] [{repeat} {timer} {interval}] [Interval in ms] [{Instruction Set} || {Verbal Notification}]\r\n" +
                                "     4.2 Remove Schedule\r\n" +
                                "         + judo schedule remove [Name]\r\n" +
                                "         + judo schedule delete [Name]\r\n" +
                                "         + judo schedule del [Name]\r\n" +
                                "     4.3 Disable Schedule\r\n" +
                                "         + judo schedule disable [Name]\r\n" +
                                "         + judo schedule deactivate [Name]\r\n" +
                                "         + judo schedule stop [Name]\r\n" +
                                "         + judo schedule off [Name]\r\n" +
                                "     4.4 Enable Schedule\r\n" +
                                "         + judo schedule enable [Name]\r\n" +
                                "         + judo schedule activate [Name]\r\n" +
                                "         + judo schedule start [Name]\r\n" +
                                "         + judo schedule on [Name]\r\n" +
                                "     4.5 Remove All Schedules\r\n" +
                                "         + judo schedule remove-all\r\n" +
                                "         + judo schedule delete-all\r\n" +
                                "         + judo schedule del-all\r\n" +
                                "         + judo schedule cleanup\r\n" +
                                "         + judo schedule clear\r\n" +
                                "         + judo schedule empty\r\n" +
                                "     4.6 Disable All Schedules\r\n" +
                                "         + judo schedule disable-all\r\n" +
                                "         + judo schedule deactivate-all\r\n" +
                                "         + judo schedule stop-all\r\n" +
                                "         + judo schedule off-all\r\n" +
                                "     4.7 Enable All Schedules\r\n" +
                                "         + judo schedule enable-all\r\n" +
                                "         + judo schedule activate-all\r\n" +
                                "         + judo schedule start-all\r\n" +
                                "         + judo schedule on-all\r\n" +
                                "     4.8 List Actives [ Names ]\r\n" +
                                "         + judo schedule active\r\n" +
                                "         + judo schedule actives\r\n" +
                                "         + judo schedule active-list\r\n" +
                                "         + judo schedule active-ls\r\n" +
                                "         + judo schedule list-actives\r\n" +
                                "         + judo schedule ls-actives\r\n" +
                                "     4.9 List Inactives [ Names ]\r\n" +
                                "         + judo schedule inactive\r\n" +
                                "         + judo schedule inactives\r\n" +
                                "         + judo schedule inactive-list\r\n" +
                                "         + judo schedule inactive-ls\r\n" +
                                "         + judo schedule list-inactives\r\n" +
                                "         + judo schedule ls-inactives\r\n" +
                                "     4.10 List All [ Names ]\r\n" +
                                "         + judo schedule names\r\n" +
                                "         + judo schedule name-list\r\n" +
                                "         + judo schedule name-ls\r\n" +
                                "         + judo schedule list-names\r\n" +
                                "         + judo schedule ls-names\r\n" +
                                "     4.11 List Actives [ Details ]\r\n" +
                                "         + judo schedule active-details\r\n" +
                                "         + judo schedule actives-details\r\n" +
                                "         + judo schedule active-list-details\r\n" +
                                "         + judo schedule active-ls-details\r\n" +
                                "         + judo schedule list-actives-details\r\n" +
                                "         + judo schedule ls-actives-details\r\n" +
                                "     4.12 List Inactives [ Details ]\r\n" +
                                "         + judo schedule inactive-details\r\n" +
                                "         + judo schedule inactives-details\r\n" +
                                "         + judo schedule inactive-list-details\r\n" +
                                "         + judo schedule inactive-ls-details\r\n" +
                                "         + judo schedule list-inactives-details\r\n" +
                                "         + judo schedule ls-inactives-details\r\n" +
                                "     4.13 List All [ Details ]\r\n" +
                                "         + judo schedule details [Name (optional)]\r\n" +
                                "         + judo schedule list [Name (optional)]\r\n" +
                                "         + judo schedule ls [Name (optional)]\r\n" +
                                "         + judo schedule status [Name (optional)]\r\n" +
                                "         + judo schedule state [Name (optional)]";
            const
            string _socket = "5.  Socket Communication\r\n" +
                                "     5.1 Start Service\r\n" +
                                "         + judo socket start\r\n" +
                                "         + judo socket enable\r\n" +
                                "         + judo socket on\r\n" +
                                "         + judo socket open\r\n" +
                                "         + judo socket listen\r\n" +
                                "     5.2 Stop Service\r\n" +
                                "         + judo socket stop\r\n" +
                                "         + judo socket disable\r\n" +
                                "         + judo socket off\r\n" +
                                "         + judo socket close\r\n" +
                                "     5.3 Setup\r\n" +
                                "         + judo socket set [Host] [Port]\r\n" +
                                "         + judo socket setup [Host] [Port]\r\n" +
                                "     5.4 Settings\r\n" +
                                "         + judo socket settings\r\n" +
                                "     5.5 Status\r\n" +
                                "         + judo socket status\r\n" +
                                "         + judo socket state";
            const
            string _server = "6.  Web Server\r\n" +
                                "     6.1 Start\r\n" +
                                "         + judo server start\r\n" +
                                "         + judo server enable\r\n" +
                                "         + judo server on\r\n" +
                                "         + judo server listen\r\n" +
                                "     6.2 Stop\r\n" +
                                "         + judo server stop\r\n" +
                                "         + judo server disable\r\n" +
                                "         + judo server off\r\n" +
                                "     6.3 Change Login\r\n" +
                                "         + judo server login [Username] [Password]\r\n" +
                                "         + judo server cred [Username] [Password]\r\n" +
                                "         + judo server credentials [Username] [Password]\r\n" +
                                "     6.4 Setup\r\n" +
                                "         + judo server set [Host] [Port] [Authentication]\r\n" +
                                "         + judo server setup [Host] [Port] [Authentication]\r\n" +
                                "     6.5 Settings\r\n" +
                                "         + judo server settings\r\n" +
                                "     6.6 Status\r\n" +
                                "         + judo server status\r\n" +
                                "         + judo server state";
            const
            string _serial = "7.  Serial Port\r\n" +
                                "     7.1 Open\r\n" +
                                "         + judo serial open [Port (optional)]\r\n" +
                                "     7.2 Close\r\n" +
                                "         + judo serial close\r\n" +
                                "     7.3 Send Command\r\n" +
                                "         + judo serial send [Command] [Timeout in ms (optional)]\r\n" +
                                "     7.4 Setup\r\n" +
                                "         + judo serial set [Port] [Baud]\r\n" +
                                "         + judo serial setup [Port] [Baud]\r\n" +
                                "     7.5 Settings\r\n" +
                                "         + judo serial settings\r\n" +
                                "     7.6 Status\r\n" +
                                "         + judo serial status\r\n" +
                                "         + judo serial state\r\n" +
                                "     7.7 Listen/Monitor\r\n" +
                                "         + judo serial listen [Timeout in ms (optional)]\r\n" +
                                "         + judo serial monitor [Timeout in ms (optional)]";
            const
            string _cloud = "8.  Web Services\r\n" +
                                "     8.1 Json Setup\r\n" +
                                "         + judo json add [ID] <lock>[Endpoint]</lock> [Node]\r\n" +
                                "         + judo json new [ID] <lock>[Endpoint]</lock> [Node]\r\n" +
                                "         + judo json set [ID] <lock>[Endpoint]</lock> [Node]\r\n" +
                                "         + judo json setup [ID] <lock>[Endpoint]</lock> [Node]\r\n" +
                                "     8.2 Json Response\r\n" +
                                "         + judo json get <lock>[Endpoint]</lock> [Node]\r\n" +
                                "         + judo json response <lock>[Endpoint]</lock> [Node]\r\n" +
                                "         + judo json consume <lock>[Endpoint]</lock> [Node]\r\n" +
                                "         + judo json extract <lock>[Endpoint]</lock> [Node]\r\n" +
                                "     8.3 Xml Setup [ Simple ]\r\n" +
                                "         + judo xml add [ID] <lock>[Endpoint]</lock> [Node] [Attribute (optional)]\r\n" +
                                "         + judo xml new [ID] <lock>[Endpoint]</lock> [Node] [Attribute (optional)]\r\n" +
                                "         + judo xml set [ID] <lock>[Endpoint]</lock> [Node] [Attribute (optional)]\r\n" +
                                "         + judo xml setup [ID] <lock>[Endpoint]</lock> [Node] [Attribute (optional)]\r\n" +
                                "     8.4 Xml Setup [ Namespace Prefix & Uri ]\r\n" +
                                "         + judo xml add [ID] <lock>[Endpoint]</lock> [Ns+Uri] [Node] [Attribute (optional)]\r\n" +
                                "         + judo xml new [ID] <lock>[Endpoint]</lock> [Ns+Uri] [Node] [Attribute (optional)]\r\n" +
                                "         + judo xml set [ID] <lock>[Endpoint]</lock> [Ns+Uri] [Node] [Attribute (optional)]\r\n" +
                                "         + judo xml setup [ID] <lock>[Endpoint]</lock> [Ns+Uri] [Node] [Attribute (optional)]\r\n" +
                                "     8.5 Xml Response [ Simple ]\r\n" +
                                "         + judo xml get <lock>[Endpoint]</lock> [Node] [Attribute (optional)]\r\n" +
                                "         + judo xml response <lock>[Endpoint]</lock> [Node] [Attribute (optional)]\r\n" +
                                "         + judo xml consume <lock>[Endpoint]</lock> [Node] [Attribute (optional)]\r\n" +
                                "         + judo xml extract <lock>[Endpoint]</lock> [Node] [Attribute (optional)]\r\n" +
                                "     8.6 Xml Response [ Namespace Prefix & Uri ]\r\n" +
                                "         + judo xml get <lock>[Endpoint]</lock> [Ns+Uri] [Node] [Attribute (optional)]\r\n" +
                                "         + judo xml response <lock>[Endpoint]</lock> [Ns+Uri] [Node] [Attribute (optional)]\r\n" +
                                "         + judo xml consume <lock>[Endpoint]</lock> [Ns+Uri] [Node] [Attribute (optional)]\r\n" +
                                "         + judo xml extract <lock>[Endpoint]</lock> [Ns+Uri] [Node] [Attribute (optional)]";
            const
            string _http = "9.  Http\r\n" +
                                "     9.1 Get\r\n" +
                                "         + judo http get [Request-URI]";
            const
            string _weather = "10. Weather\r\n" +
                                "     10.1 Setup\r\n" +
                                "         + judo weather set <lock>[Endpoint]</lock>\r\n" +
                                "         + judo weather setup <lock>[Endpoint]</lock>\r\n" +
                                "     10.2 Settings\r\n" +
                                "         + judo weather settings";
            const
            string _ping = "11. Ping\r\n" +
                                "     11.1 Default Timeout [ 1000ms ]\r\n" +
                                "         + judo ping [Host]\r\n" +
                                "     11.2 Specific Timeout\r\n" +
                                "         + judo ping [Host] [Timeout]";
            const
            string _help = "12. Help\r\n" +
                                "     12.1 Preview All\r\n" +
                                "         + judo help\r\n" +
                                "         + judo ?\r\n" +
                                "     12.2 Preview Specific Category\r\n" +
                                "         + judo help [help keyword]\r\n" +
                                "         + judo ? [help keyword]\r\n\r\n" +
                                "(*) Brackets are mandatory when place a sentence as one argument.\r\n" +
                                "(**) <lock>parser protected action</lock> Lock tags used to bypass parsing an action.\r\n" +
                                "(***) Help Keywords: inset, event, mail, sms, schedule, socket, server, serial, cloud, http, weather, ping, help";
            const
            string _all = _inset + "\r\n" +
                          _mail + "\r\n" +
                          _sms + "\r\n" +
                          _schedule + "\r\n" +
                          _socket + "\r\n" +
                          _server + "\r\n" +
                          _serial + "\r\n" +
                          _cloud + "\r\n" +
                          _http + "\r\n" +
                          _weather + "\r\n" +
                          _ping + "\r\n" +
                          _help + "\r\n";

            switch (appendix) {
                case "inset":
                case "event":
                    return _inset;
                case "mail":
                    return _mail;
                case "sms":
                    return _sms;
                case "schedule":
                    return _schedule;
                case "socket":
                    return _socket;
                case "server":
                    return _server;
                case "serial":
                    return _serial;
                case "cloud":
                    return _cloud;
                case "http":
                    return _http;
                case "weather":
                    return _weather;
                case "ping":
                    return _ping;
                case "help":
                    return _help;
                default:
                    return _all;
            }
        }
    }
}
