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
using System.Xml;

namespace jaNETFramework
{
    class YahooWeather
    {
        internal string TodayConditions { get; private set; }
        internal string TodayLow { get; private set; }
        internal string TodayHigh { get; private set; }
        internal string TodayDay { get; private set; }
        internal string TomorrowConditions { get; private set; }
        internal string TomorrowLow { get; private set; }
        internal string TomorrowHigh { get; private set; }
        internal string TomorrowDay { get; private set; }

        internal YahooWeather()
        {
            Action getWeather = () => {
                try
                {
                    string endpoint = Helpers.Xml.AppConfigQuery("jaNET/System/Others/YahooForecastFeed").Item(0).InnerText;
                    const string ns = "yweather";
                    const string uri = "http://xml.weather.yahoo.com/ns/rss/1.0";
                    const string nodePath = "/rss/channel/item/yweather:forecast";

                    XmlNodeList nodes = Helpers.Xml.SelectNodeList(endpoint, ns, uri, nodePath);
                    var le = new List<String>();

                    foreach (XmlNode node in nodes)
                    {
                        le.Add(node.Attributes["day"].InnerText.GetDay());
                        le.Add(node.Attributes["text"].InnerText);
                        le.Add(node.Attributes["low"].InnerText);
                        le.Add(node.Attributes["high"].InnerText);
                    }
                    //Today conditions
                    TodayDay = le[0];
                    TodayConditions = le[1].Replace("/", ", ");
                    TodayLow = le[2];
                    TodayHigh = le[3];
                    //Tomorrow conditions
                    TomorrowDay = le[4];
                    TomorrowConditions = le[5].Replace("/", ", ");
                    TomorrowLow = le[6];
                    TomorrowHigh = le[7];
                }
                catch {
                    // Suppress
                }
            };
            Process.CallWithTimeout(getWeather, 10000);
        }
    }
}
