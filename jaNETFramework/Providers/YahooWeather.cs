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
using System.Collections.Generic;
using System.Xml;

namespace jaNETFramework
{
    [Obsolete]
    public class YahooWeather : IWeather
    {
        public string TodayConditions { get; set; }
        public string TodayLow { get; set; }
        public string TodayHigh { get; set; }
        public string TodayDay { get; set; }
        public string TomorrowConditions { get; set; }
        public string TomorrowLow { get; set; }
        public string TomorrowHigh { get; set; }
        public string TomorrowDay { get; set; }
        public string CurrentTemp { get; set; }
        public string CurrentPressure { get; set; }
        public string CurrentHumidity { get; set; }
        public string CurrentCity { get; set; }
        public string WeatherIcon { get; set; }

        public YahooWeather()
        {
            Action getWeather = () =>
            {
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
                catch
                {
                    // Suppress
                }
            };
            Process.CallWithTimeout(getWeather, 10000);
        }
    }
}
