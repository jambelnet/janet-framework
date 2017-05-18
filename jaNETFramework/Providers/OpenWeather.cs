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
using System.Runtime.Serialization;
using System.Web.Script.Serialization;

namespace jaNET.Providers
{
    //[DataContract]
    class OpenWeather : IWeather
    {
        //[DataMember]
        public string TodayConditions { get; set; }
        //[DataMember]
        public string TodayLow { get; set; }
        //[DataMember]
        public string TodayHigh { get; set; }
        //[DataMember]
        public string TodayDay {
            get {
                return DateTime.Now.DayOfWeek.ToString();
            }
        }
        //[DataMember]
        public string TomorrowConditions { get; set; }
        //[DataMember]
        public string TomorrowLow { get; set; }
        //[DataMember]
        public string TomorrowHigh { get; set; }
        //[DataMember]
        public string TomorrowDay {
            get {
                return DateTime.Now.AddDays(1).DayOfWeek.ToString();
            }
        }
        //[DataMember]
        public string CurrentTemp { get; set; }
        //[DataMember]
        public string CurrentPressure { get; set; }
        //[DataMember]
        public string CurrentHumidity { get; set; }
        //[DataMember]
        public string CurrentCity { get; set; }
        //[DataMember]
        public string WeatherIcon { get; set; }

        //[DataContract]
        public class Coord
        {
            public double lon { get; set; }
            public double lat { get; set; }
        }

        //[DataContract]
        public class Weather
        {
            //[DataMember]
            public int id { get; set; }
            //[DataMember]
            public string main { get; set; }
            //[DataMember]
            public string description { get; set; }
            //[DataMember]
            public string icon { get; set; }
        }

        //[DataContract]
        public class Main
        {
            //[DataMember]
            public double temp { get; set; }
            //[DataMember]
            public double pressure { get; set; }
            //[DataMember]
            public int humidity { get; set; }
            //[DataMember]
            public double temp_min { get; set; }
            //[DataMember]
            public double temp_max { get; set; }
        }

        //[DataContract]
        public class Wind
        {
            //[DataMember]
            public double speed { get; set; }
            //[DataMember]
            public double deg { get; set; }
            //[DataMember]
            public double gust { get; set; }
        }

        //[DataContract]
        public class Clouds
        {
            //[DataMember]
            public int all { get; set; }
        }

        //[DataContract]
        public class Sys
        {
            //[DataMember]
            public int type { get; set; }
            //[DataMember]
            public int id { get; set; }
            //[DataMember]
            public double message { get; set; }
            //[DataMember]
            public string country { get; set; }
            //[DataMember]
            public int sunrise { get; set; }
            //[DataMember]
            public int sunset { get; set; }
        }

        //[DataContract]
        public class RootObject
        {
            //[DataMember]
            public Coord coord { get; set; }
            //[DataMember]
            public List<Weather> weather { get; set; }
            //[DataMember]
            public string @base { get; set; }
            //[DataMember]
            public Main main { get; set; }
            //[DataMember]
            public Wind wind { get; set; }
            //[DataMember]
            public Clouds clouds { get; set; }
            //[DataMember]
            public int dt { get; set; }
            //[DataMember]
            public Sys sys { get; set; }
            //[DataMember]
            public int id { get; set; }
            //[DataMember]
            public string name { get; set; }
            //[DataMember]
            public int cod { get; set; }
        }

        internal OpenWeather() {
            GetWeather();
        }

        void GetWeather() {
            try {
                string endpoint = Helpers.Xml.AppConfigQuery(AppStructure.WeatherPath).Item(0).InnerText;
                var oRootObject = new JavaScriptSerializer().Deserialize<RootObject>(Helpers.Http.Get(endpoint)); //// JavaScriptSerializer cannot be used in Xamarin.
                //var oRootObject = new Helpers.JsonSerializer().Deserialize<RootObject>(Helpers.Http.Get(endpoint));
                TodayConditions = oRootObject.weather[0].main;
                TodayHigh = Math.Round(oRootObject.main.temp_max, 1).ToString().Replace(",", ".");
                TodayLow = Math.Round(oRootObject.main.temp_min, 1).ToString().Replace(",", ".");
                CurrentCity = oRootObject.name;
                CurrentTemp = Math.Round(oRootObject.main.temp, 1).ToString().Replace(",", ".");
                CurrentHumidity = oRootObject.main.humidity.ToString();
                CurrentPressure = oRootObject.main.pressure.ToString().Replace(",", ".");
                WeatherIcon = "http://openweathermap.org/img/w/" + oRootObject.weather[0].icon + ".png";
            }
            catch (Exception e) {
                //Logger.Instance.Append(string.Format("obj [ OpenWeather.OpenWeather <Exception> ] Exception Message: [ {0} ]", e.Message));
            }
        }
    }
}