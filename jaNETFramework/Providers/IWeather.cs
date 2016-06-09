using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace jaNETFramework
{
    interface IWeather
    {
        string TodayConditions { get; set; }
        string TodayLow { get; set; }
        string TodayHigh { get; set; }
        string TodayDay { get; set; }
        string TomorrowConditions { get; set; }
        string TomorrowLow { get; set; }
        string TomorrowHigh { get; set; }
        string TomorrowDay { get; set; }
        string CurrentTemp { get; set; }
        string CurrentPresure { get; set; }
        string CurrentHumidity { get; set; }
        string CurrentCity { get; set; }
    }
}
