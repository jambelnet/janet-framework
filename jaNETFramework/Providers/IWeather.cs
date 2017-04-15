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

namespace jaNET.Providers
{
    interface IWeather
    {
        string TodayConditions { get; set; }
        string TodayLow { get; set; }
        string TodayHigh { get; set; }
        string TodayDay { get; }
        string TomorrowConditions { get; set; }
        string TomorrowLow { get; set; }
        string TomorrowHigh { get; set; }
        string TomorrowDay { get; }
        string CurrentTemp { get; set; }
        string CurrentPressure { get; set; }
        string CurrentHumidity { get; set; }
        string CurrentCity { get; set; }
        string WeatherIcon { get; set; }
    }
}
