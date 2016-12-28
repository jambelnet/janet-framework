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

    Project jaNET is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with Project jaNET. If not, see <http://www.gnu.org/licenses/>. */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;
using static jaNETFramework.Server.Web.Request;

namespace jaNETFramework
{
    public static class ExtensionMethods
    {
        public static string Parse(this string args) {
            return Parser.Instance.Parse(args, DataType.text, false);
        }

        internal static string ToJson(this object res) {
            return new JavaScriptSerializer().Serialize(res);
        }

        //public static string ToDebugString<TKey, TValue>(this IList<KeyValuePair<TKey, TValue>> dictionary)
        internal static string ToDebugString<TKey, TValue>(this IDictionary<string, KeyValuePair<TKey, TValue>> dictionary) {
            return string.Join("\r\n", dictionary.Select(kv => kv.Value.Value).ToArray());
        }

        internal static Schedule ToSchedule(this string rawSchedule) {
            string[] args = ParsingTools.SplitArguments(rawSchedule);
            var s = new Schedule {
                Name = args[0],
                Date = args[1].ToLower().FixScheduleDate(),
                Time = args[2],
                Action = args[3].Replace("\"", string.Empty)
                                .Replace("'", string.Empty),
            };
            s.Status = args.Length > 4 ? s.Status = Convert.ToBoolean(args[4]) : s.Status = Convert.ToBoolean(Schedule.State.Enable);
            return s;
        }

