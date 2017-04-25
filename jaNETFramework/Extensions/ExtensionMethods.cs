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

using jaNET.Environment;
using jaNET.Environment.Core;
using jaNET.IO.Ports;
using jaNET.Net.Http;
using jaNET.Providers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml.Serialization;

namespace jaNET.Extensions
{
    public static class ExtensionMethods
    {
        internal static T ConvertTo<T>(this object input) {
            return (T)Convert.ChangeType(input, typeof(T));
        }

        internal static void SerializeObject(this object obj, string filepath) {
            var WriteFileStream = new StreamWriter(filepath);
            var x = new XmlSerializer(obj.GetType());
            x.Serialize(WriteFileStream, obj); //(Console.Out, obj);
            WriteFileStream.Close();
        }

        public static string Parse(this string args) {
            return Parser.Instance.Parse(args, WebServer.Request.DataType.text, false);
        }

        internal static string ToJson(this object res) {
            return new JavaScriptSerializer().Serialize(res);
        }

        internal static string ToDictString<TKey, TValue>(this IEnumerable<KeyValuePair<string, KeyValuePair<TKey, TValue>>> dictionary) {
            return string.Join("\r\n", dictionary.Select(kv => kv.Value.Value).ToList());
        }

        internal static Schedule ToSchedule(this string rawSchedule) {
            List<String> args = rawSchedule.SplitArguments();
            var s = new Schedule {
                Name = args[0],
                Date = args[1].ToLower().FixScheduleDate(),
                Time = args[2],
                Action = args[3].Replace("\"", string.Empty)
                                .Replace("'", string.Empty),
            };
            s.Status = args.Count > 4 ? s.Status = Convert.ToBoolean(args[4]) : s.Status = Convert.ToBoolean(Schedule.State.Enable);
            return s;
        }

        internal static string ToHour24(this string hour) {
            DateTime dt = DateTime.ParseExact(hour, "h:mm tt", CultureInfo.InvariantCulture);
            return string.Format("{0:HH:mm}", dt);
        }

        internal static SerialComm.TypeOfSerialMessage ToTypeOfSerialMessage(this string type) {
            SerialComm.TypeOfSerialMessage t = SerialComm.TypeOfSerialMessage.None;

            switch (type.ToLower()) {
                case "send":
                    t = SerialComm.TypeOfSerialMessage.Send;
                    break;
                case "listen":
                    t = SerialComm.TypeOfSerialMessage.Listen;
                    break;
                case "monitor":
                    t = SerialComm.TypeOfSerialMessage.Monitor;
                    break;
            }
            return t;
        }

        internal static string FixScheduleDate(this string date) {
            try {
                var dt = DateTime.ParseExact(date.Replace("-", "/").Replace(".", "/"), "dd/MM/yyyy",
                                             CultureInfo.InvariantCulture);
                return dt.ToString("d/M/yyyy", CultureInfo.InvariantCulture);
            }
            catch {
                return date;
            }
        }

        internal static string GetDay(this string day) {
            switch (day) {
                case "Sun":
                    return "Sunday";
                case "Mon":
                    return "Monday";
                case "Tue":
                    return "Tuesday";
                case "Wed":
                    return "Wednesday";
                case "Thu":
                    return "Thursday";
                case "Fri":
                    return "Friday";
                case "Sat":
                    return "Saturday";
            }
            return string.Empty;
        }

