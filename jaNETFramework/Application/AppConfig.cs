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

using System.Collections.Generic;
using System.Xml.Serialization;

namespace jaNET.Environment.AppConfig
{
    struct AppStructure
    {
        // Nodes
        internal const string SystemCommRoot = "jaNET/System/Comm";
        internal const string SystemInstructionsRoot = "jaNET/Instructions";
        internal const string SystemEventsRoot = "jaNET/Events";
        internal const string SystemOthersRoot = "jaNET/System/Others";

        internal const string ComPortElement = "ComPort";
        internal const string ComBaudRateElement = "BaudRate";
        internal const string LocalHostElement = "localHost";
        internal const string LocalPortElement = "localPort";
        internal const string TrustedElement = "Trusted";
        internal const string HttpHostNameElement = "Hostname";
        internal const string HttpPortElement = "httpPort";
        internal const string HttpAuthenticationElement = "Authentication";

        internal const string Weather = "Weather";

        internal const string ComPortPath = SystemCommRoot + "/" + ComPortElement;
        internal const string ComBaudRatePath = SystemCommRoot + "/" + ComBaudRateElement;
        internal const string LocalHostPath = SystemCommRoot + "/" + LocalHostElement;
        internal const string LocalPortPath = SystemCommRoot + "/" + LocalPortElement;
        internal const string TrustedPath = SystemCommRoot + "/" + TrustedElement;
        internal const string HttpHostNamePath = SystemCommRoot + "/" + HttpHostNameElement;
        internal const string HttpPortPath = SystemCommRoot + "/" + HttpPortElement;
        internal const string HttpAuthenticationPath = SystemCommRoot + "/" + HttpAuthenticationElement;
        internal const string WeatherPath = SystemOthersRoot + "/" + Weather;
    }

    [XmlRoot(ElementName = "MailHeaders")]
    public class MailHeaders
    {
        [XmlElement(ElementName = "MailFrom")]
        public string MailFrom { get; set; }
        [XmlElement(ElementName = "MailTo")]
        public string MailTo { get; set; }
        [XmlElement(ElementName = "MailSubject")]
        public string MailSubject { get; set; }
    }

    [XmlRoot(ElementName = "Alerts")]
    public class Alerts
    {
        [XmlElement(ElementName = "MailHeaders")]
        public MailHeaders MailHeaders { get; set; }
    }

    [XmlRoot(ElementName = "Comm")]
    public class Comm
    {
        internal string GetLocalHost {
            get {
                string ret = string.Empty;
                if (Helpers.Xml.AppConfigQuery(AppStructure.LocalHostPath).Count > 0)
                    ret = Helpers.Xml.AppConfigQuery(
                        AppStructure.LocalHostPath)
                        .Item(0).InnerText;
                return ret;
            }
        }
        internal string GetLocalPort {
            get {
                string ret = string.Empty;
                if (Helpers.Xml.AppConfigQuery(AppStructure.LocalPortPath).Count > 0)
                    ret = Helpers.Xml.AppConfigQuery(
                        AppStructure.LocalPortPath)
                        .Item(0).InnerText;
                return ret;
            }
        }

        internal string GetHostname {
            get {
                string ret = string.Empty;
                if (Helpers.Xml.AppConfigQuery(AppStructure.HttpHostNamePath).Count > 0)
                    ret = Helpers.Xml.AppConfigQuery(
                        AppStructure.HttpHostNamePath)
                        .Item(0).InnerText;
                return ret;
            }
        }

        internal string GetHttpPort {
            get {
                string ret = string.Empty;
                if (Helpers.Xml.AppConfigQuery(AppStructure.HttpPortPath).Count > 0)
                    ret = Helpers.Xml.AppConfigQuery(
                        AppStructure.HttpPortPath)
                        .Item(0).InnerText;
                return ret;
            }
        }

        internal string GetAuthentication {
            get {
                string ret = string.Empty;
                if (Helpers.Xml.AppConfigQuery(AppStructure.HttpAuthenticationPath).Count > 0)
                    ret = Helpers.Xml.AppConfigQuery(
                        AppStructure.HttpAuthenticationPath)
                        .Item(0).InnerText;
                return ret;
            }
        }