        internal static string ToHour24(this string hour) {
            DateTime dt = DateTime.ParseExact(hour, "h:mm tt",
                                              CultureInfo.InvariantCulture);
            return String.Format("{0:HH:mm}", dt);
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
                SerialComm.DeactivateSerialPort();
                Parser.ParserState = false;
                context = context.Replace("%exit%", string.Empty)
                                 .Replace("%quit%", string.Empty);
            }
            if (context.Contains("%clear%") || context.Contains("%cls%")) {
                Console.Clear();
                context = context.Replace("%clear%", string.Empty)
                                 .Replace("%cls%", string.Empty);
            }
            if (context.Contains("%mute%")) {
                Parser.Mute = true;
                context = context.Replace("%mute%", string.Empty);
            }
            if (context.Contains("%unmute%")) {
                Parser.Mute = false;
                context = context.Replace("%unmute%", string.Empty);
            }
            if (context.Contains("%inet%") || context.Contains("%inetcon%")) {
                String con = method.HasInternetConnection().ToString();
                context = context.Replace("%inet%", con)
                                 .Replace("%inetcon%", con);
            }
            if (context.Contains("%gmailcount%") || context.Contains("%gcount%"))
                context = context.Replace("%gmailcount%", new Net.Mail().GmailCheck(true))
                                 .Replace("%gcount%", new Net.Mail().GmailCheck(true));
            if (context.Contains("%gmailreader%") || context.Contains("%gmailheaders%") || context.Contains("%greader%") || context.Contains("%gheaders%"))
                context = context.Replace("%gmailreader%", new Net.Mail().GmailCheck(false))
                                 .Replace("%gmailheaders%", new Net.Mail().GmailCheck(false))
                                 .Replace("%greader%", new Net.Mail().GmailCheck(false))
                                 .Replace("%gheaders%", new Net.Mail().GmailCheck(false));
            if (context.Contains("%pop3count%"))
                context = context.Replace("%pop3count%", new Net.Mail().Pop3Check().ToString());
            if (context.Contains("%user%") || context.Contains("%whoami%")) {
                String whoami = method.WhoAmI();
                context = context.Replace("%user%", whoami)
                                 .Replace("%whoami%", whoami);
            }
            if (context.Contains("%checkin%") || context.Contains("%usercheckin%")) {
                User.Status = true;
                context = context.Replace("%usercheckin%", string.Empty)
                                 .Replace("%checkin%", string.Empty);
            }
            if (context.Contains("%checkout%") || context.Contains("%usercheckout%")) {
                User.Status = false;
                context = context.Replace("%usercheckout%", string.Empty)
                                 .Replace("%checkout%", string.Empty);
            }
            if (context.Contains("%time%"))
                context = context.Replace("%time%", method.GetTime());
            if (context.Contains("%time24%"))
                context = context.Replace("%time24%", method.GetTime24());
            if (context.Contains("%hour%"))
                context = context.Replace("%hour%", method.GetHour());
            if (context.Contains("%minute%"))
                context = context.Replace("%minute%", method.GetMinute());
            if (context.Contains("%date%"))
                context = context.Replace("%date%", method.GetDate());
            if (context.Contains("%calendardate%"))
                context = context.Replace("%calendardate%", method.GetCalendarDate());
            if (context.Contains("%day%"))
                context = context.Replace("%day%", method.GetDay());
            if (context.Contains("%calendarday%"))
                context = context.Replace("%calendarday%", method.GetCalendarDay());
            if (context.Contains("%calendarmonth%"))
                context = context.Replace("%calendarmonth%", method.GetCalendarMonth());
            if (context.Contains("%calendaryear%"))
                context = context.Replace("%calendaryear%", method.GetCalendarYear());
            if (context.Contains("%salute%"))
                context = context.Replace("%salute%", method.GetSalute());
            if (context.Contains("%daypart%") || context.Contains("%partofday%")) {
                String daypart = method.GetPartOfDay(false);
                context = context.Replace("%daypart%", daypart)
                                 .Replace("%partofday%", daypart);
            }
            if (context.Contains("%todayday%"))
                context = context.Replace("%todayday%", weather.TodayDay);
            if (context.Contains("%todayconditions%"))
                context = context.Replace("%todayconditions%", weather.TodayConditions);
            if (context.Contains("%todaylow%"))
                context = context.Replace("%todaylow%", weather.TodayLow);
            if (context.Contains("%todayhigh%"))
                context = context.Replace("%todayhigh%", weather.TodayHigh);
            if (context.Contains("%currenttemperature%") || context.Contains("%currenttemp%") || context.Contains("%todaytemp%") || context.Contains("%todaytemperature%"))
                context = context.Replace("%currenttemperature%", weather.CurrentTemp)
                                 .Replace("%currenttemp%", weather.CurrentTemp)
                                 .Replace("%todaytemp%", weather.CurrentTemp)
                                 .Replace("%todaytemperature%", weather.CurrentTemp);
            if (context.Contains("%currenthumidity%"))
                context = context.Replace("%currenthumidity%", weather.CurrentHumidity);
            if (context.Contains("%currentpressure%"))
                context = context.Replace("%currentpressure%", weather.CurrentPressure);
            if (context.Contains("%currentcity%"))
                context = context.Replace("%currentcity%", weather.CurrentCity);
            if (context.Contains("%weathericon%"))
                context = context.Replace("%weathericon%", weather.WeatherIcon);
            if (context.Contains("%tomorrowday%"))
                context = context.Replace("%tomorrowday%", weather.TomorrowDay);
            if (context.Contains("%tomorrowconditions%"))
                context = context.Replace("%tomorrowconditions%", weather.TomorrowConditions);
            if (context.Contains("%tomorrowlow%"))
                context = context.Replace("%tomorrowlow%", weather.TomorrowLow);
            if (context.Contains("%tomorrowhigh%"))
                context = context.Replace("%tomorrowhigh%", weather.TomorrowHigh);
            if (context.Contains("%whereami%") || context.Contains("%userstat%") || context.Contains("%userstatus%"))
                if (User.Status)
                    context = context.Replace("%whereami%", "present")
                                     .Replace("%userstat%", "present")
                                     .Replace("%userstatus%", "present");
                else
                    context = context.Replace("%whereami%", "absent")
                                     .Replace("%userstat%", "absent")
                                     .Replace("%userstatus%", "absent");
            if (context.Contains("%uptime%"))
                context = context.Replace("%uptime%", Application.Uptime.getAll);
            if (context.Contains("%updays%"))
                context = context.Replace("%updays%", Application.Uptime.getDays.ToString());
            if (context.Contains("%uphours%"))
                context = context.Replace("%uphours%", Application.Uptime.getHours.ToString());
            if (context.Contains("%upminutes%"))
                context = context.Replace("%upminutes%", Application.Uptime.getMinutes.ToString());
            if (context.Contains("%upseconds%"))
                context = context.Replace("%upseconds%", Application.Uptime.getSeconds.ToString());
            if (context.Contains("%about%") || context.Contains("%copyright%"))
                context = context.Replace("%about%", method.GetCopyright())
                                 .Replace("%copyright%", method.GetCopyright());

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
