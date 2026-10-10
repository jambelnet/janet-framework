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

namespace jaNET.Commands;

/// <summary>The text of "judo help" and "judo help [topic]".</summary>
internal static class HelpText
{
    /// <param name="topic">inset, event, mail, sms, schedule, socket, server, serial, cloud, http, ddns, weather, ping, mqtt, help - anything else gives everything</param>
    public static string For(string topic) {
            const
            string _inset = "1. Instruction Sets & Events\r\n" +
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
            string _mail = "2. Mail\r\n" +
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
                                "         + judo gmail add [Username] [Password] [FeedUrl] [SmtpHost] [SmtpPort] [SmtpSsl] [Pop3Host] [Pop3Port] [Pop3Ssl] [ImapHost] [ImapPort] [ImapSsl]\r\n" +
                                "         + judo gmail setup [Username] [Password] [FeedUrl] [SmtpHost] [SmtpPort] [SmtpSsl] [Pop3Host] [Pop3Port] [Pop3Ssl] [ImapHost] [ImapPort] [ImapSsl]\r\n" +
                                "         + judo gmail set [Username] [Password] [FeedUrl] [SmtpHost] [SmtpPort] [SmtpSsl] [Pop3Host] [Pop3Port] [Pop3Ssl] [ImapHost] [ImapPort] [ImapSsl]\r\n" +
                                "         + judo gmail settings\r\n" +
                                "     2.4 Mail Header Settings\r\n" +
                                "         + judo mailheaders set `[From]` `[To]` `[Subject]`\r\n" +
                                "         + judo mailheaders setup `[From]` `[To]` `[Subject]`\r\n" +
                                "         + judo mailheaders settings\r\n" +
                                "     2.5 Send\r\n" +
                                "         + judo mail send [From Address] [To Address] `[Subject]` `[Message]`";
            const
            string _sms = "3. SMS\r\n" +
                                "     3.1 Settings\r\n" +
                                "         + judo sms add [Api Id] [Username] [Password]\r\n" +
                                "         + judo sms setup [Api Id] [Username] [Password]\r\n" +
                                "         + judo sms set [Api Id] [Username] [Password]\r\n" +
                                "         + judo sms settings\r\n" +
                                "     3.2 Send\r\n" +
                                "         + judo sms send [Phone Number] `[Message]`";
            const
            string _schedule = "4. Scheduler\r\n" +
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
                                "     4.8 Active List [ Names ]\r\n" +
                                "         + judo schedule active\r\n" +
                                "         + judo schedule actives\r\n" +
                                "         + judo schedule active-list\r\n" +
                                "         + judo schedule active-ls\r\n" +
                                "         + judo schedule list-actives\r\n" +
                                "         + judo schedule ls-actives\r\n" +
                                "     4.9 Inactive List [ Names ]\r\n" +
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
                                "     4.11 Active List [ with Details ]\r\n" +
                                "         + judo schedule active-details\r\n" +
                                "         + judo schedule actives-details\r\n" +
                                "         + judo schedule active-list-details\r\n" +
                                "         + judo schedule active-ls-details\r\n" +
                                "         + judo schedule list-actives-details\r\n" +
                                "         + judo schedule ls-actives-details\r\n" +
                                "     4.12 Inactive List [ with Details ]\r\n" +
                                "         + judo schedule inactive-details\r\n" +
                                "         + judo schedule inactives-details\r\n" +
                                "         + judo schedule inactive-list-details\r\n" +
                                "         + judo schedule inactive-ls-details\r\n" +
                                "         + judo schedule list-inactives-details\r\n" +
                                "         + judo schedule ls-inactives-details\r\n" +
                                "     4.13 List All [ with Details ]\r\n" +
                                "         + judo schedule details [Name (optional)]\r\n" +
                                "         + judo schedule list [Name (optional)]\r\n" +
                                "         + judo schedule ls [Name (optional)]\r\n" +
                                "         + judo schedule status [Name (optional)]\r\n" +
                                "         + judo schedule state [Name (optional)]";
            const
            string _socket = "5. Socket Communication\r\n" +
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
                                "     5.4 Trusted\r\n" +
                                "         + judo socket trust <lock>[Guests (semicolon delimeted, e.g. localhost; 192.168.1.1; etc)]</lock>\r\n" +
                                "         + judo trusted settings\r\n" +
                                "     5.5 Settings\r\n" +
                                "         + judo socket settings\r\n" +
                                "     5.6 Status\r\n" +
                                "         + judo socket status\r\n" +
                                "         + judo socket state";
            const
            string _server = "6. Web Server\r\n" +
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
                                "         + judo server state\r\n" +
                                "     6.7 HTTPS (the plain port then only serves this computer)\r\n" +
                                "         + judo server https on [Port (default 8443)]\r\n" +
                                "         + judo server https off\r\n" +
                                "         + judo server https status\r\n" +
                                "     6.8 HTTPS Certificate (default: one that jaNET makes itself)\r\n" +
                                "         + judo server https cert [File.pfx] [Password]\r\n" +
                                "         + judo server https cert default";
            const
            string _serial = "7. Serial Port\r\n" +
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
            string _cloud = "8. Web Services\r\n" +
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
            string _http = "9. Http\r\n" +
                                "     9.1 Get\r\n" +
                                "         + judo http get [Request-URI]";
            const
            string _ddns = "10. Dynamic Dns\r\n" +
                                "     10.1 Setup\r\n" +
                                "         + judo noip add [Hostname] [Username] [Password]\r\n" +
                                "         + judo noip new [Hostname] [Username] [Password]\r\n" +
                                "         + judo noip set [Hostname] [Username] [Password]\r\n" +
                                "         + judo noip setup [Hostname] [Username] [Password]\r\n" +
                                "     10.2 Settings\r\n" +
                                "         + judo noip settings\r\n" +
                                "     10.3 Update\r\n" +
                                "         + judo noip update [Hostname] [Username] [Password]\r\n" +
                                "     10.4 Update (load settings from file)\r\n" +
                                "         + judo noip update";
            const
            string _weather = "11. Weather\r\n" +
                                "     11.1 Setup\r\n" +
                                "         + judo weather set <lock>[Endpoint]</lock>\r\n" +
                                "         + judo weather setup <lock>[Endpoint]</lock>\r\n" +
                                "     11.2 Settings\r\n" +
                                "         + judo weather settings\r\n" +
                                "     11.3 API Key (kept encrypted)\r\n" +
                                "         + judo weather key [Key]\r\n" +
                                "         + judo weather key\r\n" +
                                "     11.4 Open-Meteo (no API key)\r\n" +
                                "         + judo weather openmeteo [Latitude] [Longitude] <lock>[Location]</lock>\r\n" +
                                "     11.5 Location Label\r\n" +
                                "         + judo weather location <lock>[Location]</lock>\r\n" +
                                "         + judo weather location";
            const
            string _ping = "12. Ping\r\n" +
                                "     12.1 Default Timeout [ 1000ms ]\r\n" +
                                "         + judo ping [Host]\r\n" +
                                "     12.2 Specific Timeout\r\n" +
                                "         + judo ping [Host] [Timeout]";
            const
            string _mqtt = "13. MQTT\r\n" +
                                "     13.1 Broker\r\n" +
                                "         + judo mqtt set [Host] [Port (optional)] [{plain} {tls} {insecure}] [Client Id (optional)]\r\n" +
                                "         + judo mqtt login [Username] [Password]\r\n" +
                                "         + judo mqtt settings\r\n" +
                                "     13.2 Connect (and again after a restart)\r\n" +
                                "         + judo mqtt start\r\n" +
                                "         + judo mqtt on\r\n" +
                                "         + judo mqtt enable\r\n" +
                                "         + judo mqtt connect\r\n" +
                                "     13.3 Disconnect\r\n" +
                                "         + judo mqtt stop\r\n" +
                                "         + judo mqtt off\r\n" +
                                "         + judo mqtt disable\r\n" +
                                "         + judo mqtt disconnect\r\n" +
                                "     13.4 Send a Message\r\n" +
                                "         + judo mqtt publish [Topic] `[Message]`\r\n" +
                                "         + judo mqtt publish [Topic] `[Message]` retain\r\n" +
                                "     13.5 Run an Action when a Message Arrives (inside it: %mqtttopic% %mqttpayload% %mqttnumber%)\r\n" +
                                "         + judo mqtt subscribe [Topic, + is one level and # the rest] <lock>[Action]</lock>\r\n" +
                                "         + judo mqtt unsubscribe [Topic]\r\n" +
                                "         + judo mqtt subscriptions\r\n" +
                                "     13.6 Status\r\n" +
                                "         + judo mqtt status\r\n" +
                                "         + judo mqtt state";
            const
            string _help = "14. Help\r\n" +
                                "     14.1 Preview All\r\n" +
                                "         + judo help\r\n" +
                                "         + judo ?\r\n" +
                                "     14.2 Preview Specific Category\r\n" +
                                "         + judo help [help keyword]\r\n" +
                                "         + judo ? [help keyword]\r\n\r\n" +
                                "(*) Brackets are mandatory when place a sentence as one argument.\r\n" +
                                "(**) <lock>parser protected action</lock> Lock tags used to bypass parsing an action.\r\n" +
                                "(***) Help Keywords: inset, event, mail, sms, schedule, socket, server, serial, cloud, http, ddns, weather, ping, mqtt, help";
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
                          _ddns + "\r\n" +
                          _weather + "\r\n" +
                          _ping + "\r\n" +
                          _mqtt + "\r\n" +
                          _help + "\r\n";

            switch (topic) {
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
                case "ddns":
                    return _ddns;
                case "weather":
                    return _weather;
                case "ping":
                    return _ping;
                case "mqtt":
                    return _mqtt;
                case "help":
                    return _help;
                default:
                    return _all;
            }
    }
}