        internal static string ToValues(this string context) {
            var method = Methods.Instance;
            IWeather weather = new OpenWeather();

            if (context.Contains("%exit%") || context.Contains("%quit%")) {
                Application.Dispose();
            }
            if (context.Contains("%clear%") || context.Contains("%cls%")) {
                Console.Clear();
            }
            if (context.Contains("%mute%")) {
                Parser.Mute = true;
            }
            if (context.Contains("%unmute%")) {
                Parser.Mute = false;
            }
            if (context.Contains("%checkin%") || context.Contains("%usercheckin%")) {
                User.Status = true;
            }
            if (context.Contains("%checkout%") || context.Contains("%usercheckout%")) {
                User.Status = false;
            }
            if (context.Contains("%inet%") || context.Contains("%inetcon%")) {
                context = Regex.Replace(context, "%inet%|%inetcon%", method.HasInternetConnection().ToString());
            }
            if (context.Contains("%gmailcount%") || context.Contains("%gcount%")) {
                context = Regex.Replace(context, "%gmailcount%|%gcount%", new Net.Mail().GmailCheck(true));
            }
            if (Regex.IsMatch(context, "%gmailreader%|%gmailheaders%|%greader%|%gheaders%")) {
                context = Regex.Replace(context, "%gmailreader%|%gmailheaders%|%greader%|%gheaders%", new Net.Mail().GmailCheck(false));
            }
            if (context.Contains("%pop3count%")) {
                context = Regex.Replace(context, "%pop3count%", new Net.Mail().Pop3Check().ToString());
            }
            if (context.Contains("%user%") || context.Contains("%whoami%")) {
                context = Regex.Replace(context, "%user%|%whoami%", method.WhoAmI);
            }
            if (context.Contains("%time%")) {
                context = Regex.Replace(context, "%time%", method.GetTime);
            }
            if (context.Contains("%time24%")) {
                context = Regex.Replace(context, "%time24%", method.GetTime24);
            }
            if (context.Contains("%hour%")) {
                context = Regex.Replace(context, "%hour%", method.GetHour);
            }
            if (context.Contains("%minute%")) {
                context = Regex.Replace(context, "%minute%", method.GetMinute);
            }
            if (context.Contains("%date%")) {
                context = Regex.Replace(context, "%date%", method.GetDate);
            }
            if (context.Contains("%calendardate%")) {
                context = Regex.Replace(context, "%calendardate%", method.GetCalendarDate);
            }
            if (context.Contains("%day%")) {
                context = Regex.Replace(context, "%day%", method.GetDay);
            }
            if (context.Contains("%calendarday%")) {
                context = Regex.Replace(context, "%calendarday%", method.GetCalendarDay);
            }
            if (context.Contains("%calendarmonth%")) {
                context = Regex.Replace(context, "%calendarmonth%", method.GetCalendarMonth);
            }
            if (context.Contains("%calendaryear%")) {
                context = Regex.Replace(context, "%calendaryear%", method.GetCalendarYear);
            }
            if (context.Contains("%salute%")) {
                context = Regex.Replace(context, "%salute%", method.GetSalute);
            }
            if (context.Contains("%daypart%") || context.Contains("%partofday%")) {
                context = Regex.Replace(context, "%daypart%|%partofday%", method.GetPartOfDay(false));
            }
            if (context.Contains("%todayday%")) {
                context = Regex.Replace(context, "%todayday%", weather.TodayDay);
            }
            if (context.Contains("%todayconditions%")) {
                context = Regex.Replace(context, "%todayconditions%", weather.TodayConditions);
            }
            if (context.Contains("%todaylow%")) {
                context = Regex.Replace(context, "%todaylow%", weather.TodayLow);
            }
            if (context.Contains("%todayhigh%")) {
                context = Regex.Replace(context, "%todayhigh%", weather.TodayHigh);
            }
            if (Regex.IsMatch(context, "%currenttemperature%|%currenttemp%|%todaytemp%|%todaytemperature%")) {
                context = Regex.Replace(context, "%currenttemperature%|%currenttemp%|%todaytemp%|%todaytemperature%", weather.CurrentTemp);
            }
            if (context.Contains("%currenthumidity%")) {
                context = Regex.Replace(context, "%currenthumidity%", weather.CurrentHumidity);
            }
            if (context.Contains("%currentpressure%")) {
                context = Regex.Replace(context, "%currentpressure%", weather.CurrentPressure);
            }
            if (context.Contains("%currentcity%")) {
                context = Regex.Replace(context, "%currentcity%", weather.CurrentCity);
            }
            if (context.Contains("%weathericon%")) {
                context = Regex.Replace(context, "%weathericon%", weather.WeatherIcon);
            }
            if (context.Contains("%tomorrowday%")) {
                context = Regex.Replace(context, "%tomorrowday%", weather.TomorrowDay);
            }
            if (context.Contains("%tomorrowconditions%")) {
                context = Regex.Replace(context, "%tomorrowconditions%", weather.TomorrowConditions);
            }
            if (context.Contains("%tomorrowlow%")) {
                context = Regex.Replace(context, "%tomorrowlow%", weather.TomorrowLow);
            }
            if (context.Contains("%tomorrowhigh%")) {
                context = Regex.Replace(context, "%tomorrowhigh%", weather.TomorrowHigh);
            }
            if (context.Contains("%whereami%") || context.Contains("%userstat%") || context.Contains("%userstatus%")) {
                string us;
                if (User.Status)
                    us = "present";
                else
                    us = "absent";
                context = Regex.Replace(context, "%whereami%|%userstat%|%userstatus%", us);
            }
            if (context.Contains("%uptime%")) {
                context = Regex.Replace(context, "%uptime%", Application.Uptime.GetAll);
            }
            if (context.Contains("%updays%")) {
                context = Regex.Replace(context, "%updays%", Application.Uptime.GetDays.ToString());
            }
            if (context.Contains("%uphours%")) {
                context = Regex.Replace(context, "%uphours%", Application.Uptime.GetHours.ToString());
            }
            if (context.Contains("%upminutes%")) {
                context = Regex.Replace(context, "%upminutes%", Application.Uptime.GetMinutes.ToString());
            }
            if (context.Contains("%upseconds%")) {
                context = Regex.Replace(context, "%upseconds%", Application.Uptime.GetSeconds.ToString());
            }
            if (context.Contains("%about%") || context.Contains("%copyright%")) {
                context = Regex.Replace(context, "%about%|%copyright%", method.GetCopyright);
            }
            if (context.Contains("%apppath%") || context.Contains("%applicationpath%")) {
                context = Regex.Replace(context, "%apppath%|%applicationpath%", method.GetApplicationPath);
            }
            context = Regex.Replace(context, "%clear%|%cls%|%mute%|%unmute%|%checkin%|%usercheckin%|%checkout%|%usercheckout%", string.Empty);
            // If Event
            if (context.Contains("%~>")) {
                method.GetEvent(context.Replace("%~>", string.Empty)
                                       .Replace("%", string.Empty)).Item(0).InnerText.Parse();
                context = context.Replace(context, string.Empty);
            }
            return context;
        }
    }
}