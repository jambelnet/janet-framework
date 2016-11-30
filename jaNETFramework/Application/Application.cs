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

namespace jaNETFramework
{
    public static class Application
    {
        static DateTime _Uptime;

        internal static class Uptime
        {
            internal static String getAll {
                get { return "Days[" + getDays + "], Hours[" + getHours + "], Minutes[" + getMinutes + "], Seconds[" + getSeconds + "]"; }
            }

            internal static int getDays {
                get { return (DateTime.Now - _Uptime).Days; }
            }

            internal static int getHours {
                get { return (DateTime.Now - _Uptime).Hours; }
            }

            internal static int getMinutes {
                get { return (DateTime.Now - _Uptime).Minutes; }
            }

            internal static int getSeconds {
                get { return (DateTime.Now - _Uptime).Seconds; }
            }
        }

        public static void Initialize() {
            _Uptime = DateTime.Now;

            string AppPath = Methods.Instance.GetApplicationPath();
            var appset = new ApplicationSettings();

            if (!File.Exists(AppPath + "AppConfig.xml")) {
                Logger.Instance.Append("obj [ Global.Application.Initialize <AppConfig.xml> ]: File not found.");
                return;
            }
            if (!File.Exists(AppPath + ".htaccess"))
                new Settings().SaveSettings(".htaccess", "admin\r\nadmin");

            "%checkin%".ToValues();
            Schedule.Init();
            if (!String.IsNullOrEmpty(appset.HostName))
                Server.Web.Start();
            if (!String.IsNullOrEmpty(appset.LocalHost))
                Server.TCP.Start();
            if (!String.IsNullOrEmpty(appset.ComPort))
                SerialComm.ActivateSerialPort(string.Empty); // throws exception in linux?
        }

        public static void Dispose() {
            SerialComm.DeactivateSerialPort();
            Server.Web.Stop();
            Server.TCP.Stop();
            Parser.ParserState = false;
            Environment.Exit(0);
        }
    }
}
