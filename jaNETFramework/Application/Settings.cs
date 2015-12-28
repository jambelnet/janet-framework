/* *****************************************************************************************************************************
 * (c) J@mBeL.net 2010-2015
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
using System.Collections.Generic;
using System.IO;

namespace jaNETFramework
{
    class Settings
    {
        internal IList<String> LoadSettings(string fileName)
        {
            string fullPath = Methods.Instance.GetApplicationPath() + fileName;

            if (File.Exists(fullPath))
            {
                string input;
                var args = new List<String>();
                using (var tr = new StreamReader(fullPath))
                {
                    while ((input = tr.ReadLine()) != null)
                    {
                        if (input != null)
                            args.Add(RijndaelSimple.Decrypt(input));
                    }
                }
                return args;
            }
            return null;
        }

        internal string SaveSettings(string fileName, string settings)
        {
            try
            {
                string[] args = settings.Split('\n');

                string fullPath = Methods.Instance.GetApplicationPath() + fileName;

                if (File.Exists(fullPath))
                    File.Delete(fullPath);

                using (var tw = new StreamWriter(fullPath))
                {
                    for (int i = 0; i < args.Length; i++)
                        tw.WriteLine(RijndaelSimple.Encrypt(args[i].Trim()));
                }
                return "Settings saved";
            }
            catch
            {
                return "Fail to save settings";
            }
        }
    }

    class ApplicationSettings
    {
        internal struct ApplicationStructure
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

            internal const string YahooWeatherFeed = "YahooForecastFeed";

            internal const string ComPortPath = SystemCommRoot +
                                                "/" + ComPortElement;
            internal const string ComBaudRatePath = SystemCommRoot +
                                                "/" + ComBaudRateElement;
            internal const string LocalHostPath = SystemCommRoot +
                                                "/" + LocalHostElement;
            internal const string LocalPortPath = SystemCommRoot +
                                                "/" + LocalPortElement;
            internal const string TrustedPath = SystemCommRoot +
                                                "/" + TrustedElement;
            internal const string HttpHostNamePath = SystemCommRoot +
                                                "/" + HttpHostNameElement;
            internal const string HttpPortPath = SystemCommRoot +
                                                "/" + HttpPortElement;
            internal const string HttpAuthenticationPath = SystemCommRoot +
                                                "/" + HttpAuthenticationElement;
            internal const string YahooWeatherFeedPath = SystemOthersRoot +
                                                "/" + YahooWeatherFeed;
        }

        internal string ComPort { get; private set; }
        internal string Baud { get; private set; }
        internal string LocalHost { get; private set; }
        internal string LocalPort { get; private set; }
        internal string HostName { get; private set; }
        internal string HttpPort { get; private set; }
        internal string Authentication { get; private set; }
        internal string YahooWeather { get; private set; }

        internal ApplicationSettings()
        {
            if (!File.Exists(Methods.Instance.GetApplicationPath() + "AppConfig.xml"))
            {
                Logger.Instance.Append("obj [ ApplicationSettings Constructor ]: arg [ AppConfig.xml, not found. ]");
                return;
            }
            if (Helpers.Xml.AppConfigQuery(ApplicationStructure.ComPortPath).Count > 0)
                ComPort = Helpers.Xml.AppConfigQuery(
                    ApplicationStructure.ComPortPath)
                    .Item(0).InnerText;
            if (Helpers.Xml.AppConfigQuery(ApplicationStructure.ComBaudRatePath).Count > 0)
                Baud = Helpers.Xml.AppConfigQuery(
                    ApplicationStructure.ComBaudRatePath)
                    .Item(0).InnerText;
            if (Helpers.Xml.AppConfigQuery(ApplicationStructure.LocalHostPath).Count > 0)
                LocalHost = Helpers.Xml.AppConfigQuery(
                    ApplicationStructure.LocalHostPath)
                    .Item(0).InnerText;
            if (Helpers.Xml.AppConfigQuery(ApplicationStructure.LocalPortPath).Count > 0)
                LocalPort = Helpers.Xml.AppConfigQuery(
                    ApplicationStructure.LocalPortPath)
                    .Item(0).InnerText;
            if (Helpers.Xml.AppConfigQuery(ApplicationStructure.HttpHostNamePath).Count > 0)
                HostName = Helpers.Xml.AppConfigQuery(
                    ApplicationStructure.HttpHostNamePath)
                    .Item(0).InnerText;
            if (Helpers.Xml.AppConfigQuery(ApplicationStructure.HttpPortPath).Count > 0)
                HttpPort = Helpers.Xml.AppConfigQuery(
                    ApplicationStructure.HttpPortPath)
                    .Item(0).InnerText;
            if (Helpers.Xml.AppConfigQuery(ApplicationStructure.HttpAuthenticationPath).Count > 0)
                Authentication = Helpers.Xml.AppConfigQuery(
                    ApplicationStructure.HttpAuthenticationPath)
                    .Item(0).InnerText;
            if (Helpers.Xml.AppConfigQuery(ApplicationStructure.YahooWeatherFeedPath).Count > 0)
                YahooWeather = Helpers.Xml.AppConfigQuery(
                    ApplicationStructure.YahooWeatherFeedPath)
                    .Item(0).InnerText;
        }
    }
}
