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

using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace jaNETFramework
{
    class OpenWeather : IWeather
    {
        public string TodayConditions { get; set; }
        public string TodayLow { get; set; }
        public string TodayHigh { get; set; }
        public string TodayDay {
            get {
                return DateTime.Now.DayOfWeek.ToString();
            }
        }
        public string TomorrowConditions { get; set; }
        public string TomorrowLow { get; set; }
        public string TomorrowHigh { get; set; }
        public string TomorrowDay {
            get {
                return DateTime.Now.AddDays(1).DayOfWeek.ToString();
            }
        }
        public string CurrentTemp { get; set; }
        public string CurrentPressure { get; set; }
        public string CurrentHumidity { get; set; }
        public string CurrentCity { get; set; }
        public string WeatherIcon { get; set; }

        public class Coord
        {
            public double lon { get; set; }
            public double lat { get; set; }
        }

        public class Weather
        {
            public int id { get; set; }
            public string main { get; set; }
            public string description { get; set; }
            public string icon { get; set; }
        }

        public class Main
        {
            public double temp { get; set; }
            public int pressure { get; set; }
            public int humidity { get; set; }
            public double temp_min { get; set; }
            public double temp_max { get; set; }
        }

        public class Wind
        {
            public double speed { get; set; }
            public int deg { get; set; }
            public double gust { get; set; }
        }

        public class Clouds
        {
            public int all { get; set; }
        }

        public class Sys
        {
            public int type { get; set; }
            public int id { get; set; }
            public double message { get; set; }
            public string country { get; set; }
            public int sunrise { get; set; }
            public int sunset { get; set; }
        }

        public class RootObject
        {
            public Coord coord { get; set; }
            public List<Weather> weather { get; set; }
            public string @base { get; set; }
            public Main main { get; set; }
            public Wind wind { get; set; }
            public Clouds clouds { get; set; }
            public int dt { get; set; }
            public Sys sys { get; set; }
            public int id { get; set; }
            public string name { get; set; }
            public int cod { get; set; }
        }

        internal OpenWeather() {
            Action getWeather = () => {
                try {
                    string endpoint = Helpers.Xml.AppConfigQuery(ApplicationSettings.ApplicationStructure.WeatherPath).Item(0).InnerText;
                    var oRootObject = new JavaScriptSerializer().Deserialize<RootObject>(Helpers.Http.Get(endpoint));
                    TodayConditions = oRootObject.weather[0].main;
                    TodayHigh = Math.Round(oRootObject.main.temp_max, 1).ToString().Replace(",", ".");
                    TodayLow = Math.Round(oRootObject.main.temp_min, 1).ToString().Replace(",", ".");
                    CurrentCity = oRootObject.name;
                    CurrentTemp = Math.Round(oRootObject.main.temp, 1).ToString().Replace(",", ".");
                    CurrentHumidity = oRootObject.main.humidity.ToString();
                    CurrentPressure = oRootObject.main.pressure.ToString();
                    WeatherIcon = "http://openweathermap.org/img/w/" + oRootObject.weather[0].icon + ".png";
                }
                catch { }
            };
            Process.CallWithTimeout(getWeather, 10000);
        }
    }
}