        internal string GetComPort {
            get {
                string ret = string.Empty;
                if (Helpers.Xml.AppConfigQuery(AppStructure.ComPortPath).Count > 0)
                    ret = Helpers.Xml.AppConfigQuery(
                        AppStructure.ComPortPath)
                        .Item(0).InnerText;
                return ret;
            }
        }

        internal string GetBaudRate {
            get {
                string ret = string.Empty;
                if (Helpers.Xml.AppConfigQuery(AppStructure.ComBaudRatePath).Count > 0)
                    ret = Helpers.Xml.AppConfigQuery(
                        AppStructure.ComBaudRatePath)
                        .Item(0).InnerText;
                return ret;
            }
        }

        [XmlElement(ElementName = "Trusted")]
        public string Trusted { get; set; }
        [XmlElement(ElementName = "localHost")]
        public string LocalHost { get; set; }
        [XmlElement(ElementName = "localPort")]
        public string LocalPort { get; set; }
        [XmlElement(ElementName = "Hostname")]
        public string Hostname { get; set; }
        [XmlElement(ElementName = "httpPort")]
        public string HttpPort { get; set; }
        [XmlElement(ElementName = "Authentication")]
        public string Authentication { get; set; }
        [XmlElement(ElementName = "ComPort")]
        public string ComPort { get; set; }
        [XmlElement(ElementName = "BaudRate")]
        public string BaudRate { get; set; }
        [XmlElement(ElementName = "MailKeyword")]
        public string MailKeyword { get; set; }
    }

    [XmlRoot(ElementName = "Others")]
    public class Others
    {
        internal string GetWeather {
            get {
                string ret = string.Empty;
                if (Helpers.Xml.AppConfigQuery(AppStructure.WeatherPath).Count > 0)
                    ret = Helpers.Xml.AppConfigQuery(
                        AppStructure.WeatherPath)
                        .Item(0).InnerText;
                return ret;
            }
        }

        [XmlElement(ElementName = "Weather")]
        public string Weather { get; set; }
    }

    [XmlRoot(ElementName = "System")]
    public class System
    {
        [XmlElement(ElementName = "Alerts")]
        public Alerts Alerts { get; set; }
        [XmlElement(ElementName = "Comm")]
        public Comm Comm { get; set; }
        [XmlElement(ElementName = "Others")]
        public Others Others { get; set; }
    }

    [XmlRoot(ElementName = "event")]
    public class Event
    {
        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; }
        [XmlText]
        public string Action { get; set; }
    }

    [XmlRoot(ElementName = "Events")]
    public class Events
    {
        [XmlElement(ElementName = "event")]
        public List<Event> Event { get; set; }
    }

    [XmlRoot(ElementName = "InstructionSet")]
    public class InstructionSet
    {
        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; }
        [XmlAttribute(AttributeName = "img")]
        public string ThumbnailUrl { get; set; }
        [XmlAttribute(AttributeName = "descr")]
        public string Description { get; set; }
        [XmlAttribute(AttributeName = "shortdescr")]
        public string ShortDescription { get; set; }
        [XmlAttribute(AttributeName = "header")]
        public string Header { get; set; }
        [XmlAttribute(AttributeName = "categ")]
        public string Category { get; set; }
        [XmlAttribute(AttributeName = "ref")]
        public string Reference { get; set; }
        [XmlText]
        public string Action { get; set; }
    }

    [XmlRoot(ElementName = "Instructions")]
    public class Instructions
    {
        [XmlElement(ElementName = "InstructionSet")]
        public List<InstructionSet> InstructionSet { get; set; }
    }

    [XmlRoot(ElementName = "jaNET")]
    public class jaNET
    {
        [XmlElement(ElementName = "System")]
        public System System { get; set; }
        [XmlElement(ElementName = "Events")]
        public Events Events { get; set; }
        [XmlElement(ElementName = "Instructions")]
        public Instructions Instructions { get; set; }
    }
}